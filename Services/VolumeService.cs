using System;
using System.Linq;
using NAudio.CoreAudioApi;

namespace AirPodsWin.Services;

/// <summary>
/// Controla el volumen del dispositivo de salida. Si los AirPods son la salida activa
/// (o hay un endpoint de AirPods activo), actúa sobre ellos; si no, sobre la salida predeterminada.
/// </summary>
public sealed class VolumeService : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private MMDevice? _device;

    public event Action? Changed;

    public string DeviceName { get; private set; } = "";
    public bool IsAirPods { get; private set; }

    /// <summary>Volumen 0..1</summary>
    public float Level
    {
        get { try { return _device?.AudioEndpointVolume.MasterVolumeLevelScalar ?? 0; } catch { return 0; } }
        set { try { if (_device != null) _device.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(value, 0f, 1f); } catch { } }
    }

    public bool Muted
    {
        get { try { return _device?.AudioEndpointVolume.Mute ?? false; } catch { return false; } }
        set { try { if (_device != null) _device.AudioEndpointVolume.Mute = value; } catch { } }
    }

    /// <summary>Vuelve a elegir el dispositivo (los AirPods pueden conectarse/desconectarse en cualquier momento).</summary>
    public void Refresh()
    {
        MMDevice? pick = null;
        try { pick = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); } catch { }

        if (pick?.ID == _device?.ID) { return; }

        if (_device != null)
        {
            try { _device.AudioEndpointVolume.OnVolumeNotification -= OnNotify; } catch { }
        }
        _device = pick;
        DeviceName = pick?.FriendlyName ?? "";
        IsAirPods = pick != null && IsAirPodsName(DeviceName);
        if (_device != null)
        {
            try { _device.AudioEndpointVolume.OnVolumeNotification += OnNotify; } catch { }
        }
        Changed?.Invoke();
    }

    public sealed record OutputDevice(string Id, string Name, bool IsDefault)
    {
        public string Check => IsDefault ? "" : "";
    }

    /// <summary>Salidas de audio activas, con la predeterminada marcada.</summary>
    public System.Collections.Generic.List<OutputDevice> ListOutputs()
    {
        var list = new System.Collections.Generic.List<OutputDevice>();
        try
        {
            string? defId = null;
            try { defId = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID; } catch { }
            foreach (var d in _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                list.Add(new OutputDevice(d.ID, d.FriendlyName, d.ID == defId));
        }
        catch { }
        return list;
    }

    public void SetDefault(string id)
    {
        AudioSwitcher.SetDefaultOutput(id);
        Refresh();
    }

    private void OnNotify(AudioVolumeNotificationData data) => Changed?.Invoke();

    private static bool IsAirPodsName(string n) => n.Contains("AirPods", StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        try { if (_device != null) _device.AudioEndpointVolume.OnVolumeNotification -= OnNotify; } catch { }
        _enumerator.Dispose();
    }
}
