use std::collections::VecDeque;
use std::sync::Arc;
use std::sync::atomic::{AtomicBool, Ordering};
use std::time::Duration;

use cpal::traits::{DeviceTrait, HostTrait, StreamTrait};
use parking_lot::Mutex;
use thiserror::Error;

use crate::models::OutputDevice;

/// Size (in frames) the ring is pre-filled to before the stream starts playing.
pub const PREFILL_FRAMES: usize = 512;

const MAX_BUS_CHANNELS: u16 = 8;

/// Lock-protected interleaved float ring between the mixer (producer) and the
/// WASAPI callback (consumer). Shared-mode output tolerates a brief lock here.
pub struct BusRing {
    inner: Mutex<VecDeque<f32>>,
    capacity: usize,
    pub channels: usize,
    pub fallback: f32,
}

impl BusRing {
    pub fn new(frame_capacity: usize, channels: usize) -> Arc<Self> {
        Arc::new(Self {
            inner: Mutex::new(VecDeque::with_capacity(frame_capacity * channels)),
            capacity: frame_capacity * channels,
            channels,
            fallback: 0.0,
        })
    }

    /// Append interleaved frames (len must be a multiple of `channels`).
    /// Drops oldest frames if the ring would overflow.
    pub fn push_frames(&self, frames: &[f32]) {
        if frames.is_empty() {
            return;
        }
        debug_assert!(
            frames.len() % self.channels == 0,
            "ring push not channel-aligned"
        );
        let mut q = self.inner.lock();
        let mut it = frames.chunks_exact(self.channels);
        if it.remainder().is_empty() {
            for ch in it {
                while q.len() >= self.capacity {
                    q.pop_front();
                }
                if q.len() + self.channels <= self.capacity {
                    q.extend(ch.iter().copied());
                }
            }
        } else {
            // Not aligned: drop the tail so playback never desyncs.
        }
    }

    pub fn available_frames(&self) -> usize {
        self.inner.lock().len() / self.channels
    }

    pub fn available_samples(&self) -> usize {
        self.inner.lock().len()
    }

    pub fn can_push(&self) -> bool {
        self.inner.lock().len() < self.capacity
    }

    /// Mutation-safe: len must be >= new_len.
    pub fn clear(&self) {
        self.inner.lock().clear();
    }

    /// Pop up to `dst.len()` samples; fills shortfall with `fallback`.
    pub fn pop_into_f32(&self, dst: &mut [f32]) {
        let mut q = self.inner.lock();
        let n = dst.len().min(q.len());
        for i in 0..n {
            dst[i] = q.pop_front().unwrap_or(self.fallback);
        }
        for i in n..dst.len() {
            dst[i] = self.fallback;
        }
    }
}

/// Conversion target for the stream callback.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum SampleFmt {
    F32,
    F64,
    I16,
    U16,
    U8,
    I32,
}

impl SampleFmt {
    fn from_cpal(f: cpal::SampleFormat) -> Result<Self, OutputError> {
        Ok(match f {
            cpal::SampleFormat::F32 => SampleFmt::F32,
            cpal::SampleFormat::F64 => SampleFmt::F64,
            cpal::SampleFormat::I16 => SampleFmt::I16,
            cpal::SampleFormat::U16 => SampleFmt::U16,
            cpal::SampleFormat::U8 => SampleFmt::U8,
            cpal::SampleFormat::I32 => SampleFmt::I32,
            other => return Err(OutputError::UnsupportedFormat(other)),
        })
    }
}

#[derive(Debug, Clone)]
pub struct BusSpec {
    pub id: String,
    pub name: String,
    pub channels: usize,
    pub sample_rate: u32,
    pub sample_fmt: SampleFmt,
}

/// One native output stream (bus) opened for a single device.
pub struct OutputBus {
    pub spec: BusSpec,
    pub ring: Arc<BusRing>,
    pub running: Arc<AtomicBool>,
    stream: SendStream,
}

