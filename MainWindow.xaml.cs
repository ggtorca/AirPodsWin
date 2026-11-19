using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AirPodsWin;

public partial class MainWindow : Window
{
    private DateTime _lastHide = DateTime.MinValue;

    /// <summary>En modo demo el popup no se oculta al perder el foco.</summary>
    public bool KeepOpen { get; set; }

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Deactivated += (_, _) => { if (!KeepOpen) HidePopup(); };
        SizeChanged += (_, _) => { if (IsVisible) Anchor(); };
    }

    // ---------- Aspecto nativo de Windows 11 (acrílico + esquinas redondeadas) ----------
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;

        // Ventana "tool": no aparece en Alt+Tab
        int ex = GetWindowLong(hwnd, -20);
        SetWindowLong(hwnd, -20, ex | 0x80);

        bool win11 = Environment.OSVersion.Version.Build >= 22621;
        if (win11)
        {
            try
            {
                HwndSource.FromHwnd(hwnd)!.CompositionTarget.BackgroundColor = Colors.Transparent;
                var m = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                DwmExtendFrameIntoClientArea(hwnd, ref m);
                int dark = 1, backdrop = 3 /* acrílico transitorio */, corner = 2 /* redondeado */;
                DwmSetWindowAttribute(hwnd, 20, ref dark, 4);
                DwmSetWindowAttribute(hwnd, 38, ref backdrop, 4);
                DwmSetWindowAttribute(hwnd, 33, ref corner, 4);
                Root.Background = new SolidColorBrush(Color.FromArgb(0x70, 0x20, 0x20, 0x24));
                return;
            }
            catch { }
        }
        Root.Background = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x28));
    }

    // ---------- Mostrar / ocultar ----------
    public void TogglePopup()
    {
        if (IsVisible) HidePopup();
        else if ((DateTime.UtcNow - _lastHide).TotalMilliseconds > 300) ShowPopup();
    }

    public void ShowPopup()
    {
        Vm.ShowOutputs = false;
        Card.Opacity = 0;
        Show();
        Anchor();
        Activate();

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        Slide.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
            new DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
    }

    /// <summary>Ancla la ventana a la esquina inferior derecha (la altura cambia según el contenido).</summary>
    private void Anchor()
    {
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - ActualWidth - 12;
        Top = wa.Bottom - ActualHeight - 12;
    }

    public void HidePopup()
    {
        if (!IsVisible) return;
        _lastHide = DateTime.UtcNow;
        Hide();
    }

    // ---------- Salida de audio ----------
    private MainViewModel Vm => (MainViewModel)DataContext;

    private void Mute_Click(object sender, RoutedEventArgs e) => Vm.ToggleMute();

    private void ToggleOutputs_Click(object sender, RoutedEventArgs e) => Vm.ShowOutputs = !Vm.ShowOutputs;

    private void Output_Click(object sender, RoutedEventArgs e) =>
        Vm.SelectOutput((string)((System.Windows.Controls.Button)sender).Tag);

    // ---------- Slider: salta al clic y arrastra ----------
    private void SetFromMouse(MouseEventArgs e)
    {
        double ratio = Math.Clamp(e.GetPosition(VolSlider).X / VolSlider.ActualWidth, 0, 1);
        VolSlider.Value = Math.Round(VolSlider.Minimum + ratio * (VolSlider.Maximum - VolSlider.Minimum));
    }

    private void Vol_Down(object sender, MouseButtonEventArgs e)
    {
        VolSlider.CaptureMouse();
        SetFromMouse(e);
        e.Handled = true;
    }

    private void Vol_Move(object sender, MouseEventArgs e)
    {
        if (VolSlider.IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed) SetFromMouse(e);
    }

    private void Vol_Up(object sender, MouseButtonEventArgs e)
    {
        VolSlider.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void Vol_Wheel(object sender, MouseWheelEventArgs e)
    {
        VolSlider.Value = Math.Clamp(VolSlider.Value + (e.Delta > 0 ? 2 : -2), 0, 100);
        e.Handled = true;
    }

    // ---------- P/Invoke ----------
    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS { public int Left, Right, Top, Bottom; }

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS m);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
}

