using System.Threading;
using System.Windows;
using ClaudeUsage.Core;
using ClaudeUsage.Core.Services;
using ClaudeUsage.UI;

namespace ClaudeUsage;

public partial class App : System.Windows.Application
{
    private static Mutex? _mutex;
    private TrayManager? _trayManager;
    private UsageFetcher? _fetcher;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // ── Single-instance guard ─────────────────────────────────────────
        _mutex = new Mutex(true, "ClaudeUsageApp_SingleInstance", out bool isNewInstance);
        if (!isNewInstance)
        {
            // Another instance is running — just exit
            Shutdown();
            return;
        }

        // ── Service wiring ────────────────────────────────────────────────
        var store = new CredentialsStore();
        var configReader = new ClaudeConfigReader();
        var api = new ClaudeApiService();

        _fetcher = new UsageFetcher(api, store);

        var popup = new PopupWindow(_fetcher, store, configReader);

        _trayManager = new TrayManager(popup, _fetcher, store, configReader);

        // ── First launch ──────────────────────────────────────────────────
        var creds = store.Load();
        if (!store.IsConfigured(creds))
        {
            // Show setup window after short delay so tray icon appears first
            await Task.Delay(500);
            var setup = new SetupWindow(store, configReader, async () =>
            {
                _fetcher.StartTimer();
                await _fetcher.RefreshAsync();
                return _fetcher.Usage.Error;
            });
            setup.Show();
        }
        else
        {
            _fetcher.StartTimer();
            // Kick off initial fetch immediately
            await _fetcher.RefreshAsync();
            
            // Unhide it on start so the user knows it opened!
            await Task.Delay(500);
            popup.Show();
            popup.PositionWindow();
            popup.Activate();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _fetcher?.StopTimer();
        _fetcher?.Dispose();
        _trayManager?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();

        base.OnExit(e);
    }
}
