using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using AirPodsWin.Services;

namespace AirPodsWin;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    private readonly VolumeService _volume = new();
    private readonly AirPodsScanner? _scanner;
    private readonly DispatcherTimer _timer;
    private AirPodsInfo? _info;
    private string? _error;
    private bool _updatingVolume;
    private readonly bool _demo;

    public MainViewModel(bool demo, bool demoMax = false)
    {
        _demo = demo;
        _volume.Changed += () => _ui.BeginInvoke(SyncVolume);
        _volume.Refresh();
        SyncVolume();

        if (demo)
        {
            _info = demoMax
                ? new AirPodsInfo("AirPods Max", -1, -1, -1, false, false, false, -40, DateTime.UtcNow.AddYears(1),
                                  IsHeadset: true, Headset: 65, HeadsetCharging: false)
                : new AirPodsInfo("AirPods Pro (2.ª gen)", 85, 75, 100, false, true, false, -40, DateTime.UtcNow.AddYears(1));
        }
        else
        {
            _scanner = new AirPodsScanner();
            _scanner.Updated += i => _ui.BeginInvoke(() => { _info = i; _error = null; CheckLowBattery(i); Notify(null); });
            _scanner.Failed += m => _ui.BeginInvoke(() => { _error = m; Notify(null); });
            _scanner.Start();
        }

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) =>
        {
            _volume.Refresh();
            if (_info != null && DateTime.UtcNow - _info.Seen > TimeSpan.FromMinutes(5)) _info = null;
            Notify(null);
        };
        _timer.Start();
    }

    // ---------- Batería ----------
    private bool Fresh => _info != null && DateTime.UtcNow - _info.Seen < TimeSpan.FromSeconds(20);

    /// <summary>Solo se muestra y se avisa de la batería cuando la salida de audio activa son unos AirPods.</summary>
    private bool AirPodsActive => _demo || _volume.IsAirPods;
    private AirPodsInfo? Info => AirPodsActive ? _info : null;

    public int Left => Info?.Left ?? -1;
    public int Right => Info?.Right ?? -1;
    public int Case => Info?.Case ?? -1;
    public bool LeftCharging => Info?.LeftCharging ?? false;
    public bool RightCharging => Info?.RightCharging ?? false;
    public bool CaseCharging => Info?.CaseCharging ?? false;

    // AirPods Max: una sola batería y sin estuche. Si aún no ha llegado ningún anuncio Bluetooth,
    // se reconoce por el nombre de la salida de audio activa.
    public bool IsHeadset => Info?.IsHeadset
        ?? (AirPodsActive && _volume.DeviceName.Contains("AirPods Max", StringComparison.OrdinalIgnoreCase));
    public int Headset => Info?.Headset ?? -1;
    public bool HeadsetCharging => Info?.HeadsetCharging ?? false;
    public double RingOpacity => AirPodsActive && Fresh ? 1.0 : 0.45;

    // ---------- Aviso de batería baja ----------
    private const int LowThreshold = 20;     // avisa por debajo de este porcentaje
    private const int RearmThreshold = 30;   // vuelve a armarse al subir de este porcentaje (o al cargar)
    private readonly System.Collections.Generic.HashSet<string> _alerted = new();

    /// <summary>Se dispara una vez por componente cuando baja del umbral: (nombre, porcentaje).</summary>
    public event Action<string, int>? LowBattery;

    private void CheckLowBattery(AirPodsInfo i)
    {
        if (!AirPodsActive) return;   // otra salida de audio: sin avisos

        if (i.IsHeadset)
        {
            Check("AirPods Max", i.Headset, i.HeadsetCharging);
            return;
        }

        Check("Auricular izquierdo", i.Left, i.LeftCharging);
        Check("Auricular derecho", i.Right, i.RightCharging);
        Check("Estuche", i.Case, i.CaseCharging);

        void Check(string name, int pct, bool charging)
        {
            if (pct < 0) return;                                   // desconocido
            if (charging || pct >= RearmThreshold) { _alerted.Remove(name); return; }
            if (pct < LowThreshold && _alerted.Add(name)) LowBattery?.Invoke(name, pct);
        }
    }

    // ---------- Textos ----------
    public string Title
    {
        get
        {
            if (_volume.IsAirPods) return ExtractName(_volume.DeviceName);
            return Info?.Model ?? "AirPods";
        }
    }

    public string Subtitle
    {
        get
        {
            if (!AirPodsActive) return "La salida de audio actual no son unos AirPods";
            if (_error != null) return _error;
            if (_info == null) return "Buscando AirPods… abre la tapa del estuche cerca del PC";
            var state = !Fresh ? "Sin señal reciente" : "Conectados";
            return $"{_info.Model} · {state}";
        }
    }

    public string OutputText => _volume.DeviceName.Length == 0
        ? "Sin dispositivo de salida"
        : $"Salida: {_volume.DeviceName}";

    public string TrayText
    {
        get
        {
            if (!AirPodsActive) return "AirPods — salida de audio no activa";
            if (_info == null) return "AirPods — sin datos";
            static string F(int v) => v < 0 ? "—" : $"{v}%";
            if (IsHeadset) return $"AirPods Max  {F(Headset)}";
            return $"AirPods  L {F(Left)}  R {F(Right)}  Estuche {F(Case)}";
        }
    }

    // ---------- Salida de audio ----------
    public System.Collections.ObjectModel.ObservableCollection<VolumeService.OutputDevice> Outputs { get; } = new();

    private bool _showOutputs;
    public bool ShowOutputs
    {
        get => _showOutputs;
        set { _showOutputs = value; if (value) RefreshOutputs(); Notify(nameof(ShowOutputs)); }
    }

    public void RefreshOutputs()
    {
        Outputs.Clear();
        foreach (var o in _volume.ListOutputs()) Outputs.Add(o);
    }

    public void SelectOutput(string id)
    {
        try { _volume.SetDefault(id); } catch { }
        ShowOutputs = false;
        SyncVolume();
        Notify(null);
    }

    // ---------- Volumen ----------
    private double _volumePct;
    public double Volume
    {
        get => _volumePct;
        set
        {
            if (Math.Abs(_volumePct - value) < 0.01) return;
            _volumePct = value;
            Notify(nameof(Volume));
            Notify(nameof(VolumeIcon));
            if (!_updatingVolume)
            {
                if (_volume.Muted && value > 0) _volume.Muted = false;
                _volume.Level = (float)(value / 100.0);
            }
        }
    }

    // ---------- Silenciar ----------
    private bool _muted;
    /// <summary>Altavoz tachado cuando está silenciado.</summary>
    public string MuteIcon => _muted ? "" : "";
    public double SliderOpacity => _muted ? 0.45 : 1.0;
    public string MuteTip => _muted ? "Activar sonido" : "Silenciar";

    public void ToggleMute()
    {
        _volume.Muted = !_volume.Muted;
        SyncVolume();
    }

    public string VolumeIcon => _volumePct <= 0 ? "" : _volumePct < 34 ? "" : _volumePct < 67 ? "" : "";

    private void SyncVolume()
    {
        _updatingVolume = true;
        try { Volume = Math.Round(_volume.Level * 100); }
        finally { _updatingVolume = false; }
        _muted = _volume.Muted;
        Notify(nameof(MuteIcon));
        Notify(nameof(SliderOpacity));
        Notify(nameof(MuteTip));
        Notify(nameof(OutputText));
        Notify(nameof(Title));
        Notify(nameof(Subtitle));
    }

    // "Auriculares (AirPods de Gerard)" -> "AirPods de Gerard"
    private static string ExtractName(string friendly)
    {
        int a = friendly.LastIndexOf('('), b = friendly.LastIndexOf(')');
        return a >= 0 && b > a ? friendly[(a + 1)..b] : friendly;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    /// <summary>null = refrescar todas las propiedades.</summary>
    private void Notify(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        _timer.Stop();
        _scanner?.Dispose();
        _volume.Dispose();
    }
}

