using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AirPodsWin;

public partial class BatteryRing : UserControl
{
    public static readonly DependencyProperty PercentProperty = Reg<int>(nameof(Percent), -1);
    public static readonly DependencyProperty IsChargingProperty = Reg<bool>(nameof(IsCharging), false);
    public static readonly DependencyProperty TitleProperty = Reg<string>(nameof(Title), "");
    public static readonly DependencyProperty IconDataProperty = Reg<Geometry?>(nameof(IconData), null);
    public static readonly DependencyProperty MirrorProperty = Reg<bool>(nameof(Mirror), false);

    private static DependencyProperty Reg<T>(string name, T def) =>
        DependencyProperty.Register(name, typeof(T), typeof(BatteryRing),
            new PropertyMetadata(def, (d, _) => ((BatteryRing)d).Update()));

    public int Percent { get => (int)GetValue(PercentProperty); set => SetValue(PercentProperty, value); }
    public bool IsCharging { get => (bool)GetValue(IsChargingProperty); set => SetValue(IsChargingProperty, value); }
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public Geometry? IconData { get => (Geometry?)GetValue(IconDataProperty); set => SetValue(IconDataProperty, value); }
    public bool Mirror { get => (bool)GetValue(MirrorProperty); set => SetValue(MirrorProperty, value); }

    private static readonly Brush Green = Freeze(Color.FromRgb(0x30, 0xD1, 0x58));
    private static readonly Brush Red = Freeze(Color.FromRgb(0xFF, 0x45, 0x3A));
    private static Brush Freeze(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    public BatteryRing()
    {
        InitializeComponent();
        Update();
    }

    private void Update()
    {
        if (Arc == null) return; // aún no inicializado

        int p = Percent;
        bool known = p >= 0;

        PercentText.Text = known ? $"{p}%" : "—";
        Bolt.Visibility = known && IsCharging ? Visibility.Visible : Visibility.Collapsed;
        TitleText.Text = Title;
        Icon.Data = IconData;
        IconBox.RenderTransform = new ScaleTransform(Mirror ? -1 : 1, 1);

        if (!known) { Arc.Data = null; return; }

        Arc.Stroke = p <= 20 && !IsCharging ? Red : Green;

        const double r = 31.5, c = 34;
        double angle = Math.Clamp(p, 0, 100) / 100.0 * 360;
        if (angle >= 360) angle = 359.99;
        double rad = (angle - 90) * Math.PI / 180;
        var end = new Point(c + r * Math.Cos(rad), c + r * Math.Sin(rad));

        var fig = new PathFigure { StartPoint = new Point(c, c - r) };
        fig.Segments.Add(new ArcSegment(end, new Size(r, r), 0, angle > 180, SweepDirection.Clockwise, true));
        Arc.Data = new PathGeometry(new[] { fig });
    }
}
