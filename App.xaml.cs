using System;
using System.Linq;
using System.Threading;
using System.Windows;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace AirPodsWin;

public partial class App : Application
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunName = "AirPodsWin";

    private Mutex? _mutex;
    private Forms.NotifyIcon? _tray;
    private MainViewModel? _vm;
    private MainWindow? _window;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, "AirPodsWin_SingleInstance", out bool first);
        if (!first) { Shutdown(); return; }

        bool demoMax = e.Args.Contains("--demo-max");
        bool demo = e.Args.Contains("--demo") || demoMax;

        _vm = new MainViewModel(demo, demoMax);
        _window = new MainWindow(_vm) { KeepOpen = demo };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Abrir", null, (_, _) => _window.ShowPopup());
        var autostart = new Forms.ToolStripMenuItem("Iniciar con Windows") { Checked = IsAutostart(), CheckOnClick = true };
        autostart.CheckedChanged += (_, _) => SetAutostart(autostart.Checked);
        menu.Items.Add(autostart);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, _) => Quit());

        _tray = new Forms.NotifyIcon
        {
            Icon = MakeIcon(),
            Visible = true,
            Text = "AirPods",
            ContextMenuStrip = menu
        };
        _tray.MouseClick += (_, a) => { if (a.Button == Forms.MouseButtons.Left) _window.TogglePopup(); };

        _vm.LowBattery += (name, pct) =>
            _tray.ShowBalloonTip(6000, "Batería baja", $"{name}: {pct}%", Forms.ToolTipIcon.Warning);

        _vm.PropertyChanged += (_, _) =>
        {
            var t = _vm.TrayText;
            _tray.Text = t.Length > 63 ? t[..63] : t;
        };

        if (demo || e.Args.Contains("--snapshot")) { _window.KeepOpen = true; _window.ShowPopup(); }

        // Herramienta de desarrollo: --demo --snapshot <ruta.png> guarda capturas de la interfaz y sale.
        int si = Array.IndexOf(e.Args, "--snapshot");
        if (si >= 0 && si + 1 < e.Args.Length)
        {
            string path = e.Args[si + 1];
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            int step = 0;
            t.Tick += (_, _) =>
            {
                Snapshot(path.Replace(".png", step == 0 ? "_main.png" : step == 1 ? "_outputs.png" : "_muted.png"));
                if (step++ == 0) { _vm!.ShowOutputs = true; return; }
                if (step == 2) { _vm!.ShowOutputs = false; _vm.ToggleMute(); return; }
                _vm!.ToggleMute();   // restaura el estado original del sonido
                t.Stop(); Quit();
            };
            t.Start();
        }
    }

    private void Snapshot(string file)
    {
        var w = _window!;
        w.UpdateLayout();
        int pw = (int)w.ActualWidth, ph = (int)w.ActualHeight;
        var dv = new System.Windows.Media.DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2E, 0x2E, 0x36)),
                null, new Rect(0, 0, pw, ph));
            dc.DrawRectangle(new System.Windows.Media.VisualBrush((System.Windows.Media.Visual)w.Content), null, new Rect(0, 0, pw, ph));
        }
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(pw * 2, ph * 2, 192, 192, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(dv);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
        using var fs = System.IO.File.Create(file);
        enc.Save(fs);
    }

    private void Quit()
    {
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        _vm?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    // ---------- Icono de bandeja (el logo, en el tamaño que pide Windows) ----------
    private static Drawing.Icon MakeIcon()
    {
        var uri = new Uri("pack://application:,,,/Assets/icon.ico");
        using var stream = GetResourceStream(uri).Stream;
        return new Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
    }
    // ---------- Inicio automático ----------
    private static bool IsAutostart()
    {
        using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(RunName) != null;
    }

    private static void SetAutostart(bool on)
    {
        using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, true);
        if (k == null) return;
        if (on) k.SetValue(RunName, $"\"{Environment.ProcessPath}\"");
        else k.DeleteValue(RunName, false);
    }
}

