using NAudio.Wave;

namespace JockeyStudio.Wpf.Engine;

/// <summary>Repeats a reader at end-of-stream (1:1 of engine.rs repeat_infinite +
/// the counted Times(n) restart logic). Runs on the audio thread: when the
/// underlying reader reaches its end it is repositioned to 0 and (for counted
/// modes) `ConsumeWrapReset()` reports that a fresh playthrough started so the
/// fade-in envelope can restart.
///
/// The playthrough counter is re-armed live from the UI thread by
/// <see cref="SetLoopMode"/> (a loop change mid-playback applies without
/// rebuilding the pipeline, so the current playthrough counts as playthrough 1
/// of the newly selected loop). All counter state is therefore guarded so the
/// UI-thread write can never race the audio-thread reads/writes.</summary>
public sealed class LoopingProvider : IWaveProvider
{
    private readonly WaveStream _source;
    private readonly object _gate = new();
    private int _playsRemaining;
    private bool _wrapPending;
    private bool _done;

    /// <summary>Non-0 when the final playthrough finished and the stream is done.</summary>
    public bool IsDone
    {
        get { lock (_gate) return _done; }
    }

    public LoopingProvider(WaveStream source, string loopMode)
    {
        _source = source;
        _playsRemaining = PlaysFor(loopMode);
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public bool IsEndless
    {
        get { lock (_gate) return _playsRemaining == int.MaxValue; }
    }

    private static int PlaysFor(string loopMode) =>
        LoopModes.IsEndless(loopMode)
            ? int.MaxValue
            : Math.Max(1, LoopModes.RepeatCount(loopMode));

    /// <summary>Change the loop mode while audio is streaming. The stream keeps
    /// playing uninterrupted from its current position; the counter is re-armed
    /// so the current playthrough is playthrough 1 of the new selection (a done
    /// stream is brought back to life the next time it hits the end).</summary>
    public void SetLoopMode(string loopMode)
    {
        lock (_gate)
        {
            _playsRemaining = PlaysFor(loopMode);
            _wrapPending = false;
            _done = false;
        }
    }

    /// <summary>True once (then cleared) when the reader wrapped to a new
    /// playthrough of a counted loop.</summary>
    public bool ConsumeWrapReset()
    {
        lock (_gate)
        {
            if (!_wrapPending) return false;
            _wrapPending = false;
            return true;
        }
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        int total = 0;
        while (total < count)
        {
            if (IsDone)
            {
                Array.Clear(buffer, offset + total, count - total);
                break;
            }

            bool beforeEnd = SafeAtEnd();
            int read = _source.Read(buffer, offset + total, count - total);
            total += read;
            bool atEnd = read == 0 || SafeAtEnd();

            if (read == 0 || (beforeEnd && atEnd))
            {
                bool wrap;
                lock (_gate)
                {
                    if (_playsRemaining > 1)
                    {
                        // Endless (int.MaxValue) always counts down but never
                        // reaches 1, so it wraps forever.
                        _playsRemaining--;
                        _wrapPending = true;
                        wrap = true;
                    }
                    else
                    {
                        _playsRemaining = 0;
                        _done = true;
                        wrap = false;
                    }
                }

                if (wrap)
                {
                    SeekStart();
                    continue;
                }
                Array.Clear(buffer, offset + total, count - total);
                break;
            }
            if (total < count) continue;
        }
        return total;
    }

    private bool SafeAtEnd()
    {
        try { return _source.Position >= _source.Length; }
        catch { return false; }
    }

    private void SeekStart()
    {
        try { _source.Position = 0; }
        catch { /* keep playing from wherever we are */ }
    }
}