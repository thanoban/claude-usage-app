using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using ClaudeUsage.Core.Models;
using ClaudeUsage.Core.Services;

namespace ClaudeUsage.Core;

public class UsageFetcher : INotifyPropertyChanged, IDisposable
{
    private readonly ClaudeApiService _api;
    private readonly CredentialsStore _store;
    private readonly DispatcherTimer _timer;
    private CancellationTokenSource? _cts;

    private UsageInfo _usage = new() { IsLoading = false };
    public UsageInfo Usage
    {
        get => _usage;
        private set { _usage = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public UsageFetcher(ClaudeApiService api, CredentialsStore store)
    {
        _api = api;
        _store = store;

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(60),
        };
        _timer.Tick += async (_, _) => await FetchAsync();
    }

    public void StartTimer()
    {
        _timer.Start();
    }

    public void StopTimer()
    {
        _timer.Stop();
    }

    /// <summary>
    /// Triggers an immediate fetch and resets the 60-second timer.
    /// </summary>
    public async Task RefreshAsync()
    {
        _timer.Stop();
        await FetchAsync();
        _timer.Start();
    }

    private async Task FetchAsync()
    {
        // Cancel any in-flight fetch
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        var creds = _store.Load();
        if (!_store.IsConfigured(creds))
        {
            Usage = new UsageInfo
            {
                Error = "Not configured. Please enter your session key.",
                IsLoading = false,
            };
            return;
        }

        // Set loading state while preserving any previous data
        Usage = new UsageInfo
        {
            Limits = _usage.Limits,
            LastUpdated = _usage.LastUpdated,
            IsLoading = true,
        };

        try
        {
            var limits = await _api.FetchUsageAsync(creds.OrganizationId, creds.SessionKey, ct);

            if (ct.IsCancellationRequested) return;

            Usage = new UsageInfo
            {
                Limits = limits,
                LastUpdated = DateTime.Now,
                IsLoading = false,
            };
        }
        catch (ClaudeApiException ex)
        {
            if (ct.IsCancellationRequested) return;

            Usage = new UsageInfo
            {
                Limits = _usage.Limits, // Keep old data visible
                LastUpdated = _usage.LastUpdated,
                Error = ex.Message,
                IsLoading = false,
            };
        }
        catch (OperationCanceledException)
        {
            // Silently ignore – superseded by newer request
        }
        catch (Exception ex)
        {
            if (ct.IsCancellationRequested) return;

            Usage = new UsageInfo
            {
                Limits = _usage.Limits,
                LastUpdated = _usage.LastUpdated,
                Error = $"Unexpected error: {ex.Message}",
                IsLoading = false,
            };
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _cts?.Cancel();
        _cts?.Dispose();
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
