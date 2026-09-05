using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using DaevaMini.Config;
using DaevaMini.Services;

namespace DaevaMini.Views.Max;

/// <summary>
/// Picks the strip color for when the machine is idle: not in settings, not mid-pour
/// (splash and menu screens). Every change is sent to the strip immediately, so what you
/// see on the machine while this page is open is the real color, not a swatch - see
/// <see cref="MainWindow.ApplyIdleTint"/>, which leaves this page out of the usual
/// settings-orange tint precisely so that preview is not overridden. "SALVA COLORE" is
/// what keeps the choice after you leave the page.
/// </summary>
public partial class LedColorPage : UserControl, INotifyPropertyChanged
{
    /// <summary>Firmware's own default WAIT color (kBluDaeva), so "predefinito" restores
    /// exactly what an unedited config already looks like.</summary>
    private static readonly (byte R, byte G, byte B) DefaultColor = (202, 240, 248);

    private byte _r;
    private byte _g;
    private byte _b;

    public LedColorPage()
    {
        InitializeComponent();

        var stored = AppConfigService.Instance.Config.IdleLedRgb;
        _r = stored.R;
        _g = stored.G;
        _b = stored.B;

        RefreshPreview();
        SendPreview();
    }

    public string RgbLabel => $"R {_r} · G {_g} · B {_b}";
    public IBrush PreviewBrush => new SolidColorBrush(Color.FromRgb(_r, _g, _b));

    private void OnBackClick(object? sender, RoutedEventArgs e)
    {
        if (VisualRoot is MainWindow mainWindow)
            mainWindow.ShowSettingsPage();
    }

