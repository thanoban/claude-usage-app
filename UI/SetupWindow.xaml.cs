using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using ClaudeUsage.Core.Services;

namespace ClaudeUsage.UI;

public partial class SetupWindow : Window
{
    private readonly CredentialsStore _store;
    private readonly ClaudeConfigReader _configReader;
    private readonly BrowserCookieReader _cookieReader;
    private readonly Func<Task<string?>>? _onSaved;

    // Detected org ID cached from ~/.claude.json
    private string? _detectedOrgId;

    public SetupWindow(CredentialsStore store, ClaudeConfigReader configReader, Func<Task<string?>>? onSaved = null)
    {
        _store        = store;
        _configReader = configReader;
        _cookieReader = new BrowserCookieReader();
        _onSaved      = onSaved;

        InitializeComponent();
        Loaded += OnLoaded;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _detectedOrgId = _configReader.TryReadOrganizationId();

        // Run browser scan off the UI thread
        ShowPanel(Panel.Scanning);
        SubtitleText.Text = "Scanning your browsers…";

        List<DiscoveredSession> sessions = [];
        try
        {
            sessions = await Task.Run(() => _cookieReader.ScanAllBrowsers());
        }
        catch { /* never crash — fall through to manual */ }

        if (sessions.Count == 1)
        {
            // Lucky path: exactly one account → auto-connect
            await AutoConnect(sessions[0]);
        }
        else if (sessions.Count > 1)
        {
            // Multiple accounts: show picker
            SubtitleText.Text = $"{sessions.Count} Claude sessions found";
            SessionList.ItemsSource = sessions;
            ShowPanel(Panel.Picker);
        }
        else
        {
            // Nothing found: show manual form
            SubtitleText.Text = "Enter your credentials manually";
            LoadManualPanel();
            ShowPanel(Panel.Manual);
        }
    }

    // ── Auto-connect (single session or user picked one) ─────────────────────

    private async Task AutoConnect(DiscoveredSession session)
    {
        ShowPanel(Panel.Connecting);
        ConnectingBrowserText.Text = $"{session.BrowserName}  ·  {session.ProfileName}";
        SubtitleText.Text = "Connecting…";
        ErrorText.Visibility = Visibility.Collapsed;

        var orgId = _detectedOrgId ?? string.Empty;

        _store.Save(new Credentials
        {
            SessionKey     = session.SessionKey,
            OrganizationId = orgId,
        });

        // If org ID is missing, we'll still try — error will show if API rejects
        if (_onSaved != null)
        {
            var error = await _onSaved();
            if (!string.IsNullOrEmpty(error))
            {
                // If auto-connect fails, drop to manual with error shown
                SubtitleText.Text = "Enter your credentials manually";
                LoadManualPanel();
                ShowPanel(Panel.Manual);
                ShowError(error);
                return;
            }
        }

        Close();
    }

    // ── Manual panel setup ────────────────────────────────────────────────────

    private void LoadManualPanel()
    {
        var creds = _store.Load();

        if (!string.IsNullOrWhiteSpace(creds.SessionKey))
            SessionKeyBox.Password = creds.SessionKey;

        if (!string.IsNullOrWhiteSpace(_detectedOrgId))
        {
            OrgIdBox.Style = (Style)Resources["GreenInputPasswordField"];
            OrgIdBox.Password = _detectedOrgId;
            OrgIdAutoHint.Visibility   = Visibility.Visible;
            OrgIdManualHint.Visibility = Visibility.Collapsed;
        }
        else
        {
            OrgIdBox.Style = (Style)Resources["InputPasswordField"];
            OrgIdBox.Password = creds.OrganizationId ?? "";
            OrgIdAutoHint.Visibility   = Visibility.Collapsed;
            OrgIdManualHint.Visibility = Visibility.Visible;
        }

        SessionKeyBox.Focus();
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private async void SessionCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn &&
            btn.Tag is DiscoveredSession session)
        {
            await AutoConnect(session);
        }
    }

    private void ManualEntryBtn_Click(object sender, RoutedEventArgs e)
    {
        SubtitleText.Text = "Enter your credentials manually";
        LoadManualPanel();
        ShowPanel(Panel.Manual);
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;

        var rawSession   = SessionKeyBox.Password.Trim();
        var matchSession = Regex.Match(rawSession, @"sk-ant-[A-Za-z0-9\-_]+");
        var sessionKey   = matchSession.Success ? matchSession.Value : rawSession;

        var rawOrg   = OrgIdBox.Password.Trim();
        var matchOrg = Regex.Match(rawOrg, @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
        var orgId    = matchOrg.Success ? matchOrg.Value : rawOrg;

        if (string.IsNullOrWhiteSpace(sessionKey))
        {
            ShowError("Please enter your session key.");
            SessionKeyBox.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(orgId))
        {
            ShowError("Please enter your Organization ID.");
            OrgIdBox.Focus();
            return;
        }

        SaveBtn.IsEnabled = false;
        SaveBtn.Content   = "Connecting…";

        _store.Save(new Credentials
        {
            SessionKey     = sessionKey,
            OrganizationId = orgId,
        });

        if (_onSaved != null)
        {
            var error = await _onSaved();
            if (!string.IsNullOrEmpty(error))
            {
                ShowError(error);
                return;
            }
        }

        Close();
    }

    private void ShowError(string msg)
    {
        ErrorText.Text       = msg;
        ErrorText.Visibility = Visibility.Visible;
        SaveBtn.IsEnabled    = true;
        SaveBtn.Content      = "Save & Connect";
    }

    private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    // ── Panel switching ───────────────────────────────────────────────────────

    private enum Panel { Scanning, Picker, Connecting, Manual }

    private void ShowPanel(Panel panel)
    {
        ScanningPanel.Visibility   = panel == Panel.Scanning   ? Visibility.Visible : Visibility.Collapsed;
        PickerPanel.Visibility     = panel == Panel.Picker     ? Visibility.Visible : Visibility.Collapsed;
        ConnectingPanel.Visibility = panel == Panel.Connecting ? Visibility.Visible : Visibility.Collapsed;
        ManualPanel.Visibility     = panel == Panel.Manual     ? Visibility.Visible : Visibility.Collapsed;
    }
}
