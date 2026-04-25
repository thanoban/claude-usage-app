using System.IO;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace ClaudeUsage.Core.Services;

public class ClaudeConfigReader
{
    private static readonly string CliConfigPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude.json");
    private static readonly string DesktopConfigPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude", "config.json");
    private static readonly string DesktopBlocklistPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude", "extensions-blocklist.json");
    private static readonly Regex OrgIdRegex =
        new(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
            RegexOptions.Compiled);

    /// <summary>
    /// Tries to read the organization UUID from local Claude config files.
    /// Returns null if no supported source contains one.
    /// </summary>
    public string? TryReadOrganizationId()
    {
        return TryReadCliOrganizationId()
            ?? TryReadDesktopOrganizationIdFromBlocklist()
            ?? TryReadDesktopOrganizationIdFromConfig();
    }

    private static string? TryReadCliOrganizationId()
    {
        try
        {
            if (!File.Exists(CliConfigPath))
                return null;

            var json = File.ReadAllText(CliConfigPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("oauthAccount", out var oauthAccount) &&
                oauthAccount.TryGetProperty("organizationUuid", out var orgUuid))
            {
                var value = orgUuid.GetString();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryReadDesktopOrganizationIdFromBlocklist()
    {
        try
        {
            if (!File.Exists(DesktopBlocklistPath))
                return null;

            var json = File.ReadAllText(DesktopBlocklistPath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return null;

            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                if (!entry.TryGetProperty("url", out var urlEl)) continue;
                var url = urlEl.GetString();
                if (string.IsNullOrWhiteSpace(url)) continue;

                var match = OrgIdRegex.Match(url);
                if (match.Success) return match.Value;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static string? TryReadDesktopOrganizationIdFromConfig()
    {
        try
        {
            if (!File.Exists(DesktopConfigPath))
                return null;

            var json = File.ReadAllText(DesktopConfigPath);
            var match = OrgIdRegex.Match(json);
            return match.Success ? match.Value : null;
        }
        catch
        {
            return null;
        }
    }
}
