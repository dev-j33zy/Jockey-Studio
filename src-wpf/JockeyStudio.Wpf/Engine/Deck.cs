using NAudio.CoreAudioApi;
using NAudio.Wave;
using static JockeyStudio.Wpf.Engine.EngineConst;

namespace JockeyStudio.Wpf.Engine;

/// <summary>Per-deck state machine (1:1 of engine.rs TilePlayer + TileState):
/// media, transport, loop, volume/mute, automix fades, and per-deck output
/// device. Sound is produced by a <see cref="DeckPlayer"/> built on demand.</summary>
public sealed class Deck
{
    private readonly Func<string, MMDevice?> _resolveDevice;
    private readonly Func<string, MMDevice?>? _resolveDeviceOnThread;
    private readonly Action<Action>? _postToUi;
    private DeckPlayer? _player;
    private int _loadGen;

    /// <summary>Raised after any change to the deck's persisted surface (media,
    /// volume, mute, loop, device, fades, automix). The engine forwards it so
    /// the shell can persist the layout (1:1 of the React store.persist).</summary>
    internal Action<Deck>? StateChanged { get; set; }

    public string Id { get; }

    public string MediaId { get; private set; } = "";
    public string MediaPath { get; private set; } = "";
    public string Title { get; private set; } = "";
    public string MediaType { get; private set; } = "";
    public bool HasMedia { get; private set; }

    public double DurationSecs { get; private set; }
    public double PositionSecs { get; private set; }

    public float Volume { get; private set; } = 0.9f;
    public bool Muted { get; private set; }
    public string LoopMode { get; private set; } = "off";
    public FadesConfig Fades { get; private set; } = new();
    public string DeviceId { get; private set; } = DEFAULT_DEVICE_ID;

    public PlaybackStatus Status { get; private set; } = PlaybackStatus.Stopped;
    public string Error { get; private set; } = "";

    public DeckPlayer? Player => _player;
    public bool IsPlaying => Status == PlaybackStatus.Playing;

    // ---- engine-side ducking / ramps ----
    /// <summary>True while this deck is being ducked by automix: playing,
    /// automix-engaged and not the current newest automix deck.</summary>
    public bool AutoMixDucked { get; internal set; }

    /// <summary>The deck was ducked by automix and then auto-paused (fade-pause);
    /// it resumes when it becomes the newest automix deck again — i.e. after the
    /// newer deck that ducked it is paused or stopped.</summary>
    public bool AutoMixHeldPaused { get; internal set; }

    /// <summary>Linear duck factor (0..1) the engine currently applies to ducked
    /// decks (10^(-autoMixDb/20)). The deck uses it to map a ducked volume-slider
    /// value back to the underlying base volume.</summary>
    public float DuckFactor { get; internal set; } = 1f;

    /// <summary>Monotonic stamp of the deck's latest play start. The automix
    /// winner is the playing automix deck with the highest stamp — "newest media
    /// played wins, everything else ducks".</summary>
    public ulong AutoMixSeq { get; internal set; }

    /// <summary>The level the volume slider should show: the audible gain
    /// (0 when muted), not the user-set base volume, so a duck is visible.
    /// A deck auto-paused by automix is silent, so it reads 0.</summary>
    public float EffectiveVolume => Muted || AutoMixHeldPaused ? 0f : Gain;

    public bool Ramping { get; internal set; }
    public float RampTarget { get; internal set; }
    public float RampRate { get; internal set; }
    public float Gain { get; internal set; } = 1f;

    /// <summary>Raised the moment the deck actually starts playing (play or
    /// resume), so the engine can stamp <see cref="AutoMixSeq"/>.</summary>
    internal Action<Deck>? TransportStarted { get; set; }

    public Deck(Func<string, MMDevice?> resolveDevice, Action<Action>? postToUi = null,
        Func<string, MMDevice?>? resolveDeviceOnThread = null, string? id = null)
    {
        Id = id ?? Guid.NewGuid().ToString("N");
        _resolveDevice = resolveDevice;
        _postToUi = postToUi;
        _resolveDeviceOnThread = resolveDeviceOnThread;
        Gain = Volume;
    }

    // ---------- media ----------

    public void LoadMedia(MediaItem item)
    {
        ClearDuckState();
        _loadGen++;
        MediaId = item.Id;
        MediaPath = item.Path;
        Title = item.Title;
        HasMedia = true;
        DurationSecs = item.DurationSecs;
        PositionSecs = 0;
        MediaType = BuildMediaType(item);
        Status = PlaybackStatus.Stopped;
        Error = "";
        DisposePlayer();
        StateChanged?.Invoke(this);
    }

    private static string BuildMediaType(MediaItem item)
    {
        string kind = item.Kind == MediaKind.Video ? "VIDEO" : "AUDIO";
        string container = (item.Container ?? "").TrimStart('.').ToUpperInvariant();
        string channels = item.Channels == 1 ? "1CH" : $"{item.Channels}CH";
        uint kHz = item.SampleRate / 1000;
        string sample = $"{kHz}KHZ";
        return $"{kind} · {container} · {channels} · {sample}";
    }

