using System.IO;
using NAudio.CoreAudioApi;
using static JockeyStudio.Wpf.Engine.EngineConst;

namespace JockeyStudio.Wpf.Engine;

/// <summary>The deck player engine (1:1 of Rust engine.rs + commands/media.rs +
/// commands/devices.rs). Owns decks, the media library, device resolution and
/// the 200ms heartbeat that runs transport/automix progress. UI thread only —
/// call <see cref="Tick"/> from a 200ms DispatcherTimer.</summary>
public sealed class PlayerEngine : IDisposable
{
    public const int HEARTBEAT_MS = 200;

    private readonly DeviceLibrary _devices = new();
    private readonly List<Deck> _decks = new();
    private readonly List<MediaItem> _media = new();
    private readonly EngineSettings _settings = new();

    /// <summary>Monotonic source of <see cref="Deck.AutoMixSeq"/> stamps: each
    /// play start makes that deck the newest automix contender ("newest media
    /// played wins, others duck").</summary>
    private ulong _autoMixSeqNext = 1;

    public IReadOnlyList<Deck> Decks => _decks;
    public IReadOnlyList<MediaItem> Media => _media;
    public EngineSettings Settings => _settings;
    public DeviceLibrary Devices => _devices;

    /// <summary>When set, deck loads run on a worker thread and completion is
    /// marshalled back through this delegate (e.g. Dispatcher.BeginInvoke).
    /// When null, loads run synchronously on the calling thread.</summary>
    public Action<Action>? PostToUi { get; set; }

    /// <summary>Latest version the update notification was dismissed for (persisted
    /// in the app state so a silent boot check does not re-nudge).</summary>
    public string DismissedUpdateVersion { get; set; } = "";

    /// <summary>Raised whenever anything that should survive a restart changes
    /// (deck added/removed/reordered, media imported/loaded, settings or any
    /// per-deck control). The shell debounces this into a disk save.</summary>
    public event Action? Changed;

    private bool _restoring;

    // ---------- decks ----------

    public Deck AddDeck()
    {
        var deck = CreateDeck();
        _decks.Add(deck);
        Changed?.Invoke();
        return deck;
    }

    private Deck CreateDeck(string? id = null)
    {
        var deck = new Deck(ResolveDeviceId, PostToUi, ResolveDeviceOnCallerThread, id);
        deck.StateChanged = _ =>
        {
            if (!_restoring) Changed?.Invoke();
        };
        deck.TransportStarted = d => d.AutoMixSeq = _autoMixSeqNext++;
        return deck;
    }

    public void AddDecks(int count)
    {
        for (int i = 0; i < count; i++) AddDeck();
    }

    public void RemoveDeck(string id)
    {
        var deck = _decks.FirstOrDefault(d => d.Id == id);
        if (deck == null) return;
        _decks.Remove(deck);
        RestoreAllDucks();
        deck.DisposeAll();
        Changed?.Invoke();
    }

    public void ReorderDecks(IReadOnlyList<string> orderedIds)
    {
        var map = _decks.ToDictionary(d => d.Id);
        _decks.Clear();
        foreach (var id in orderedIds)
        {
            if (map.TryGetValue(id, out var d)) _decks.Add(d);
        }
        Changed?.Invoke();
    }

    /// <summary>reset_all: keep decks, drop their media.</summary>
    public void ResetAll()
    {
        bool any = false;
        foreach (var deck in _decks.ToList())
        {
            any |= deck.HasMedia;
            deck.ResetMedia();
        }
        RestoreAllDucks();
        _ = any;
        Changed?.Invoke();
    }

    // ---------- media ----------

    public MediaItem ImportMedia(string path)
    {
        var item = MediaProbe.Probe(path);
        _media.Add(item);
        AnalyzePeakInBackground(item);
        Changed?.Invoke();
        return item;
    }

