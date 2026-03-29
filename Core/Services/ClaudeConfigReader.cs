using System.IO;
using System.Text.Json;

namespace ClaudeUsage.Core.Services;

public class ClaudeConfigReader
{
    private static readonly string ConfigPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude.json");

    /// <summary>
    /// Tries to read the organization UUID from ~/.claude.json.
    /// Returns null if file doesn't exist or key is missing.
    /// </summary>
    public string? TryReadOrganizationId()
    {
        try
        {
            if (!File.Exists(ConfigPath))
                return null;

            var json = File.ReadAllText(ConfigPath);
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
}
