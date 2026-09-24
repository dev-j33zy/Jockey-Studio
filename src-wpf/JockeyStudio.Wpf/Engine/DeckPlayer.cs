using NAudio.CoreAudioApi;
using NAudio.MediaFoundation;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace JockeyStudio.Wpf.Engine;

/// <summary>Per-deck output player (1:1 of engine.rs output layer + Sink):
/// reader → LoopingProvider → float DeckAudioPipe → resampler → WasapiOut on
/// the deck's chosen device.</summary>
public sealed class DeckPlayer : IDisposable
{
    private MediaFoundationReader? _reader;
    private LoopingProvider? _loop;
    private DeckAudioPipe? _pipe;
    private MediaFoundationResampler? _mfResampler;
    private WasapiOut? _out;

    public DeckAudioPipe? Pipe => _pipe;

    public MediaFoundationReader? Reader => _reader;

    public bool IsPlaying { get; private set; }

    /// <summary>True when the final playthrough completed (loop/plain EOF).</summary>
    public bool IsDone => _loop?.IsDone == true;

    public bool HasOutput => _out != null;

    /// <summary>Open a file and start it playing immediately.</summary>
    /// <returns>null on success, otherwise a user-facing error message.</returns>
    public string? Open(
        string path,
        MMDevice device,
        double startSecs,
        float volume,
        string loopMode,
        float fadeInSecs)
    {
        try
        {
            var reader = new MediaFoundationReader(path);
            if (startSecs > 0.05)
            {
                double total = reader.TotalTime.TotalSeconds;
                reader.CurrentTime = TimeSpan.FromSeconds(Math.Min(startSecs, Math.Max(0, total)));
            }
            _reader = reader;

            _loop = new LoopingProvider(reader, loopMode);
            var sampleSource = _loop.ToSampleProvider();
            _pipe = new DeckAudioPipe(sampleSource, _loop.ConsumeWrapReset, fadeInSecs);
            _pipe.SetVolume(volume);

            WaveFormat mixFormat = device.AudioClient.MixFormat;

            IWaveProvider driving;
            try
            {
                _mfResampler = new MediaFoundationResampler(new SampleToWaveProvider(_pipe), mixFormat);
                driving = _mfResampler;
            }
            catch (Exception)
            {
                _mfResampler?.Dispose();
                _mfResampler = null;
                driving = new SampleToWaveProvider(_pipe);
            }

            _out = new WasapiOut(device, AudioClientShareMode.Shared, false, 100);
            _out.Init(driving);
            _out.Play();
            IsPlaying = true;
            return null;
        }
        catch (Exception ex)
        {
            Cleanup();
            return ex.Message;
        }
    }

    public void Pause()
    {
        if (_out == null || !IsPlaying) return;
        try { _out.Pause(); } catch { }
        IsPlaying = false;
    }

    /// <summary>Re-arm the loop counter live, without rebuilding the pipeline,
    /// so the current playthrough counts as playthrough 1 of the new mode.</summary>
    public void SetLoopMode(string mode) => _loop?.SetLoopMode(mode);

    public void Resume()
    {
        if (_out == null || IsPlaying) return;
        try { _out.Play(); } catch { }
        IsPlaying = true;
    }

    public void StopSave()
    {
        if (_out != null)
        {
            try { _out.Stop(); } catch { }
        }
        IsPlaying = false;
    }

    /// <summary>Current position in seconds, clamped to the file duration.</summary>
    public double PositionSecs
    {
        get
        {
            if (_reader == null) return 0;
            try
            {
                double p = _reader.CurrentTime.TotalSeconds;
                double len = _reader.TotalTime.TotalSeconds;
                return Math.Clamp(p, 0, Math.Max(0, len));
            }
            catch { return 0; }
        }
    }

    public double DurationSecs => _reader?.TotalTime.TotalSeconds ?? 0;

    /// <summary>Seek the underlying reader while keeping the pipeline warm.
    /// Only usable when the deck treats playback as paused-for-seek; the engine
    /// usually rebuilds the pipeline via <see cref="Open"/> instead.</summary>
    public void SeekRuntime(double secs)
    {
        if (_reader == null) return;
        try { _reader.CurrentTime = TimeSpan.FromSeconds(Math.Max(0, secs)); }
        catch { }
    }

    public void Dispose() => Cleanup();

    private void Cleanup()
    {
        IsPlaying = false;
        try
        {
            if (_out != null) { _out.Stop(); _out.Dispose(); _out = null; }
        }
        catch { _out = null; }
        _mfResampler?.Dispose();
        _mfResampler = null;
        _reader?.Dispose();
        _reader = null;
        _loop = null;
        _pipe = null;
    }
}