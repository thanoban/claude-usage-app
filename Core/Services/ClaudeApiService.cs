using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using ClaudeUsage.Core.Models;

namespace ClaudeUsage.Core.Services;

public class ClaudeApiService
{
    private readonly HttpClient _http;

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
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
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

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("content-type", "application/json");
        request.Headers.Add("Cookie", $"sessionKey={sessionKey}");

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
            throw new ClaudeApiException("Blocked by Cloudflare. Try updating session key.");
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