/// cpal marks `Stream` `!Send`/`!Sync` because the ASIO host requires stream
/// use on its creating thread. The Windows default host is WASAPI (COM), which
/// is thread-safe to use and drop from other threads, and our DIDID bus streams
/// are only ever driven from behind the engine mutex, so this is sound here.
struct SendStream(Option<cpal::Stream>);
unsafe impl Send for SendStream {}
unsafe impl Sync for SendStream {}

impl OutputBus {
    /// Open is *not* audible until `start()` is called (pre-fill then play).
    pub fn open(device: &cpal::Device, id: String) -> Result<Self, OutputError> {
        let name = device.name().unwrap_or_else(|_| id.clone());
        let config = device.default_output_config()?;
        let sample_fmt = SampleFmt::from_cpal(config.sample_format())?;
        let channels = (config.channels() as u16).min(MAX_BUS_CHANNELS) as u16;
        let sample_rate = config.sample_rate();
        let rate = sample_rate.0 as usize;

        let ring = BusRing::new(rate * 2, channels as usize);
        let running = Arc::new(AtomicBool::new(true));

        let stream_cfg = cpal::StreamConfig {
            channels,
            sample_rate,
            buffer_size: cpal::BufferSize::Default,
        };

        let ring_p = ring.clone();
        let running_p = running.clone();
        let stream = build_stream(device, &stream_cfg, sample_fmt, ring_p, running_p)?;

        Ok(Self {
            spec: BusSpec {
                id,
                name,
                channels: channels as usize,
                sample_rate: sample_rate.0,
                sample_fmt,
            },
            ring,
            running,
            stream: SendStream(Some(stream)),
        })
    }

    pub fn start(&mut self) -> Result<(), OutputError> {
        if let Some(s) = self.stream.0.take() {
            s.play()?;
            self.stream.0 = Some(s);
        }
        Ok(())
    }

    pub fn stop(&mut self) {
        self.running.store(false, Ordering::SeqCst);
        if let Some(s) = self.stream.0.take() {
            let _ = s.pause();
        }
    }
}

fn build_stream(
    device: &cpal::Device,
    cfg: &cpal::StreamConfig,
    fmt: SampleFmt,
    ring: Arc<BusRing>,
    running: Arc<AtomicBool>,
) -> Result<cpal::Stream, OutputError> {
    match fmt {
        SampleFmt::F32 => build_stream_typed::<f32>(device, cfg, ring, running),
        SampleFmt::F64 => build_stream_typed::<f64>(device, cfg, ring, running),
        SampleFmt::I16 => build_stream_typed::<i16>(device, cfg, ring, running),
        SampleFmt::U16 => build_stream_typed::<u16>(device, cfg, ring, running),
        SampleFmt::U8 => build_stream_typed::<u8>(device, cfg, ring, running),
        SampleFmt::I32 => build_stream_typed::<i32>(device, cfg, ring, running),
    }
}

fn build_stream_typed<T: cpal::SizedSample + cpal::FromSample<f32> + cpal::Sample>(
    device: &cpal::Device,
    cfg: &cpal::StreamConfig,
    ring: Arc<BusRing>,
    running: Arc<AtomicBool>,
) -> Result<cpal::Stream, OutputError> {
    let data_cb = move |data: &mut [T], _info: &cpal::OutputCallbackInfo| {
        if !running.load(Ordering::Relaxed) {
            // Remain silent.
            for s in data.iter_mut() {
                *s = T::from_sample(0.0f32);
            }
            return;
        }
        let mut tmp: Vec<f32> = Vec::with_capacity(data.len());
        tmp.resize(data.len(), 0.0);
        ring.pop_into_f32(&mut tmp);
        for (i, s) in data.iter_mut().enumerate() {
            *s = T::from_sample(tmp[i]);
        }
    };
    let err_cb: fn(cpal::StreamError) = |_e| {};
    Ok(device.build_output_stream(cfg, data_cb, err_cb, Some(Duration::from_millis(120)))?)
}

