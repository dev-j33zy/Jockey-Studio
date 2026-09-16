use std::fs::File;
use std::path::Path;
use std::time::Duration;

use symphonia::core::audio::SampleBuffer;
use symphonia::core::codecs::{Decoder, DecoderOptions};
use symphonia::core::errors::Error as SymError;
use symphonia::core::formats::{FormatOptions, FormatReader, SeekMode, SeekTo};
use symphonia::core::io::MediaSourceStream;
use symphonia::core::meta::MetadataOptions;
use symphonia::core::probe::Hint;
use thiserror::Error;

/// Metadata summary gathered during probe.
#[derive(Debug, Clone)]
pub struct MediaInfo {
    pub title: String,
    pub duration: Option<f64>,
    pub channels: u16,
    pub sample_rate: u32,
    pub container: Option<String>,
    pub codec: Option<String>,
    pub is_video: bool,
}

#[derive(Debug, Error)]
pub enum DecoderError {
    #[error("io error: {0}")]
    Io(#[from] std::io::Error),
    #[error("sym: {0}")]
    Sym(#[from] SymError),
    #[error("no playable audio track")]
    NoAudioTrack,
    #[error("unsupported codec or stream")]
    UnsupportedCodec,
    #[error("seek failed")]
    SeekFailed,
}

/// Streaming audio decoder over symphonia. Outputs interleaved f32.
pub struct MediaDecoder {
    format: Box<dyn FormatReader>,
    track_id: u32,
    decoder: Box<dyn Decoder>,
    pub channels: u16,
    pub sample_rate: u32,
    pub duration_secs: Option<f64>,
    pub container: Option<String>,
    pub codec: Option<String>,
    pub is_video: bool,
    pub title: String,
    sample_buf: Option<SampleBuffer<f32>>,
}

impl MediaDecoder {
    pub fn open(path: &Path) -> Result<Self, DecoderError> {
        let file = File::open(path)?;
        let src = MediaSourceStream::new(Box::new(file), Default::default());
        let hint = Hint::new();
        let probe = symphonia::default::get_probe().format(
            &hint,
            src,
            &FormatOptions::default(),
            &MetadataOptions::default(),
        )?;
        let mut format: Box<dyn FormatReader> = probe.format;

        let track = format
            .tracks()
            .iter()
            .find(|t| t.codec_params.sample_rate.is_some())
            .ok_or(DecoderError::NoAudioTrack)?;
        let track_id = track.id;
        let sample_rate = track
            .codec_params
            .sample_rate
            .or_else(|| track.codec_params.time_base.map(|t| t.denom as u32))
            .ok_or(DecoderError::UnsupportedCodec)?;
        let channels = track
            .codec_params
            .channels
            .map(|c| c.count() as u16)
            .unwrap_or(2);

        let decoder = symphonia::default::get_codecs()
            .make(&track.codec_params, &DecoderOptions::default())
            .map_err(|_| DecoderError::UnsupportedCodec)?;

        let duration = track.codec_params.n_frames.and_then(|f| {
            track
                .codec_params
                .time_base
                .map(|tb| tb.calc_time(f).seconds as f64)
        });

        let codec = format
            .tracks()
            .iter()
            .find(|t| t.id == track_id)
            .map(|t| format!("{:?}", t.codec_params.codec));

        let container = path
            .extension()
            .map(|e| e.to_string_lossy().to_lowercase())
            .or_else(|| {
                format
                    .tracks()
                    .iter()
                    .find(|t| t.id == track_id)
                    .map(|t| format!("{:?}", t.codec_params.codec))
            });

        let is_video = format
            .tracks()
            .iter()
            .any(|t| t.codec_params.sample_rate.is_none());

        // Best-effort title tag.
        let title = format
            .metadata()
            .current()
            .and_then(|m| {
                m.tags()
                    .iter()
                    .find(|t| t.key.to_lowercase().contains("title"))
                    .map(|t| t.value.to_string())
            })
            .unwrap_or_else(|| {
                path.file_stem()
                    .map(|s| s.to_string_lossy().into_owned())
                    .unwrap_or_else(|| "Untitled".to_string())
            });

        Ok(Self {
            format,
            track_id,
            decoder,
            channels: channels.max(1),
            sample_rate: sample_rate.max(1),
            duration_secs: duration,
            container,
            codec,
            is_video,
            sample_buf: None,
            title,
        })
    }

    /// Convenience: probe metadata without keeping a decoder open.
    pub fn probe_info(path: &Path) -> Result<MediaInfo, DecoderError> {
        let d = Self::open(path)?;
        Ok(MediaInfo {
            title: d.title,
            duration: d.duration_secs,
            channels: d.channels,
            sample_rate: d.sample_rate,
            container: d.container,
            codec: d.codec,
            is_video: d.is_video,
        })
    }

    /// Decode one block. Returns `Some` interleaved f32 samples, or `None` at end-of-stream.
    /// A returned block has `len % channels == 0`.
    pub fn read(&mut self) -> Result<Option<Vec<f32>>, DecoderError> {
        loop {
            let packet = match self.format.next_packet() {
                Ok(p) => p,
                Err(SymError::IoError(e)) => {
                    if e.kind() == std::io::ErrorKind::UnexpectedEof {
                        return Ok(None);
                    }
                    return Err(DecoderError::Io(e));
                }
                Err(SymError::DecodeError(_)) => continue,
                Err(e) => return Err(DecoderError::Sym(e)),
            };
            if packet.track_id() != self.track_id {
                continue;
            }
            let decoded = self.decoder.decode(&packet)?;
            let spec = *decoded.spec();
            let cap = decoded.capacity() as u64;
            let sample_buf = self
                .sample_buf
                .get_or_insert_with(|| SampleBuffer::<f32>::new(cap, spec));
            sample_buf.copy_interleaved_ref(decoded);
            return Ok(Some(sample_buf.samples().to_vec()));
        }
    }

    /// Seek to `secs` (absolute). Subsequent `read()` resumes from the keyframe before it.
    pub fn seek(&mut self, secs: f64) -> Result<(), DecoderError> {
        let frames = (secs.max(0.0) * self.sample_rate as f64).round() as u64;
        let to = SeekTo::TimeStamp {
            ts: frames,
            track_id: self.track_id,
        };
        self.format
            .seek(SeekMode::Accurate, to)
            .map_err(|_| DecoderError::SeekFailed)?;
        self.decoder.reset();
        Ok(())
    }
}

/// Probe media file metadata. Missing formats not covered by symphonia
/// return `DecodeError` (FFmpeg not bundled; see README).
pub fn probe(path: &Path) -> Result<MediaInfo, DecoderError> {
    MediaDecoder::probe_info(path)
}

/// Loudness summary produced by a full decode pass.
#[derive(Debug, Clone, Copy)]
pub struct AudioStats {
    /// Overall peak level in decibels (0 dB = full scale).
    pub peak_db: f32,
    /// Root-mean-square level in decibels.
    pub rms_db: f32,
}

/// Decode the audio track of `path` end-to-end and measure overall peak and
/// RMS loudness. Intended to run on a background thread (long files take a
/// while to scan).
pub fn analyze(path: &Path) -> Result<AudioStats, DecoderError> {
    let mut d = MediaDecoder::open(path)?;
    let mut peak: f32 = 0.0;
    let mut sum_sq: f64 = 0.0;
    let mut n: u64 = 0;
    while let Some(block) = d.read()? {
        for &s in &block {
            let a = s.abs();
            if a > peak {
                peak = a;
            }
            sum_sq += (s as f64) * (s as f64);
            n += 1;
        }
    }
    let peak_db = if peak > 0.0 { 20.0 * peak.log10() } else { -120.0 };
    let rms = if n > 0 { (sum_sq / n as f64) as f32 } else { 0.0 };
    let rms_db = if rms > 0.0 { 20.0 * rms.log10() } else { -120.0 };
    Ok(AudioStats { peak_db, rms_db })
}

/// Symphonia-backed playback source that always decodes the *audio* track
/// (so music/video containers both play) and reports a real `total_duration`.
/// Emits interleaved i16 for rodio.
pub struct MediaSource {
    inner: MediaDecoder,
    buf: Vec<f32>,
    pos: usize,
}

impl MediaSource {
    pub fn open(path: &Path) -> Result<Self, DecoderError> {
        Ok(Self {
            inner: MediaDecoder::open(path)?,
            buf: Vec::new(),
            pos: 0,
        })
    }

    /// Seek to `secs` (absolute). Subsequent samples resume from there.
    pub fn seek(&mut self, secs: f64) -> Result<(), DecoderError> {
        self.inner.seek(secs)?;
        self.buf.clear();
        self.pos = 0;
        Ok(())
    }

    fn refill(&mut self) {
        if self.pos < self.buf.len() {
            return;
        }
        self.buf.clear();
        self.pos = 0;
        if let Ok(Some(block)) = self.inner.read() {
            self.buf = block;
        }
    }
}

impl Iterator for MediaSource {
    type Item = i16;

    fn next(&mut self) -> Option<Self::Item> {
        self.refill();
        if self.pos >= self.buf.len() {
            return None;
        }
        let sample = self.buf[self.pos];
        self.pos += 1;
        let clamped = sample.clamp(-1.0, 1.0);
        Some((clamped * i16::MAX as f32) as i16)
    }
}

impl rodio::Source for MediaSource {
    fn current_frame_len(&self) -> Option<usize> {
        Some(self.buf.len().saturating_sub(self.pos))
    }

    fn channels(&self) -> u16 {
        self.inner.channels
    }

    fn sample_rate(&self) -> u32 {
        self.inner.sample_rate
    }

    fn total_duration(&self) -> Option<Duration> {
        self.inner.duration_secs.map(Duration::from_secs_f64)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::io::Write;

    fn write_sine_wav(path: &Path, seconds: u32) {
        let hz: u32 = 440;
        let rate: u32 = 44100;
        let channels: u16 = 2;
        let samples = rate * seconds;
        let bytes_per_sample = 2usize;
        let data_len = samples as usize * channels as usize * bytes_per_sample;
        let mut buf = Vec::with_capacity(44 + data_len);
        buf.extend_from_slice(b"RIFF");
        buf.extend_from_slice(&(36u32 + data_len as u32).to_le_bytes());
        buf.extend_from_slice(b"WAVE");
        buf.extend_from_slice(b"fmt ");
        buf.extend_from_slice(&16u32.to_le_bytes());
        buf.extend_from_slice(&1u16.to_le_bytes()); // PCM
        buf.extend_from_slice(&channels.to_le_bytes());
        buf.extend_from_slice(&rate.to_le_bytes());
        buf.extend_from_slice(&(rate as u32 * channels as u32 * 2).to_le_bytes());
        buf.extend_from_slice(&(channels as u32 * 2).to_le_bytes());
        buf.extend_from_slice(&16u16.to_le_bytes());
        buf.extend_from_slice(b"data");
        buf.extend_from_slice(&(data_len as u32).to_le_bytes());
        let mut w = std::fs::File::create(path).unwrap();
        w.write_all(&buf).unwrap();
        let twopi = std::f64::consts::TAU / rate as f64;
        let mut block = [0u8; 4];
        for i in 0..samples {
            let v = ((twopi * hz as f64 * i as f64).sin() * 0.5 * i16::MAX as f64) as i16;
            block.copy_from_slice(&v.to_le_bytes());
            w.write_all(&block).unwrap();
            w.write_all(&block).unwrap();
        }
        w.sync_all().unwrap();
    }

    #[test]
    fn probes_wav() {
        let dir = tempfile::tempdir().unwrap();
        let p = dir.path().join("sine.wav");
        write_sine_wav(&p, 1);
        let info = probe(&p).unwrap();
        assert_eq!(info.channels, 2);
        assert_eq!(info.sample_rate, 44100);
        assert!((info.duration.unwrap() - 1.0).abs() < 0.05, "{}", info.duration.unwrap());
        assert!(!info.is_video);
    }

    #[test]
    fn decodes_frames_and_reports_eof() {
        let dir = tempfile::tempdir().unwrap();
        let p = dir.path().join("sine.wav");
        write_sine_wav(&p, 1);
        let mut d = MediaDecoder::open(&p).unwrap();
        let mut total: usize = 0;
        while let Some(block) = d.read().unwrap() {
            assert_eq!(block.len() % d.channels as usize, 0);
            assert!(!block.is_empty());
            total += block.len() / d.channels as usize;
        }
        assert_eq!(total, 44100);
    }

    #[test]
    fn seek_midpoint() {
        let dir = tempfile::tempdir().unwrap();
        let p = dir.path().join("sine.wav");
        write_sine_wav(&p, 4);
        let mut d = MediaDecoder::open(&p).unwrap();
        d.seek(2.0).unwrap();
        let mut got = 0usize;
        while let Some(block) = d.read().unwrap() {
            got += block.len() / d.channels as usize;
            if got > 1000 {
                break;
            }
        }
        assert!(got > 0);
    }

    #[test]
    fn rejects_garbage() {
        let dir = tempfile::tempdir().unwrap();
        let p = dir.path().join("garbage.bin");
        std::fs::write(&p, b"this is not audio at all, just bytes...").unwrap();
        assert!(probe(&p).is_err());
    }
}