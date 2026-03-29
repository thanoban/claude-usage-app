using System.Windows;
using ClaudeUsage.Core;
using ClaudeUsage.Core.Services;

namespace ClaudeUsage.UI;

public class TrayManager : IDisposable
{
    private readonly Hardcodet.Wpf.TaskbarNotification.TaskbarIcon _trayIcon;
    private readonly PopupWindow _popup;
    private readonly CredentialsStore _store;
    private readonly ClaudeConfigReader _configReader;
    private readonly UsageFetcher _fetcher;

    public TrayManager(PopupWindow popup, UsageFetcher fetcher,
                       CredentialsStore store, ClaudeConfigReader configReader)
    {
        _popup = popup;
        _fetcher = fetcher;
        _store = store;
        _configReader = configReader;

        _trayIcon = new Hardcodet.Wpf.TaskbarNotification.TaskbarIcon
        {
            ToolTipText = "Claude Usage — Click to view limits",
            IconSource = LoadIcon(),
        };

        // Build context menu
        var menu = new System.Windows.Controls.ContextMenu();

        var openItem = new System.Windows.Controls.MenuItem { Header = "Open" };
        openItem.Click += (_, _) => TogglePopup();

        var quitItem = new System.Windows.Controls.MenuItem { Header = "Quit" };
        quitItem.Click += (_, _) => System.Windows.Application.Current.Shutdown();

        menu.Items.Add(openItem);
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(quitItem);

        _trayIcon.ContextMenu = menu;

        // Left click toggles popup
        _trayIcon.TrayLeftMouseDown += (_, _) => TogglePopup();
    }

    private void TogglePopup()
    {
        if (_popup.IsVisible)
        {
            _popup.Hide();
        }
        else
        {
            _popup.Show();
            _popup.PositionWindow();
            _popup.Activate();
        }
    }

    private static System.Windows.Media.ImageSource? LoadIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Resources/icon.ico", UriKind.Absolute);
            var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(
                uri,
                System.Windows.Media.Imaging.BitmapCreateOptions.DelayCreation,
                System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
            return decoder.Frames[0];
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _trayIcon?.Dispose();
    }
}
