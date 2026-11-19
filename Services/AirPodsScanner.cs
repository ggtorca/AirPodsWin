using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;

namespace AirPodsWin.Services;

/// <summary>Estado de batería decodificado. Valores -1 = desconocido.</summary>
public sealed record AirPodsInfo(
    string Model,
    int Left, int Right, int Case,
    bool LeftCharging, bool RightCharging, bool CaseCharging,
    short Rssi, DateTime Seen);

/// <summary>
/// Escucha los anuncios BLE de Apple (company id 0x004C, tipo 0x07 "proximity pairing")
/// que emiten los AirPods y decodifica la batería de cada auricular y del estuche.
/// </summary>
public sealed class AirPodsScanner : IDisposable
{
    private const ushort AppleCompanyId = 0x004C;

    private readonly BluetoothLEAdvertisementWatcher _watcher =
        new() { ScanningMode = BluetoothLEScanningMode.Passive };
    private readonly Dictionary<ulong, AirPodsInfo> _seen = new();
    private readonly object _lock = new();

    public event Action<AirPodsInfo>? Updated;
    public event Action<string>? Failed;

    public AirPodsScanner()
    {
        _watcher.Received += OnReceived;
        _watcher.Stopped += (_, e) =>
        {
            if (e.Error != BluetoothError.Success)
                Failed?.Invoke(e.Error == BluetoothError.RadioNotAvailable
                    ? "Bluetooth apagado o no disponible"
                    : $"Error de Bluetooth: {e.Error}");
        };
    }

    public void Start()
    {
        try { _watcher.Start(); }
        catch (Exception ex) { Failed?.Invoke(ex.Message); }
    }

    public void Dispose()
    {
        try { _watcher.Stop(); } catch { }
    }

    private void OnReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        foreach (var md in args.Advertisement.ManufacturerData)
        {
            if (md.CompanyId != AppleCompanyId) continue;
            var info = Parse(md.Data.ToArray(), args.RawSignalStrengthInDBm);
            if (info is null) continue;

            AirPodsInfo best;
            lock (_lock)
            {
                _seen[args.BluetoothAddress] = info;
                var cutoff = DateTime.UtcNow.AddSeconds(-10);
                foreach (var k in _seen.Where(kv => kv.Value.Seen < cutoff).Select(kv => kv.Key).ToList())
                    _seen.Remove(k);
                // Si hay varios AirPods cerca, el de señal más fuerte es el más cercano (el tuyo).
                best = _seen.Values.OrderByDescending(v => v.Rssi).First();
            }
            Updated?.Invoke(best);
        }
    }

    // data = payload tras el company id: 07 19 01 <modelHi> <modelLo> <status> <batt> <charge|case> ...
    internal static AirPodsInfo? Parse(byte[] d, short rssi)
    {
        if (d.Length < 8 || d[0] != 0x07) return null;

        int model = (d[3] << 8) | d[4];
        bool flipped = ((d[5] >> 4) & 0x02) == 0;   // indica cuál auricular es "primero" en el anuncio

        int hi = d[6] >> 4, lo = d[6] & 0x0F;
        int left = Pct(flipped ? hi : lo);
        int right = Pct(flipped ? lo : hi);

        int chargeBits = d[7] >> 4;
        int caseNib = d[7] & 0x0F;
        bool chargeL = (chargeBits & (flipped ? 0x02 : 0x01)) != 0;
        bool chargeR = (chargeBits & (flipped ? 0x01 : 0x02)) != 0;
        bool chargeC = (chargeBits & 0x04) != 0;

        return new AirPodsInfo(ModelName(model), left, right, Pct(caseNib),
            chargeL, chargeR, chargeC, rssi, DateTime.UtcNow);
    }

    // Apple solo transmite 0..10 (pasos del 10 %); 15 = desconocido / no presente.
    private static int Pct(int nibble) => nibble switch
    {
        10 => 100,
        >= 0 and < 10 => nibble * 10 + 5,
        _ => -1
    };

    private static string ModelName(int id) => id switch
    {
        0x0220 => "AirPods (1.ª gen)",
        0x0F20 => "AirPods (2.ª gen)",
        0x1320 => "AirPods (3.ª gen)",
        0x1920 => "AirPods (4.ª gen)",
        0x1B20 => "AirPods 4 (cancelación de ruido)",
        0x0E20 => "AirPods Pro",
        0x1420 => "AirPods Pro (2.ª gen)",
        0x2420 => "AirPods Pro (2.ª gen, USB-C)",
        0x0A20 => "AirPods Max",
        0x1F20 => "AirPods Max (USB-C)",
        _ => "AirPods"
    };
}