    /// <summary>Scan the file for overall loudness on a background thread so
    /// import returns immediately (1:1 of src-tauri commands/media.rs):
    /// results are patched into the media item later and never shown in the
    /// deck UI (reserved for a future normalize-volume setting).</summary>
    private void AnalyzePeakInBackground(MediaItem item)
    {
        string path = item.Path;
        var post = PostToUi;
        Task.Run(() =>
        {
            (float peakDb, float rmsDb) stats;
            try { stats = MediaProbe.AnalyzeLoudness(path); }
            catch { return; }
            Action apply = () =>
            {
                item.PeakDb = stats.peakDb;
                item.RmsDb = stats.rmsDb;
            };
            (post ?? ((Action a) => a()))(apply);
        });
    }

    public Deck? FindDeck(string deckId) => _decks.FirstOrDefault(d => d.Id == deckId);

    /// <summary>Unload a deck's media (Edit → Cut): the deck goes empty but
    /// the media item stays in the library so it can be pasted elsewhere.</summary>
    public void UnloadMedia(string deckId)
    {
        FindDeck(deckId)?.ResetMedia();
    }

    public void LoadMediaIntoTile(string deckId, string mediaId)
    {
        var deck = FindDeck(deckId);
        var item = _media.FirstOrDefault(m => m.Id == mediaId);
        if (deck == null || item == null) return;
        deck.LoadMedia(item);
    }

    /// <summary>loadInto: next empty deck, or auto-add a new one.</summary>
    public Deck LoadIntoNextEmpty(string mediaId)
    {
        var item = _media.FirstOrDefault(m => m.Id == mediaId);
        if (item == null) throw new InvalidOperationException("media not found");
        var deck = _decks.FirstOrDefault(d => !d.HasMedia) ?? AddDeck();
        deck.LoadMedia(item);
        return deck;
    }

    // ---------- settings ----------

    public void SetSettings(EngineSettings s)
    {
        _settings.AutoMixEnabled = s.AutoMixEnabled;
        _settings.AutoMixDb = s.AutoMixDb;
        _settings.AutoMixAttackMs = s.AutoMixAttackMs;
        _settings.AutoMixReleaseMs = s.AutoMixReleaseMs;
        _settings.DefaultFadeIn = s.DefaultFadeIn;
        _settings.DefaultFadeOut = s.DefaultFadeOut;
        _settings.DefaultDeviceId = string.IsNullOrEmpty(s.DefaultDeviceId) ? DEFAULT_DEVICE_ID : s.DefaultDeviceId;
        if (!_restoring) Changed?.Invoke();
    }

    // ---------- persistence ----------

    /// <summary>Snapshot everything that should survive a restart (1:1 of the
    /// React store buildPersistState / Rust AppState).</summary>
    public AppState CaptureState()
    {
        return new AppState
        {
            TileOrder = _decks.Select(d => d.Id).ToList(),
            Tiles = _decks.Select(d => new PersistedTile
            {
                Id = d.Id,
                MediaId = d.HasMedia ? d.MediaId : null,
                DeviceId = d.DeviceId,
                Volume = d.Volume,
                Muted = d.Muted,
                LoopMode = d.LoopMode,
                Fades = d.Fades.Clone(),
            }).ToList(),
            Media = _media.Select(CloneMedia).ToList(),
            Settings = CloneSettings(_settings),
            UpdateDismissedVersion = DismissedUpdateVersion,
        };
    }

