using NAudio.Wave;

namespace JockeyStudio.Wpf.Engine;

/// <summary>Float sample stage mirroring engine.rs DeckAudioPipe: exponential
/// fade-in envelope, a volatile volume (set from the UI thread each heartbeat),
/// mute (folds into volume), and an RMS meter used by automix. RMS accumulates
/// on the audio thread into locals and commits under a tiny lock once per read.</summary>
public sealed class DeckAudioPipe : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly Func<bool>? _wrapSignal;
    private readonly int _channels;
    private readonly float _sampleRate;
    private readonly object _meterLock = new();

    private volatile float _volume = 1f;
    private float _fadeInSecs = 0.05f;
    private double _fadeTau;
    private double _fadeTime;
    private float _fadeGain;
    private bool _fadeActive;

    private double _sumSq;
    private long _count;

    public DeckAudioPipe(ISampleProvider source, Func<bool>? wrapSignal, float fadeInSecs)
    {
        _source = source;
        _wrapSignal = wrapSignal;
        _channels = source.WaveFormat.Channels;
        _sampleRate = source.WaveFormat.SampleRate;
        ResetFade(fadeInSecs);
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public void SetVolume(float v) => _volume = Math.Clamp(v, 0f, 1f);

    public float GetVolume() => _volume;

    /// <summary>Restart the exponential fade-in (called at play start and on
    /// each counted-loop playthrough).</summary>
    public void ResetFade(float fadeInSecs)
    {
        _fadeInSecs = fadeInSecs;
        _fadeTau = Math.Max(0.0001, fadeInSecs);
        _fadeTime = 0.0;
        _fadeGain = 0f;
        _fadeActive = true;
    }

    /// <summary>cancel fade (fadeOut cue is handled by the engine) — snaps gain.</summary>
    public void CancelFade() { _fadeActive = false; _fadeGain = 1f; }

    /// <summary>Post-gain RMS in dB, resetting the accumulator (one heartbeat).</summary>
    public double CommitRmsDb()
    {
        double sumSq;
        long count;
        lock (_meterLock)
        {
            sumSq = _sumSq;
            count = _count;
            _sumSq = 0;
            _count = 0;
        }
        if (count == 0) return -96.0;
        double rms = Math.Sqrt(Math.Max(0.0, sumSq / count));
        return rms < 1e-6 ? -96.0 : 20.0 * Math.Log10(rms);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        if (read <= 0) return read;

        if (_wrapSignal?.Invoke() == true) ResetFade(_fadeInSecs);

        float vol = _volume;
        double sumSq = 0;
        bool plain = !_fadeActive && Math.Abs(vol - 1f) < 0.0001f;

        for (int i = 0; i < read; )
        {
            float g = _fadeGain;
            if (_fadeActive)
            {
                _fadeTime += 1.0 / _sampleRate;
                g = (float)(1.0 - Math.Exp(-_fadeTime / _fadeTau));
                _fadeGain = g;
                if (g >= 0.999f)
                {
                    _fadeGain = 1f;
                    _fadeActive = false;
                }
            }

            if (plain)
            {
                for (int c = 0; c < _channels && i < read; c++, i++)
                {
                    float s = buffer[offset + i];
                    sumSq += s * (double)s;
                }
            }
            else
            {
                g *= vol;
                for (int c = 0; c < _channels && i < read; c++, i++)
                {
                    float s = buffer[offset + i] * g;
                    buffer[offset + i] = s;
                    sumSq += s * (double)s;
                }
            }
        }

        lock (_meterLock)
        {
            _sumSq += sumSq;
            _count += read;
        }
        return read;
    }
}