    public void ResetMedia()
    {
        ClearDuckState();
        _loadGen++;
        HasMedia = false;
        MediaId = "";
        MediaPath = "";
        Title = "";
        MediaType = "";
        DurationSecs = 0;
        PositionSecs = 0;
        Status = PlaybackStatus.Stopped;
        Error = "";
        DisposePlayer();
        StateChanged?.Invoke(this);
    }

    // ---------- transport ----------

    public void TogglePlay()
    {
        if (!HasMedia) return;
        if (Status == PlaybackStatus.Playing) Pause();
        else Play();
    }

    public void Play()
    {
        if (!HasMedia || Status == PlaybackStatus.Playing) return;

        var resumeTarget = _player;
        if (Status == PlaybackStatus.Paused && resumeTarget?.HasOutput == true)
        {
            AutoMixHeldPaused = false;
            AutoMixDucked = false;
            resumeTarget.Resume();
            Status = PlaybackStatus.Playing;
            TransportStarted?.Invoke(this);
            return;
        }

        double from = Status == PlaybackStatus.Ended ? 0 : PositionSecs;
        StartPlayback(from, stamp: true);
        if (Status == PlaybackStatus.Playing) return;
        // fall through: error already recorded
    }

    private void StartPlayback(double from, bool stamp = false)
    {
        ClearDuckState();
        DisposePlayer();

        // Without a UI post, build the pipeline synchronously (headless harness).
        if (_postToUi == null)
        {
            MMDevice? device;
            try { device = _resolveDevice(DeviceId); }
            catch { device = null; }
            if (device == null)
            {
                Status = PlaybackStatus.Error;
                Error = "output device not found";
                return;
            }
            var player = new DeckPlayer();
            string? err = player.Open(MediaPath, device, from, Gain, LoopMode, Fades.FadeIn);
            if (err != null)
            {
                player.Dispose();
                Status = PlaybackStatus.Error;
                Error = err;
                return;
            }
            _player = player;
            PositionSecs = from;
            DurationSecs = player.DurationSecs;
            Status = PlaybackStatus.Playing;
            if (stamp) TransportStarted?.Invoke(this);
            return;
        }

        // With a UI post, build the pipeline off the UI thread so clicks never
        // block on WASAPI/MediaFoundation init (mirrors the Rust worker thread).
        // The MMDevice is EXPENSIVE COM that is apartment-bound: an endpoint
        // activated on the STA UI thread cannot be used by NAudio from the MTA
        // worker (cross-apartment IMMDevice QueryInterface fails), so resolve
        // it here with a fresh enumerator created on the worker thread itself.
        int gen = ++_loadGen;
        string path = MediaPath;
        string deviceId = DeviceId;
        float gain = Gain;
        string loop = LoopMode;
        float fadeIn = Fades.FadeIn;
        var post = _postToUi;
        var resolveOnThread = _resolveDeviceOnThread;
        Status = PlaybackStatus.Loading;

        Task.Run(() =>
        {
            try
            {
                MMDevice? device;
                try { device = (resolveOnThread ?? _resolveDevice)(deviceId); }
                catch { device = null; }
                if (device == null)
                {
                    post(() => FinishLoad(gen, null, "output device not found", from, stamp));
                    return;
                }
                var player = new DeckPlayer();
                string? err = player.Open(path, device, from, gain, loop, fadeIn);
                post(() => FinishLoad(gen, player, err, from, stamp));
            }
            catch (Exception ex)
            {
                post(() => FinishLoad(gen, null, ex.Message, from, stamp));
            }
        });
    }

    private void FinishLoad(int gen, DeckPlayer? player, string? err, double from, bool stamp)
    {
        if (gen != _loadGen)
        {
            player?.Dispose();
            return;
        }
        if (err != null)
        {
            player?.Dispose();
            if (gen == _loadGen)
            {
                Status = PlaybackStatus.Error;
                Error = err;
            }
            return;
        }
        if (player == null)
        {
            Status = PlaybackStatus.Error;
            Error = "output device not found";
            return;
        }
        _player = player;
        PositionSecs = from;
        DurationSecs = player.DurationSecs;
        // The pipeline was opened with the loop mode captured when the load
        // started; re-arm the counter with whatever mode is current now, so a
        // loop change made while Loading still persists to the new selection.
        player.SetLoopMode(LoopMode);
        _player.Pipe?.SetVolume(Gain);
        Status = PlaybackStatus.Playing;
        if (stamp) TransportStarted?.Invoke(this);
    }

    public void Pause()
    {
        if (Status != PlaybackStatus.Playing) return;
        // The engine's automix reconcile restores any duck on the next tick:
        // a paused deck is no longer playing, so it ramps back to base volume.
        _player?.Pause();
        Status = PlaybackStatus.Paused;
    }

    public void Stop()
    {
        ClearDuckState();
        _loadGen++;
        DisposePlayer();
        PositionSecs = 0;
        Status = PlaybackStatus.Stopped;
    }

