using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeUsage.Core.Services;

public class Credentials
{
    [JsonPropertyName("sessionKey")]
    public string SessionKey { get; set; } = string.Empty;

    [JsonPropertyName("organizationId")]
    public string OrganizationId { get; set; } = string.Empty;
}

public class CredentialsStore
{
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClaudeUsage");

    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    private static readonly JsonSerializerOptions _opts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public Credentials Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new Credentials();

            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<Credentials>(json, _opts) ?? new Credentials();
        }
        catch
        {
            return new Credentials();
        }
    }

    public void Save(Credentials creds)
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(creds, _opts);
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Silently ignore write errors
        }
    }

    public bool IsConfigured(Credentials creds) =>
        !string.IsNullOrWhiteSpace(creds.SessionKey) &&
        !string.IsNullOrWhiteSpace(creds.OrganizationId);
}
