using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ClaudeUsage.Core.Services;

namespace ClaudeUsage.UI;

public partial class SetupWindow : Window
{
    private readonly CredentialsStore _store;
    private readonly ClaudeConfigReader _configReader;
    private readonly Func<Task>? _onSaved;

    public SetupWindow(CredentialsStore store, ClaudeConfigReader configReader, Func<Task>? onSaved = null)
    {
        _store = store;
        _configReader = configReader;
        _onSaved = onSaved;

        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Pre-fill existing creds
        var creds = _store.Load();
        if (!string.IsNullOrWhiteSpace(creds.SessionKey))
            SessionKeyBox.Password = creds.SessionKey;

        // Try auto-detecting org ID from ~/.claude.json
        var detectedOrgId = _configReader.TryReadOrganizationId();
        if (!string.IsNullOrWhiteSpace(detectedOrgId))
        {
            OrgIdBox.Style = (Style)Resources["GreenInputField"];
            OrgIdBox.Text = detectedOrgId;
            OrgIdAutoHint.Visibility = Visibility.Visible;
            OrgIdManualHint.Visibility = Visibility.Collapsed;
        }
        else
        {
            // Fall back to saved value
            OrgIdBox.Style = (Style)Resources["InputField"];
            OrgIdBox.Text = creds.OrganizationId;
            OrgIdAutoHint.Visibility = Visibility.Collapsed;
            OrgIdManualHint.Visibility = Visibility.Visible;
        }

        SessionKeyBox.Focus();
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;

        var sessionKey = SessionKeyBox.Password.Trim();
        var orgId = OrgIdBox.Text.Trim();

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
        SaveBtn.Content = "Saving...";

        _store.Save(new Credentials
        {
            SessionKey = sessionKey,
            OrganizationId = orgId,
        });

        if (_onSaved != null)
            await _onSaved();

        Close();
    }

    private void ShowError(string msg)
    {
        ErrorText.Text = msg;
        ErrorText.Visibility = Visibility.Visible;
        SaveBtn.IsEnabled = true;
        SaveBtn.Content = "Save & Connect";
    }

    private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }
}