    /// <summary>Called by the engine when the final playthrough finished.</summary>
    public void MarkEnded()
    {
        ClearDuckState();
        _loadGen++;
        var p = _player;
        if (p?.HasOutput == true)
        {
            DurationSecs = p.DurationSecs;
            PositionSecs = Math.Max(0, DurationSecs);
        }
        DisposePlayer();
        Status = PlaybackStatus.Ended;
    }

    public void Seek(double secs)
    {
        if (!HasMedia) return;
        double target = Math.Clamp(secs, 0, Math.Max(0, DurationSecs));
        PositionSecs = target;

        if (Status == PlaybackStatus.Playing)
        {
            StartPlayback(target);
        }
        else
        {
            DisposePlayer();
        }
    }

    // ---------- controls ----------

    public void SetVolume(float v)
    {
        if (!HasMedia) return;
        Volume = Math.Clamp(v, 0f, 1f);
        Ramping = false;
        Gain = Muted ? 0f : (AutoMixDucked ? Volume * DuckFactor : Volume);
        _player?.Pipe?.SetVolume(Gain);
        StateChanged?.Invoke(this);
    }

    /// <summary>Set volume from the ducked ("effective") level shown on the
    /// slider. While automix is ducking the deck, the audible level is
    /// base * duckFactor, so the requested slider value is mapped back to the
    /// base volume (staying ducked) rather than accidentally un-ducking.</summary>
    public void SetVolumeEffective(float v)
    {
        if (!HasMedia) return;
        float eff = Math.Clamp(v, 0f, 1f);
        float baseAmt = AutoMixDucked ? eff / Math.Max(0.0001f, DuckFactor) : eff;
        SetVolume(baseAmt);
    }

    public void SetMuted(bool m)
    {
        if (!HasMedia) return;
        Muted = m;
        Ramping = false;
        Gain = m ? 0f : (AutoMixDucked ? Volume * DuckFactor : Volume);
        _player?.Pipe?.SetVolume(Gain);
        StateChanged?.Invoke(this);
    }

    public void SetLoop(string mode)
    {
        LoopMode = mode;
        Ramping = false;
        // Re-arm the counter on the live player. Rebuilding the pipeline while
        // playing would tear down the WASAPI output and flick the deck through
        // Loading (the tile would flash its Play glyph and the seeker could
        // glitch); the in-place change keeps the media going uninterrupted and
        // counts the current playthrough as playthrough 1 of the selection.
        if (_player != null) _player.SetLoopMode(mode);
        StateChanged?.Invoke(this);
    }

    public void SetDevice(string deviceId)
    {
        DeviceId = deviceId;
        Ramping = false;
        if (Status == PlaybackStatus.Playing)
        {
            double keep = PositionSecs;
            StartPlayback(keep);
        }
        StateChanged?.Invoke(this);
    }

    public void ToggleAutoMix()
    {
        Fades.AutoMix = !Fades.AutoMix;
        Ramping = false;
        if (Status == PlaybackStatus.Playing)
        {
            double keep = PositionSecs;
            StartPlayback(keep);
        }
        StateChanged?.Invoke(this);
    }

    public void SetFades(FadesConfig fades)
    {
        Fades = fades.Clone();
        Ramping = false;
        StateChanged?.Invoke(this);
    }

    // ---------- engine helpers ----------

    public void WaveVolume()
    {
        _player?.Pipe?.SetVolume(Muted ? 0f : Gain);
    }

    public void ClearDuckState()
    {
        Ramping = false;
        AutoMixDucked = false;
        AutoMixHeldPaused = false;
        Gain = Muted ? 0f : Volume;
    }

    /// <summary>Auto-pause a deck whose duck has completed: its output is
    /// faded down and the transport is parked until a newer deck stops ducking
    /// it (see <see cref="ResumeFromAutomix"/>).</summary>
    public void PauseForAutomix()
    {
        if (AutoMixHeldPaused) return;
        var p = _player;
        if (p != null) p.Pause();
        AutoMixHeldPaused = true;
        Status = PlaybackStatus.Paused;
        Ramping = false;
        Gain = 0f;
        _player?.Pipe?.SetVolume(0f);
    }

    /// <summary>Un-park a deck auto-paused by automix and fade it back in. The
    /// engine drives the actual gain ramp to the base volume.</summary>
    public void ResumeFromAutomix()
    {
        AutoMixHeldPaused = false;
        if (Status == PlaybackStatus.Paused)
        {
            _player?.Resume();
            Status = PlaybackStatus.Playing;
        }
    }

    public void RefreshProgress()
    {
        var p = _player;
        if (p?.HasOutput == true)
        {
            DurationSecs = p.DurationSecs;
            double pos = p.PositionSecs;
            if (pos > 0) PositionSecs = pos;
        }
    }

    public void DisposePlayer() => _player?.Dispose();
    public void DisposeAll() { ClearDuckState(); DisposePlayer(); }
}