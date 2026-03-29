using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClaudeUsage.Core.Models;

namespace ClaudeUsage.UI.Controls;

public partial class LimitRowControl : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty LimitProperty =
        DependencyProperty.Register(nameof(Limit), typeof(LimitData), typeof(LimitRowControl),
            new PropertyMetadata(null, OnLimitChanged));

    public LimitData? Limit
    {
        get => (LimitData?)GetValue(LimitProperty);
        set => SetValue(LimitProperty, value);
    }

    public LimitRowControl()
    {
        InitializeComponent();
        // Subscribe to size changes once, here in constructor
        SizeChanged += (_, _) => RefreshBarWidth();
        Loaded += (_, _) => RefreshBarWidth();
    }

    private static void OnLimitChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is LimitRowControl ctrl && e.NewValue is LimitData data)
            ctrl.Render(data);
    }

    private void Render(LimitData data)
    {
        var pct = Math.Clamp(data.Percentage, 0, 100);
        var color = GetBarColor(pct);

        LabelText.Text = data.Label;

        PercentText.Text = $"{pct:0}%";
        PercentText.Foreground = new SolidColorBrush(color);

        ProgressBar.Background = new SolidColorBrush(color);

        // Store for resize callbacks
        Tag = pct;

        RefreshBarWidth();

        ResetText.Text = FormatResetTime(data.ResetsAt);
    }

    private void RefreshBarWidth()
    {
        if (Tag is not double pct) return;
        var track = ProgressBar.Parent as FrameworkElement;
        if (track == null || track.ActualWidth <= 0) return;
        ProgressBar.Width = Math.Max(2, track.ActualWidth * (pct / 100.0));
    }

    private static System.Windows.Media.Color GetBarColor(double pct) => pct switch
    {
        >= 95 => System.Windows.Media.Color.FromRgb(0xEF, 0x44, 0x44),  // Red
        >= 80 => System.Windows.Media.Color.FromRgb(0xF5, 0x9E, 0x0B),  // Amber
        _ => System.Windows.Media.Color.FromRgb(0x22, 0xC5, 0x5E),       // Green
    };

    private static string FormatResetTime(DateTime? resetsAt)
    {
        if (resetsAt == null) return string.Empty;

        var dt = resetsAt.Value;
        var offset = TimeZoneInfo.Local.GetUtcOffset(dt);
        var sign = offset >= TimeSpan.Zero ? "+" : "-";
        var offsetStr = $"GMT{sign}{Math.Abs(offset.Hours)}:{offset.Minutes:D2}";
        var timeStr = dt.ToString("h:mmtt", CultureInfo.InvariantCulture).ToLower();

        if (dt.Date == DateTime.Today)
            return $"Resets {timeStr} ({offsetStr})";

        var dateStr = dt.ToString("MMM d", CultureInfo.InvariantCulture);
        return $"Resets {dateStr} at {timeStr} ({offsetStr})";
    }
}
