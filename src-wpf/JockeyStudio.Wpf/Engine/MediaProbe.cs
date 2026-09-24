using NAudio.Wave;

namespace JockeyStudio.Wpf.Engine;

/// <summary>Probes a media file's properties using Media Foundation (decodes
/// header only; title is inferred from the file stem, matching no-tag-decode
/// choice). Covers wav/aac/mp3/flac/m4a/wma/ogg and video containers whose
/// audio Media Foundation can open.</summary>
public static class MediaProbe
{
    private static readonly HashSet<string> VideoExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".mkv", ".avi", ".webm", ".wmv", ".m4v", ".mpg", ".mpeg"
    };

    public static MediaItem Probe(string path)
    {
        try
        {
            using var reader = new MediaFoundationReader(path);
            return new MediaItem
            {
                Id = Guid.NewGuid().ToString("N"),
                Path = path,
                Title = System.IO.Path.GetFileNameWithoutExtension(path),
                Container = System.IO.Path.GetExtension(path),
                Codec = FormatTagName(reader.WaveFormat.Encoding),
                DurationSecs = reader.TotalTime.TotalSeconds,
                Channels = (ushort)reader.WaveFormat.Channels,
                SampleRate = (uint)reader.WaveFormat.SampleRate,
                Kind = VideoExts.Contains(System.IO.Path.GetExtension(path)) ? MediaKind.Video : MediaKind.Audio,
                PeakDb = null,
                RmsDb = null,
            };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("unsupported media: " + System.IO.Path.GetFileName(path), ex);
        }
    }

    /// <summary>Decode the audio track of <paramref name="path"/> end-to-end and
    /// measure overall peak and RMS loudness in dBFS (0 dB = full scale),
    /// matching src-tauri decoder.rs `analyze`. Expensive for long files, so
    /// callers run it on a background thread; returns (-120, -120) for silence
    /// or empty input.</summary>
    public static (float peakDb, float rmsDb) AnalyzeLoudness(string path)
    {
        using var reader = new MediaFoundationReader(path);
        var sampleSource = reader.ToSampleProvider();
        var buffer = new float[sampleSource.WaveFormat.Channels * 4096];

        float peak = 0.0f;
        double sumSq = 0.0;
        long count = 0;

        while (true)
        {
            int read = sampleSource.Read(buffer, 0, buffer.Length);
            if (read <= 0) break;
            for (int i = 0; i < read; i++)
            {
                float a = Math.Abs(buffer[i]);
                if (a > peak) peak = a;
                sumSq += (double)buffer[i] * buffer[i];
            }
            count += read;
        }

        float peakDb = peak > 0.0f ? 20.0f * (float)Math.Log10(peak) : -120.0f;
        double rms = count > 0 ? Math.Sqrt(sumSq / count) : 0.0;
        float rmsDb = rms > 0.0 ? 20.0f * (float)Math.Log10(rms) : -120.0f;
        return (peakDb, rmsDb);
    }

    private static string FormatTagName(WaveFormatEncoding encoding) => encoding switch
    {
        WaveFormatEncoding.Pcm => "PCM",
        WaveFormatEncoding.IeeeFloat => "FLOAT",
        WaveFormatEncoding.MpegLayer3 => "MP3",
        WaveFormatEncoding.Adpcm => "ADPCM",
        WaveFormatEncoding.Extensible => "EXT",
        _ => encoding.ToString(),
    };
}