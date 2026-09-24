using System.Collections.Generic;
using NAudio.CoreAudioApi;
using static JockeyStudio.Wpf.Engine.EngineConst;

namespace JockeyStudio.Wpf.Engine;

/// <summary>Enumeration and resolution of Windows render endpoints (1:1 of
/// commands/devices.rs + engine.rs resolve_device). Device ids are the device
/// friendly names; the special id "default" means "follow the app-level
/// default output device".</summary>
/// <remarks>The <see cref="OutputDevice"/> snapshot is built once per
/// <see cref="Refresh"/>: touching <c>MMDevice.FriendlyName</c> is an
/// expensive COM property read (~40ms each), so play/refresh must never call
/// it on the hot path.</remarks>
public sealed class DeviceLibrary
{
    private readonly object _lock = new();
    private readonly Dictionary<string, MMDevice> _byId = new();
    private MMDevice? _default;
    private List<OutputDevice> _cached = new();

    public DeviceLibrary() => Refresh();

    /// <summary>"System Default" + every active render endpoint. Cheap: returns
    /// a copy of the last <see cref="Refresh"/> snapshot (no COM calls).</summary>
    public List<OutputDevice> ListDevices()
    {
        lock (_lock) return new List<OutputDevice>(_cached);
    }

    /// <summary>Resolve a device id (a friendly name, or "default" for the OS
    /// default endpoint) to an MMDevice. Returns null when unknown/unplugged.</summary>
    public MMDevice? Resolve(string id)
    {
        if (string.IsNullOrEmpty(id) || id == DEFAULT_DEVICE_ID) return _default;
        lock (_lock)
        {
            if (_byId.TryGetValue(id, out var dev)) return dev;
            foreach (var (key, value) in _byId)
            {
                if (string.Equals(key, id, System.StringComparison.OrdinalIgnoreCase)) return value;
            }
        }
        return null;
    }

    public string DefaultDeviceName()
    {
        lock (_lock) return _default?.FriendlyName ?? "System Default";
    }

    public void Refresh()
    {
        var enumerator = new MMDeviceEnumerator();
        var devices = new Dictionary<string, MMDevice>();
        MMDevice? def = null;
        try { def = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console); } catch { }
        string defName = def != null ? SafeName(def) : "";
        try
        {
            var all = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            foreach (var dev in all)
            {
                if (dev == null) continue;
                string name = SafeName(dev);
                if (string.IsNullOrEmpty(name)) continue;
                devices.TryAdd(name, dev);
            }
        }
        catch
        {
            // Fall through so playback can still use the default endpoint.
        }
        lock (_lock)
        {
            _default = def;
            _byId.Clear();
            foreach (var (k, v) in devices) _byId[k] = v;

            var list = new List<OutputDevice>();
            if (def != null)
            {
                list.Add(Describe(def, DEFAULT_DEVICE_ID, true, defName));
            }
            foreach (var (id, dev) in _byId)
            {
                string name = SafeName(dev);
                if (string.IsNullOrEmpty(name) || string.Equals(name, defName, System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                list.Add(Describe(dev, id, false, name));
            }
            _cached = list;
        }
    }

    private static string SafeName(MMDevice d)
    {
        try { return d.FriendlyName; } catch { return ""; }
    }

    private static OutputDevice Describe(MMDevice dev, string id, bool isDefault, string name)
    {
        uint sampleRate;
        ushort channels;
        try
        {
            var fmt = dev.AudioClient.MixFormat;
            sampleRate = (uint)fmt.SampleRate;
            channels = (ushort)fmt.Channels;
        }
        catch
        {
            sampleRate = 48000;
            channels = 2;
        }
        return new OutputDevice
        {
            Id = id,
            Name = name is { Length: > 0 } n ? n : id,
            IsDefault = isDefault,
            IsActive = true,
            Channels = channels,
            SampleRate = sampleRate,
        };
    }
}