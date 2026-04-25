using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using ClaudeUsage.Core.Models;

namespace ClaudeUsage.Core.Services;

public class ClaudeApiService
{
    private readonly HttpClient _http;
    private readonly BrowserCookieReader _cookieReader = new();
    private const string DefaultUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/135.0.0.0 Safari/537.36";

    // Cache browser cookie context so we don't scan all profiles on every API call
    private BrowserCookieContext? _cachedContext;
    private string? _cachedSessionKey;
    private DateTime _cacheExpiry = DateTime.MinValue;
    private readonly SemaphoreSlim _scanLock = new(1, 1);

    public ClaudeApiService()
    {
        var handler = new HttpClientHandler
        {
            UseCookies = false,          // We inject cookies manually
            AllowAutoRedirect = true,
        };

        _http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30),
        };

        // Static request headers required by Claude API
        _http.DefaultRequestHeaders.Clear();
        _http.DefaultRequestHeaders.Accept.ParseAdd("*/*");
        _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        _http.DefaultRequestHeaders.Add("anthropic-client-platform", "web_claude_ai");
        _http.DefaultRequestHeaders.Add("anthropic-client-version", "1.0.0");
        _http.DefaultRequestHeaders.Add("origin", "https://claude.ai");
        _http.DefaultRequestHeaders.Add("referer", "https://claude.ai/settings/usage");
        _http.DefaultRequestHeaders.Add("sec-fetch-dest", "empty");
        _http.DefaultRequestHeaders.Add("sec-fetch-mode", "cors");
        _http.DefaultRequestHeaders.Add("sec-fetch-site", "same-origin");
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(DefaultUserAgent);
    }

    /// <summary>
    /// Fetches the user's organization UUID using the session key alone.
    /// Returns null if the request fails or the account has no org.
    /// </summary>
    public async Task<string?> FetchOrganizationIdAsync(string sessionKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sessionKey)) return null;

        try
        {
            var (cookieHeader, _) = await BuildCookieHeaderAsync(sessionKey);
            using var request = new HttpRequestMessage(HttpMethod.Get,
                "https://claude.ai/api/auth/current_account");
            request.Headers.Add("Cookie", cookieHeader);

            var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;

            var body = await response.Content.ReadAsStringAsync(ct);
            // Body can be HTML (Cloudflare) — bail
            if (body.TrimStart().StartsWith('<')) return null;

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            // Try memberships[0].organization.uuid first
            if (root.TryGetProperty("memberships", out var memberships) &&
                memberships.ValueKind == JsonValueKind.Array &&
                memberships.GetArrayLength() > 0)
            {
                var first = memberships[0];
                if (first.TryGetProperty("organization", out var org) &&
                    org.TryGetProperty("uuid", out var uuid))
                {
                    var val = uuid.GetString();
                    if (!string.IsNullOrWhiteSpace(val)) return val;
                }
            }

            // Fallback: account.organization_uuid
            if (root.TryGetProperty("account", out var account) &&
                account.TryGetProperty("organization_uuid", out var orgUuid))
            {
                var val = orgUuid.GetString();
                if (!string.IsNullOrWhiteSpace(val)) return val;
            }

            return null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Fetches usage data from Claude API and returns parsed limit rows.
    /// Throws <see cref="ClaudeApiException"/> for known error cases.
    /// </summary>
    public async Task<List<LimitData>> FetchUsageAsync(string organizationId, string sessionKey,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(organizationId))
            throw new ClaudeApiException("Organization ID is not configured.");
        if (string.IsNullOrWhiteSpace(sessionKey))
            throw new ClaudeApiException("Session key is not configured.");

        var url = $"https://claude.ai/api/organizations/{organizationId}/usage";
        var (cookieHeader, hasLocalSessionContext) = await BuildCookieHeaderAsync(sessionKey);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", cookieHeader);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex) when (ex.InnerException is System.Net.Sockets.SocketException)
        {
            throw new ClaudeApiException("No internet connection.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ClaudeApiException("Request timed out.");
        }

        // Read body first so we can inspect it regardless of status code
        var body = await response.Content.ReadAsStringAsync(ct);

        // Cloudflare block check
        if (body.TrimStart().StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) ||
            body.TrimStart().StartsWith("<html", StringComparison.OrdinalIgnoreCase))
        {
            var msg = hasLocalSessionContext
                ? "Blocked by Cloudflare challenge. Open claude.ai in your browser, then click Refresh."
                : "Blocked by Cloudflare challenge. Your saved session key does not match an active local Claude session. Open claude.ai, sign in, update the session key, then click Refresh.";
            throw new ClaudeApiException(msg);
        }

        switch (response.StatusCode)
        {
            case HttpStatusCode.Unauthorized:
            case HttpStatusCode.Forbidden:
                throw new ClaudeApiException("Session expired. Please update your session key.");
            case HttpStatusCode.TooManyRequests:
                throw new ClaudeApiException("Rate limited. Try again in a minute.");
        }

        if (!response.IsSuccessStatusCode)
            throw new ClaudeApiException($"HTTP {(int)response.StatusCode}");

        // Parse JSON
        UsageResponse usageResponse;
        try
        {
            usageResponse = JsonSerializer.Deserialize<UsageResponse>(body)
                            ?? throw new ClaudeApiException("Empty response from server.");
        }
        catch (JsonException)
        {
            throw new ClaudeApiException("Invalid response format from server.");
        }

        return BuildLimitList(usageResponse);
    }

    private async Task<(string header, bool hasContext)> BuildCookieHeaderAsync(string sessionKey)
    {
        await _scanLock.WaitAsync();
        try
        {
            if (_cachedSessionKey != sessionKey || DateTime.UtcNow > _cacheExpiry)
            {
                _cachedContext = await Task.Run(() =>
                    _cookieReader.FindContextBySessionKey(sessionKey));
                _cachedSessionKey = sessionKey;
                _cacheExpiry = DateTime.UtcNow.AddMinutes(5);
            }
        }
        finally
        {
            _scanLock.Release();
        }

        var context = _cachedContext;
        if (context == null || string.IsNullOrWhiteSpace(context.CookieHeader))
            return ($"sessionKey={sessionKey}", false);

        var cookies = new Dictionary<string, string>(context.Cookies, StringComparer.Ordinal)
        {
            ["sessionKey"] = sessionKey,
        };

        var header = string.Join("; ", cookies
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => $"{pair.Key}={pair.Value}"));

        return (header, true);
    }

    private static List<LimitData> BuildLimitList(UsageResponse r)
    {
        var limits = new List<LimitData>();

        if (r.FiveHour != null)
            limits.Add(Map("Current session", r.FiveHour));

        if (r.SevenDay != null)
            limits.Add(Map("Current week (all models)", r.SevenDay));

        if (r.SevenDaySonnet != null)
            limits.Add(Map("Current week (Sonnet)", r.SevenDaySonnet));

        return limits;
    }

    private static LimitData Map(string label, UsagePeriod period)
    {
        DateTime? resetsAt = null;
        if (!string.IsNullOrEmpty(period.ResetsAt) &&
            DateTime.TryParse(period.ResetsAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
        {
            resetsAt = dt.ToLocalTime();
        }

        return new LimitData
        {
            Label = label,
            Percentage = period.Utilization,
            ResetsAt = resetsAt,
        };
    }
}

public class ClaudeApiException : Exception
{
    public ClaudeApiException(string message) : base(message) { }
}