    /// <summary>Tag is "r,g,b" for a quick-pick swatch.</summary>
    private void OnPresetClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string csv }) return;

        var parts = csv.Split(',');
        if (parts.Length != 3
            || !byte.TryParse(parts[0], out byte r)
            || !byte.TryParse(parts[1], out byte g)
            || !byte.TryParse(parts[2], out byte b))
            return;

        _r = r;
        _g = g;
        _b = b;

        StatusLine.Text = string.Empty;
        RefreshPreview();
        SendPreview();
    }

    private void OnResetClick(object? sender, RoutedEventArgs e)
    {
        _r = DefaultColor.R;
        _g = DefaultColor.G;
        _b = DefaultColor.B;

        RefreshPreview();
        SendPreview();
        Save("Colore default: ripristinato il valore di fabbrica.");
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
        => Save("Colore default aggiornato.");

    private void Save(string message)
    {
        var config = AppConfigService.Instance.Config;
        config.IdleLedR = _r;
        config.IdleLedG = _g;
        config.IdleLedB = _b;

        AppConfigService.Instance.SaveConfig("idle-led-color");
        StatusLine.Text = message;
    }

    /// <summary>Fire-and-forget, like every other tint change: this is decoration and the
    /// UI must not wait on the serial port while a finger is still on the wheel.</summary>
    private void SendPreview()
    {
        string command = ArduinoProtocolHelper.BuildTintCommand((_r, _g, _b));
        Task.Run(() => ArduinoSerialManager.Instance.Send(command));
    }

    private void RefreshPreview()
    {
        OnPropertyChanged(nameof(RgbLabel));
        OnPropertyChanged(nameof(PreviewBrush));
    }

    // ------------------------------------------------------------ the colour wheel

    /// <summary>Wheel diameter in the page's design pixels; the bitmap is built at 1:1.</summary>
    private const int WheelSize = 420;
    private const double WheelRadius = WheelSize / 2.0;
    private const double MarkerSize = 34;
    private const double BarWidth = 420;

    private double _hue;
    private double _sat;
    private double _val = 1;

    public bool IsWheelOpen { get; private set; }

    /// <summary>
    /// Painted pixel by pixel rather than with a ConicGradientBrush so that the picture
    /// and the hit test come from the same two lines of maths: with a brush, a difference
    /// of opinion about where 0° sits would hand back a colour other than the one touched.
    /// </summary>
    public static Bitmap WheelImage { get; } = BuildWheel();

    public double MarkerLeft { get; private set; }
    public double MarkerTop { get; private set; }
    public double BrightnessHandleLeft { get; private set; }

    /// <summary>Black to the fully lit version of the chosen hue: the bar is the value axis.</summary>
    public IBrush BrightnessBrush
    {
        get
        {
            var (r, g, b) = HsvToRgb(_hue, _sat, 1);
            return new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Colors.Black, 0),
                    new GradientStop(Color.FromRgb(r, g, b), 1),
                },
            };
        }
    }

    private void OnCustomiseClick(object? sender, RoutedEventArgs e)
    {
        (_hue, _sat, _val) = RgbToHsv(_r, _g, _b);
        IsWheelOpen = true;

        StatusLine.Text = string.Empty;
        RefreshWheel();
    }

    private void OnCloseWheel(object? sender, RoutedEventArgs e)
    {
        IsWheelOpen = false;
        OnPropertyChanged(nameof(IsWheelOpen));
    }

    private void OnWheelPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Visual visual)
            PickFromWheel(e.GetPosition(visual));
    }

    private void OnWheelPointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not Visual visual) return;
        if (!e.GetCurrentPoint(visual).Properties.IsLeftButtonPressed) return;

        PickFromWheel(e.GetPosition(visual));
    }

    /// <summary>Angle around the wheel is the hue, distance from the middle is the
    /// saturation - so the washed-out colours sit in the centre, as on a phone.</summary>
    private void PickFromWheel(Point point)
    {
        double dx = point.X - WheelRadius;
        double dy = point.Y - WheelRadius;

        _hue = AngleToHue(dx, dy);
        _sat = Math.Clamp(Math.Sqrt(dx * dx + dy * dy) / WheelRadius, 0, 1);

        ApplyHsv();
    }

    private void OnBrightnessPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Visual visual)
            PickBrightness(e.GetPosition(visual));
    }

    private void OnBrightnessPointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not Visual visual) return;
        if (!e.GetCurrentPoint(visual).Properties.IsLeftButtonPressed) return;

        PickBrightness(e.GetPosition(visual));
    }

    private void PickBrightness(Point point)
    {
        _val = Math.Clamp(point.X / BarWidth, 0, 1);
        ApplyHsv();
    }

    private void ApplyHsv()
    {
        (_r, _g, _b) = HsvToRgb(_hue, _sat, _val);

        RefreshPreview();
        RefreshWheel();
        SendPreview();
    }

    private void RefreshWheel()
    {
        double radius = _sat * WheelRadius;
        double theta = _hue * Math.PI / 180.0;

        MarkerLeft = WheelRadius + radius * Math.Sin(theta) - MarkerSize / 2;
        MarkerTop = WheelRadius - radius * Math.Cos(theta) - MarkerSize / 2;
        BrightnessHandleLeft = _val * BarWidth - 5;

        OnPropertyChanged(nameof(IsWheelOpen));
        OnPropertyChanged(nameof(MarkerLeft));
        OnPropertyChanged(nameof(MarkerTop));
        OnPropertyChanged(nameof(BrightnessHandleLeft));
        OnPropertyChanged(nameof(BrightnessBrush));
    }

    /// <summary>Clockwise from twelve o'clock, matching <see cref="RefreshWheel"/> and the
    /// bitmap below. Screen y grows downwards, hence the negated dy.</summary>
    private static double AngleToHue(double dx, double dy)
        => (Math.Atan2(dx, -dy) * 180.0 / Math.PI + 360) % 360;

    private static Bitmap BuildWheel()
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(WheelSize, WheelSize), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Unpremul);

        var pixels = new byte[WheelSize * WheelSize * 4];

        for (int y = 0; y < WheelSize; y++)
        {
            for (int x = 0; x < WheelSize; x++)
            {
                double dx = x - WheelRadius + 0.5;
                double dy = y - WheelRadius + 0.5;
                double distance = Math.Sqrt(dx * dx + dy * dy);
                int i = (y * WheelSize + x) * 4;

                if (distance > WheelRadius)
                    continue; // left transparent: the bitmap is square, the wheel is not

                var (r, g, b) = HsvToRgb(AngleToHue(dx, dy), Math.Min(1, distance / WheelRadius), 1);

                pixels[i] = b;
                pixels[i + 1] = g;
                pixels[i + 2] = r;
                // One pixel of alpha ramp at the rim, or the circle reads as jagged.
                pixels[i + 3] = (byte)(255 * Math.Clamp(WheelRadius - distance, 0, 1));
            }
        }

        using (var buffer = bitmap.Lock())
        {
            for (int y = 0; y < WheelSize; y++)
                Marshal.Copy(pixels, y * WheelSize * 4,
                    IntPtr.Add(buffer.Address, y * buffer.RowBytes), WheelSize * 4);
        }

        return bitmap;
    }

    private static (byte R, byte G, byte B) HsvToRgb(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;

        double c = v * s;
        double x = c * (1 - Math.Abs(h / 60.0 % 2 - 1));
        double m = v - c;

        (double r, double g, double b) = h switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };

        return ((byte)Math.Round((r + m) * 255),
                (byte)Math.Round((g + m) * 255),
                (byte)Math.Round((b + m) * 255));
    }

    private static (double H, double S, double V) RgbToHsv(byte r, byte g, byte b)
    {
        double rd = r / 255.0, gd = g / 255.0, bd = b / 255.0;
        double max = Math.Max(rd, Math.Max(gd, bd));
        double min = Math.Min(rd, Math.Min(gd, bd));
        double delta = max - min;

        double h = 0;
        if (delta > 0.00001)
        {
            if (max == rd) h = 60 * (((gd - bd) / delta) % 6);
            else if (max == gd) h = 60 * ((bd - rd) / delta + 2);
            else h = 60 * ((rd - gd) / delta + 4);
        }

        if (h < 0) h += 360;

        return (h, max <= 0 ? 0 : delta / max, max);
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