#[derive(Debug, Error)]
pub enum OutputError {
    #[error("cpal device error: {0}")]
    Device(String),
    #[error("default output config error: {0}")]
    Config(String),
    #[error("unsupported sample format: {0:?}")]
    UnsupportedFormat(cpal::SampleFormat),
    #[error("stream build error: {0}")]
    Stream(String),
    #[error("stream play error: {0}")]
    Play(String),
}

impl From<cpal::BuildStreamError> for OutputError {
    fn from(e: cpal::BuildStreamError) -> Self {
        OutputError::Stream(e.to_string())
    }
}

impl From<cpal::PlayStreamError> for OutputError {
    fn from(e: cpal::PlayStreamError) -> Self {
        OutputError::Play(e.to_string())
    }
}

impl From<cpal::DefaultStreamConfigError> for OutputError {
    fn from(e: cpal::DefaultStreamConfigError) -> Self {
        OutputError::Config(e.to_string())
    }
}

impl From<cpal::StreamError> for OutputError {
    fn from(e: cpal::StreamError) -> Self {
        OutputError::Device(e.to_string())
    }
}

/// Enumerate Windows output endpoints (including Bluetooth devices exposed by
/// Windows as regular endpoints).
pub fn list_output_devices() -> Result<Vec<OutputDevice>, OutputError> {
    let host = cpal::default_host();
    let mut out = Vec::new();
    let default = host.default_output_device();
    if let Some(d) = default.as_ref() {
        out.push(describe(d, crate::models::DEFAULT_DEVICE_ID.to_string(), true)?);
    }
    let devices = host.devices().map_err(|e| OutputError::Device(e.to_string()))?;
    for d in devices {
        if let Ok(name) = d.name() {
            if default.as_ref().map(|dd| dd.name().ok() == Some(name.clone())).unwrap_or(false) {
                continue;
            }
            out.push(describe(&d, name.clone(), false)?);
        }
    }
    Ok(out)
}

fn describe(device: &cpal::Device, id: String, is_default: bool) -> Result<OutputDevice, OutputError> {
    let name = device.name().unwrap_or_else(|_| id.clone());
    let (channels, sample_rate) = match device.default_output_config() {
        Ok(c) => (
            (c.channels() as u16).min(MAX_BUS_CHANNELS),
            c.sample_rate().0,
        ),
        Err(_) => (2, 48000),
    };
    Ok(OutputDevice {
        id,
        name,
        is_default,
        is_active: false,
        channels,
        sample_rate,
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn ring_roundtrip() {
        let ring = BusRing::new(4, 2);
        ring.push_frames(&[1.0, 2.0, 3.0, 4.0, 5.0, 6.0]);
        let mut out = [0f32; 4];
        ring.pop_into_f32(&mut out);
        assert_eq!(out, [1.0, 2.0, 3.0, 4.0]);
    }

    #[test]
    fn ring_alignment_discards_misaligned_tail() {
        let ring = BusRing::new(100, 2);
        ring.push_frames(&[1.0, 2.0, 3.0]);
        assert_eq!(ring.available_samples(), 2);
    }

    #[test]
    fn ring_never_exceeds_capacity() {
        let ring = BusRing::new(4, 2);
        let big: Vec<f32> = (0..(40 * 2)).map(|i| i as f32).collect();
        ring.push_frames(&big);
        assert!(ring.available_samples() <= 4 * 2);
        let mut out = [999f32; 2];
        ring.pop_into_f32(&mut out);
        // Oldest frames were dropped, so remaining are the tail of `big`.
        assert!(out[0] >= 30.0);
    }

    #[test]
    fn pop_shortfill_uses_fallback() {
        let ring = BusRing::new(8, 2);
        ring.push_frames(&[1.0, 2.0]);
        let mut out = [0f32; 6];
        ring.pop_into_f32(&mut out);
        assert_eq!(out, [1.0, 2.0, 0.0, 0.0, 0.0, 0.0]);
    }
}