    /// <summary>Rebuild the session from disk on launch (1:1 of engine.rs
    /// restore): settings first, then the media library (re-probing only when a
    /// persisted item lost its metadata), then decks in their saved order with
    /// each deck's media, volume, mute, loop, device and fades. Decks whose
    /// media id no longer resolves stay empty. No Changed events are raised —
    /// the loaded layout is exactly what is already on disk.</summary>
    public void Restore(AppState state)
    {
        _restoring = true;
        try
        {
            // Tear down the old session's playback first so repeated restores
            // (Open Playlist, Undo/Redo) never leave orphaned live players.
            foreach (var old in _decks) old.DisposeAll();
            _media.Clear();
            _decks.Clear();

            DismissedUpdateVersion = state.UpdateDismissedVersion ?? "";
            SetSettings(state.Settings);

            foreach (var pm in state.Media)
            {
                if (_media.Any(m => m.Id == pm.Id)) continue;
                var item = CloneMedia(pm);
                if (string.IsNullOrWhiteSpace(item.Title))
                {
                    string stem = Path.GetFileNameWithoutExtension(item.Path);
                    item.Title = string.IsNullOrEmpty(stem) ? "Untitled" : stem;
                }
                if (item.DurationSecs <= 0 || string.IsNullOrEmpty(item.Container))
                {
                    try
                    {
                        var probe = MediaProbe.Probe(item.Path);
                        item.Container = probe.Container;
                        item.Codec = probe.Codec;
                        item.DurationSecs = probe.DurationSecs;
                        item.Channels = probe.Channels;
                        item.SampleRate = probe.SampleRate;
                        item.Kind = probe.Kind;
                    }
                    catch
                    {
                    }
                }
                _media.Add(item);
            }

            var byId = new Dictionary<string, Deck>();
            foreach (var pt in state.Tiles)
            {
                if (byId.ContainsKey(pt.Id)) continue;
                var deck = CreateDeck(pt.Id);
                if (!string.IsNullOrEmpty(pt.MediaId))
                {
                    var item = _media.FirstOrDefault(m => m.Id == pt.MediaId);
                    if (item != null) deck.LoadMedia(item);
                }
                deck.SetVolume(pt.Volume);
                deck.SetMuted(pt.Muted);
                deck.SetDevice(pt.DeviceId);
                deck.SetLoop(pt.LoopMode);
                deck.SetFades(pt.Fades);
                byId[pt.Id] = deck;
            }

            foreach (var id in state.TileOrder)
            {
                if (byId.TryGetValue(id, out var deck))
                {
                    _decks.Add(deck);
                    byId.Remove(id);
                }
            }
            _decks.AddRange(byId.Values);

            RestoreAllDucks();
        }
        finally
        {
            _restoring = false;
        }
    }

    private static MediaItem CloneMedia(MediaItem m) => new()
    {
        Id = m.Id,
        Path = m.Path,
        Title = m.Title,
        Container = m.Container,
        Codec = m.Codec,
        DurationSecs = m.DurationSecs,
        Channels = m.Channels,
        SampleRate = m.SampleRate,
        Kind = m.Kind,
        PeakDb = m.PeakDb,
        RmsDb = m.RmsDb,
    };

    private static EngineSettings CloneSettings(EngineSettings s) => new()
    {
        AutoMixEnabled = s.AutoMixEnabled,
        AutoMixDb = s.AutoMixDb,
        AutoMixAttackMs = s.AutoMixAttackMs,
        AutoMixReleaseMs = s.AutoMixReleaseMs,
        DefaultFadeIn = s.DefaultFadeIn,
        DefaultFadeOut = s.DefaultFadeOut,
        DefaultDeviceId = s.DefaultDeviceId,
    };

    // ---------- device resolution ----------

    private MMDevice? ResolveDeviceId(string deviceId)
    {
        var lib = _devices;
        if (deviceId != DEFAULT_DEVICE_ID)
        {
            return lib.Resolve(deviceId);
        }
        if (_settings.DefaultDeviceId != DEFAULT_DEVICE_ID)
        {
            var pinned = lib.Resolve(_settings.DefaultDeviceId);
            if (pinned != null) return pinned;
        }
        return lib.Resolve(DEFAULT_DEVICE_ID);
    }

