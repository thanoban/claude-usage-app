using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ClaudeUsage.Core;
using ClaudeUsage.Core.Models;
using ClaudeUsage.Core.Services;
using ClaudeUsage.UI.Controls;
using WinForms = System.Windows.Forms;

namespace ClaudeUsage.UI;

public partial class PopupWindow : Window
{
    private readonly UsageFetcher _fetcher;
    private readonly CredentialsStore _store;
    private readonly ClaudeConfigReader _configReader;
    private readonly DispatcherTimer _clockTimer;
    private Storyboard? _spinStoryboard;
    private bool _isSpinning;

    public PopupWindow(UsageFetcher fetcher, CredentialsStore store, ClaudeConfigReader configReader)
    {
        _fetcher = fetcher;
        _store = store;
        _configReader = configReader;

        InitializeComponent();

        _fetcher.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(UsageFetcher.Usage))
                Dispatcher.Invoke(RenderUsage);
        };

        // Updates "Xm ago" text every 30s while popup is visible
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _clockTimer.Tick += (_, _) => UpdateLastUpdatedText();

        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) _clockTimer.Start();
            else _clockTimer.Stop();
        };

        Deactivated += (_, _) => Hide();
        Loaded += (_, _) => { PositionWindow(); RenderUsage(); };

        _spinStoryboard = (Storyboard)Resources["SpinAnimation"];
    }

    // ── Positioning ──────────────────────────────────────────────────────────

    public void PositionWindow()
    {
        var workArea = SystemParameters.WorkArea;

        double left = workArea.Right - Width - 12;
        double top = workArea.Bottom - ActualHeight - 8;

        // Detect taskbar edge using primary screen bounds vs work area
        try
        {
            var screen = WinForms.Screen.PrimaryScreen;
            if (screen != null)
            {
                var full = screen.Bounds;
                var work = screen.WorkingArea;

                bool taskbarOnRight = work.Right < full.Right;
                bool taskbarOnLeft  = work.Left  > full.Left;
                bool taskbarOnTop   = work.Top   > full.Top;

                if (taskbarOnRight)
                {
                    left = workArea.Right - Width - 12;
                    top  = workArea.Bottom - ActualHeight - 8;
                }
                else if (taskbarOnLeft)
                {
                    left = workArea.Left + 12;
                    top  = workArea.Bottom - ActualHeight - 8;
                }
                else if (taskbarOnTop)
                {
                    left = workArea.Right - Width - 12;
                    top  = workArea.Top + 8;
                }
                // else: bottom taskbar (default)
            }
        }
        catch { /* fall back to defaults */ }

        Left = Math.Max(left, workArea.Left);
        Top  = Math.Max(top,  workArea.Top + 8);
    }

    // ── Rendering ─────────────────────────────────────────────────────────────

    private void RenderUsage()
    {
        var info = _fetcher.Usage;

        // Loading with no cached data
        if (info.IsLoading && info.Limits.Count == 0)
        {
            LoadingPanel.Visibility = Visibility.Visible;
            ErrorPanel.Visibility   = Visibility.Collapsed;
            LimitsPanel.Visibility  = Visibility.Collapsed;
            SetSpinning(true);
            return;
        }

        SetSpinning(info.IsLoading);

        // Error with no data at all
        if (!string.IsNullOrEmpty(info.Error) && info.Limits.Count == 0)
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility   = Visibility.Visible;
            LimitsPanel.Visibility  = Visibility.Collapsed;
            ErrorText.Text  = info.Error;
            // Only show "update session key" link for auth errors
            UpdateKeyLink.Visibility = info.Error.Contains("expired") || info.Error.Contains("session")
                ? Visibility.Visible : Visibility.Collapsed;
            UpdateLastUpdatedText();
            return;
        }

        // Normal data view
        LoadingPanel.Visibility = Visibility.Collapsed;
        LimitsPanel.Visibility  = Visibility.Visible;

        // Show inline error banner if we have data but also an error
        if (!string.IsNullOrEmpty(info.Error))
        {
            ErrorPanel.Visibility = Visibility.Visible;
            ErrorText.Text = info.Error;
            UpdateKeyLink.Visibility = Visibility.Collapsed;
        }
        else
        {
            ErrorPanel.Visibility = Visibility.Collapsed;
        }

        var rows = new[] { Row1, Row2, Row3 };
        for (int i = 0; i < rows.Length; i++)
        {
            if (i < info.Limits.Count)
            {
                rows[i].Visibility = Visibility.Visible;
                rows[i].Limit = info.Limits[i];
            }
            else
            {
                rows[i].Visibility = Visibility.Collapsed;
            }
        }

        UpdateLastUpdatedText();
        Dispatcher.InvokeAsync(PositionWindow, DispatcherPriority.Loaded);
    }

    private void UpdateLastUpdatedText()
    {
        var info = _fetcher.Usage;
        if (info.LastUpdated == default)
        {
            LastUpdatedText.Text = "—";
            return;
        }
        var elapsed = DateTime.Now - info.LastUpdated;
        LastUpdatedText.Text = elapsed.TotalSeconds < 60 ? "Just now"
            : elapsed.TotalMinutes < 60 ? $"{(int)elapsed.TotalMinutes}m ago"
            : $"{(int)elapsed.TotalHours}h ago";
    }

    private void SetSpinning(bool spin)
    {
        if (spin == _isSpinning) return;
        _isSpinning = spin;
        RefreshButton.IsEnabled = !spin;
        if (spin) _spinStoryboard?.Begin();
        else      _spinStoryboard?.Stop();
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await _fetcher.RefreshAsync();
    }

    private void GearButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        var setup = new SetupWindow(_store, _configReader, async () =>
        {
            await _fetcher.RefreshAsync();
        });
        setup.ShowDialog();
    }

    private void QuitButton_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.Application.Current.Shutdown();
    }
}
