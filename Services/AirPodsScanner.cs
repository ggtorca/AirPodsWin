using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;

namespace AirPodsWin.Services;

/// <summary>
/// Estado de batería decodificado. Valores -1 = desconocido.
/// Auriculares con estuche: Left / Right / Case. AirPods Max (IsHeadset): Headset.
/// </summary>
public sealed record AirPodsInfo(
    string Model,
    int Left, int Right, int Case,
    bool LeftCharging, bool RightCharging, bool CaseCharging,
    short Rssi, DateTime Seen,
    bool IsHeadset = false, int Headset = -1, bool HeadsetCharging = false);

/// <summary>
/// Escucha los anuncios BLE de Apple (company id 0x004C, tipo 0x07 "proximity pairing")
/// que emiten los AirPods y decodifica la batería. Solo se aceptan modelos de AirPods conocidos;
/// Beats y otros dispositivos de Apple se descartan.
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

    // data = payload tras el company id: 07 19 01 <modelHi> <modelLo> <status> <batt> <flags|case> ...
    internal static AirPodsInfo? Parse(byte[] d, short rssi)
    {
        // Mensaje "proximity pairing" con la longitud exacta de los AirPods (0x19) y prefijo de estado 0x01.
        // Otras tramas tipo 0x07 (p. ej. al conectar) no usan este formato y darían datos basura.
        if (d.Length < 8 || d[0] != 0x07 || d[1] != 0x19 || d[2] != 0x01) return null;

        int model = (d[3] << 8) | d[4];
        var m = Lookup(model);
        if (m is null) return null;   // modelo desconocido, Beats u otro dispositivo de Apple

        int chargeBits = d[7] >> 4;

        if (m.Value.Headset)
        {
            // AirPods Max: una sola batería (nibble bajo), sin estuche. Bit 0 de los flags = cargando.
            return new AirPodsInfo(m.Value.Name, -1, -1, -1, false, false, false, rssi, DateTime.UtcNow,
                IsHeadset: true, Headset: Pct(d[6] & 0x0F), HeadsetCharging: (chargeBits & 0x01) != 0);
        }

        bool flipped = ((d[5] >> 4) & 0x02) == 0;   // indica cuál auricular es "primero" en el anuncio

        int hi = d[6] >> 4, lo = d[6] & 0x0F;
        int left = Pct(flipped ? hi : lo);
        int right = Pct(flipped ? lo : hi);

        int caseNib = d[7] & 0x0F;
        bool chargeL = (chargeBits & (flipped ? 0x02 : 0x01)) != 0;
        bool chargeR = (chargeBits & (flipped ? 0x01 : 0x02)) != 0;
        bool chargeC = (chargeBits & 0x04) != 0;

        return new AirPodsInfo(m.Value.Name, left, right, Pct(caseNib),
            chargeL, chargeR, chargeC, rssi, DateTime.UtcNow);
    }

    // Apple solo transmite 0..10 (pasos del 10 %); 15 = desconocido / no presente.
    private static int Pct(int nibble) => nibble switch
    {
        10 => 100,
        >= 0 and < 10 => nibble * 10 + 5,
        _ => -1
    };

    // Identificadores de modelo de AirPods (2 bytes tras el prefijo). Beats usa otros y queda fuera.
    private static (string Name, bool Headset)? Lookup(int id) => id switch
    {
        0x0220 => ("AirPods (1.ª gen)", false),
        0x0F20 => ("AirPods (2.ª gen)", false),
        0x1320 => ("AirPods (3.ª gen)", false),
        0x1920 => ("AirPods (4.ª gen)", false),
        0x1B20 => ("AirPods 4 (cancelación de ruido)", false),
        0x3620 => ("AirPods (5.ª gen)", false),
        0x3020 => ("AirPods (5.ª gen)", false),
        0x0E20 => ("AirPods Pro", false),
        0x1420 => ("AirPods Pro (2.ª gen)", false),
        0x2420 => ("AirPods Pro (2.ª gen, USB-C)", false),
        0x2720 => ("AirPods Pro (3.ª gen)", false),
        0x0A20 => ("AirPods Max", true),
        0x1F20 => ("AirPods Max (USB-C)", true),
        0x2D20 => ("AirPods Max (2.ª gen)", true),
        _ => null
    };
}
