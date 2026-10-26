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
    private SwitchableWaveProvider? _outputSource;
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
            _outputSource = new SwitchableWaveProvider(CreateDrivingProvider(_pipe, mixFormat));
            _out = new WasapiOut(device, AudioClientShareMode.Shared, true, 10);
            _out.Init(_outputSource);
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
                double p = _loop?.PositionSecs ?? 0;
                double len = _reader.TotalTime.TotalSeconds;
                return Math.Clamp(p, 0, Math.Max(0, len));
            }
            catch { return 0; }
        }
    }

    public double DurationSecs => _reader?.TotalTime.TotalSeconds ?? 0;

    /// <summary>Flush queued output and reset the resampler after moving the
    /// reader, so no buffered samples from the old position can be rendered.</summary>
    public void SeekRuntime(double secs)
    {
        if (_loop == null || _pipe == null || _outputSource == null)
            throw new InvalidOperationException("playback stream is unavailable");

        bool resumePlayback = IsPlaying;
        _out?.Stop();
        IsPlaying = false;
        _loop.SeekRuntime(secs);

        _mfResampler?.Dispose();
        _mfResampler = null;
        _outputSource.SetSource(CreateDrivingProvider(_pipe, _outputSource.WaveFormat));

        if (resumePlayback)
        {
            _out?.Play();
            IsPlaying = true;
        }
    }

    public void Dispose() => Cleanup();

    private IWaveProvider CreateDrivingProvider(DeckAudioPipe pipe, WaveFormat mixFormat)
    {
        try
        {
            _mfResampler = new MediaFoundationResampler(new SampleToWaveProvider(pipe), mixFormat);
            return _mfResampler;
        }
        catch (Exception)
        {
            _mfResampler?.Dispose();
            _mfResampler = null;
            return new SampleToWaveProvider(pipe);
        }
    }

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
        _outputSource = null;
        _reader?.Dispose();
        _reader = null;
        _loop = null;
        _pipe = null;
    }

    private sealed class SwitchableWaveProvider(IWaveProvider source) : IWaveProvider
    {
        private IWaveProvider _source = source;

        public WaveFormat WaveFormat { get; } = source.WaveFormat;

        public void SetSource(IWaveProvider source) => Volatile.Write(ref _source, source);

        public int Read(byte[] buffer, int offset, int count)
            => Volatile.Read(ref _source).Read(buffer, offset, count);
    }
}