    /// <summary>Resolve an output endpoint using a fresh enumerator created on
    /// the CALLING thread. Used by the async deck-load path so the MMDevice is
    /// activated in the worker thread's apartment: an MMDevice created on the
    /// STA UI thread cannot be consumed by NAudio from the MTA worker thread
    /// (cross-apartment QueryInterface for IMMDevice fails with E_NOINTERFACE).
    /// Thread-safe: touches no shared state.</summary>
    public MMDevice? ResolveDeviceOnCallerThread(string deviceId)
    {
        string target = deviceId;
        if (target == DEFAULT_DEVICE_ID && _settings.DefaultDeviceId != DEFAULT_DEVICE_ID)
        {
            target = _settings.DefaultDeviceId;
        }
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            if (target == DEFAULT_DEVICE_ID)
            {
                return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
            }
            var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            foreach (var dev in endpoints)
            {
                if (dev == null) continue;
                string name = SafeFriendlyName(dev);
                if (string.Equals(name, target, StringComparison.OrdinalIgnoreCase)) return dev;
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string SafeFriendlyName(MMDevice dev)
    {
        try { return dev.FriendlyName; } catch { return ""; }
    }

    public List<OutputDevice> ListDevices() => _devices.ListDevices();

    /// <summary>The display label for the app-level default output device (1:1
    /// of the React settings-panel/tile label): "System Default" when decks
    /// follow the OS default, otherwise the configured device's name, or its raw
    /// id when that device is no longer present. Cheap — reads the cached
    /// <see cref="DeviceLibrary"/> snapshot, no COM calls.</summary>
    public string DefaultDeviceDisplayName()
    {
        string id = _settings.DefaultDeviceId;
        if (string.IsNullOrEmpty(id) || id == DEFAULT_DEVICE_ID) return "System Default";
        foreach (var dev in _devices.ListDevices())
        {
            if (string.Equals(dev.Id, id, StringComparison.OrdinalIgnoreCase)) return dev.Name;
        }
        return id;
    }

    // ---------- heartbeat ----------

    /// <summary>The 200ms engine heartbeat: ramp steps + progress + end
    /// detection, then the automix reconcile (1:1 of engine.rs tick loop).</summary>
    public void Tick()
    {
        LoopRamps();

        var ended = new List<Deck>();
        foreach (var deck in _decks)
        {
            var player = deck.Player;
            if (player == null || !player.HasOutput) continue;
            if (player.IsDone)
            {
                ended.Add(deck);
                continue;
            }
            deck.RefreshProgress();
        }
        foreach (var deck in ended) deck.MarkEnded();

        ReconcileAutomix();
    }

    /// <summary>loop_ramps: step every active ramp toward its target and write
    /// the resulting gain into each deck's pipe.</summary>
    private void LoopRamps()
    {
        foreach (var deck in _decks)
        {
            if (!deck.Ramping) continue;
            float g = deck.Gain + (deck.RampTarget - deck.Gain) * deck.RampRate;
            if (Math.Abs(g - deck.RampTarget) < 0.0005f)
            {
                g = deck.RampTarget;
                deck.Ramping = false;
            }
            deck.Gain = g;
        }
    }

    /// <summary>"Newest wins" automix (1:1 of engine.rs reconcile_automix):
    /// every deck with automix engaged can duck or be ducked by every other
    /// automix-engaged deck. The newest playing automix deck — highest
    /// <see cref="Deck.AutoMixSeq"/> play stamp — keeps its full volume and
    /// becomes the "active" deck. Every other playing automix deck is ramped
    /// down to base * duckFactor (duckFactor = 10^(-autoMixDb/20)) and, once
    /// the duck completes, auto-pauses (fade-pause). When the active deck is
    /// paused or stopped (or a newer deck takes over), the newest ducked deck
    /// resumes and fades back up. Decks that are not playing, muted or not
    /// automix-engaged simply hold their own base level; when global auto-mix
    /// is off nothing is ducked or paused and any previously held deck resumes.</summary>
    private void ReconcileAutomix()
    {
        float db = _settings.AutoMixEnabled ? Math.Clamp(_settings.AutoMixDb, 0f, 60f) : 0f;
        float duckFactor = (float)Math.Pow(10.0, -db / 20.0);
        double attackRate = RampRate((double)_settings.AutoMixAttackMs);
        double releaseRate = RampRate((double)_settings.AutoMixReleaseMs);

        Deck? winner = null;
        if (_settings.AutoMixEnabled)
        {
            foreach (var deck in _decks)
            {
                if (!deck.Fades.AutoMix || deck.Muted) continue;
                if (!(deck.IsPlaying || deck.AutoMixHeldPaused)) continue;
                if (winner == null || deck.AutoMixSeq > winner.AutoMixSeq) winner = deck;
            }
        }

        foreach (var deck in _decks)
        {
            deck.DuckFactor = duckFactor;
            float baseGain = deck.Muted ? 0f : deck.Volume;
            bool automixCandidate = _settings.AutoMixEnabled && deck.Fades.AutoMix
                && !deck.Muted && (deck.IsPlaying || deck.AutoMixHeldPaused);

            if (winner == null || ReferenceEquals(deck, winner))
            {
                deck.AutoMixDucked = false;
                if (deck.AutoMixHeldPaused)
                {
                    // The deck that ducked this one is gone: fade it back in.
                    deck.ResumeFromAutomix();
                    RampTo(deck, baseGain, releaseRate);
                }
                else
                {
                    RampTo(deck, baseGain, releaseRate);
                }
                continue;
            }

            if (!automixCandidate)
            {
                // A deck held paused by automix that a still-newer deck keeps
                // blocking stays parked in silence; everything else just holds
                // its own level.
                deck.AutoMixDucked = false;
                if (deck.AutoMixHeldPaused)
                {
                    deck.Gain = 0f;
                    deck.Ramping = false;
                }
                else
                {
                    RampTo(deck, baseGain, releaseRate);
                }
                continue;
            }

            // A playing automix deck beaten by a newer deck: duck it, then park it.
            deck.AutoMixDucked = true;
            if (deck.AutoMixHeldPaused)
            {
                deck.Gain = 0f;
                deck.Ramping = false;
                continue;
            }
            float duckTarget = baseGain * duckFactor;
            if (!deck.Ramping && Math.Abs(deck.Gain - duckTarget) < 0.0005f)
            {
                deck.PauseForAutomix();
            }
            else
            {
                RampTo(deck, duckTarget, attackRate);
            }
        }

        WriteVolumes();
    }

    /// <summary>Push every deck's current gain into its audio pipe (the ramp
    /// steps from <see cref="LoopRamps"/> go live here).</summary>
    private void WriteVolumes()
    {
        foreach (var deck in _decks) deck.WaveVolume();
    }

    /// <summary>Point a deck's gain at a target: engage the exponential ramp
    /// when the gain actually has to move (attack for ducking down, release for
    /// restoring up), otherwise leave it idle at the target.</summary>
    private static void RampTo(Deck deck, float target, double rate)
    {
        deck.RampTarget = target;
        if (Math.Abs(deck.Gain - target) < 0.0005f)
        {
            deck.Ramping = false;
            deck.Gain = target;
            return;
        }
        deck.Ramping = true;
        deck.RampRate = (float)rate;
    }

    private void RestoreAllDucks()
    {
        foreach (var deck in _decks)
        {
            deck.Ramping = false;
            deck.AutoMixDucked = false;
            deck.DuckFactor = 1f;
            deck.Gain = deck.Muted ? 0f : deck.Volume;
            deck.WaveVolume();
        }
    }

    private static double RampRate(double ms) =>
        1.0 - Math.Exp(-HEARTBEAT_MS / Math.Max(1.0, ms));

    public void Dispose() => RestoreAllDucks();
}