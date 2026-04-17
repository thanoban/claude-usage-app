using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace ClaudeUsage.Core.Services;

/// <summary>One discovered Claude session from a browser profile.</summary>
public class DiscoveredSession
{
    public string BrowserName { get; init; } = string.Empty;
    public string ProfileName { get; init; } = string.Empty;
    public string SessionKey  { get; init; } = string.Empty;

    public override string ToString() => $"{BrowserName}  ·  {ProfileName}";
}

/// <summary>
/// Reads Claude's sessionKey cookie from Chrome, Edge, Brave, and Firefox
/// without any user interaction, using Windows DPAPI + AES-GCM decryption.
/// </summary>
public class BrowserCookieReader
{
    // Paths are computed at runtime (not static initializers) to avoid TypeInitializerException
    private static string LocalApp   => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static string RoamingApp => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    private static (string Name, string UserDataPath)[] GetChromiumBrowsers() =>
    [
        ("Chrome",   Path.Combine(LocalApp, @"Google\Chrome\User Data")),
        ("Edge",     Path.Combine(LocalApp, @"Microsoft\Edge\User Data")),
        ("Brave",    Path.Combine(LocalApp, @"BraveSoftware\Brave-Browser\User Data")),
        ("Chromium", Path.Combine(LocalApp, @"Chromium\User Data")),
        ("Vivaldi",  Path.Combine(LocalApp, @"Vivaldi\User Data")),
        ("Opera",    Path.Combine(RoamingApp, @"Opera Software\Opera Stable")),
    ];

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Scans all supported browsers and returns every Claude session found.
    /// Never throws — failures are silently skipped.
    /// </summary>
    public List<DiscoveredSession> ScanAllBrowsers()
    {
        var results = new List<DiscoveredSession>();

        foreach (var (name, userDataPath) in GetChromiumBrowsers())
            results.AddRange(ScanChromiumBrowser(name, userDataPath));

        results.AddRange(ScanFirefox());

        return results;
    }

    // ── Chromium (Chrome / Edge / Brave / …) ─────────────────────────────────

    private static List<DiscoveredSession> ScanChromiumBrowser(string browserName, string userDataPath)
    {
        var sessions = new List<DiscoveredSession>();
        if (!Directory.Exists(userDataPath)) return sessions;

        byte[]? masterKey = GetChromiumMasterKey(Path.Combine(userDataPath, "Local State"));
        if (masterKey == null) return sessions;

        // Scan every profile folder (Default, Profile 1, Profile 2, …)
        var profileDirs = Directory.EnumerateDirectories(userDataPath)
            .Where(d =>
            {
                var name = Path.GetFileName(d);
                return name == "Default" || name.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase);
            });

        foreach (var profileDir in profileDirs)
        {
            var profileName = Path.GetFileName(profileDir);

            // Try to read the human-friendly profile name from Preferences JSON
            try
            {
                var prefFile = Path.Combine(profileDir, "Preferences");
                if (File.Exists(prefFile))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(prefFile));
                    if (doc.RootElement.TryGetProperty("profile", out var prof) &&
                        prof.TryGetProperty("name", out var nameEl))
                    {
                        var friendly = nameEl.GetString();
                        if (!string.IsNullOrWhiteSpace(friendly))
                            profileName = friendly;
                    }
                }
            }
            catch { /* use folder name */ }

            // Cookies file exists in Default\Network\Cookies (Chrome 96+) or Default\Cookies
            var cookieDb = Path.Combine(profileDir, "Network", "Cookies");
            if (!File.Exists(cookieDb))
                cookieDb = Path.Combine(profileDir, "Cookies");
            if (!File.Exists(cookieDb)) continue;

            var key = ReadChromiumCookie(cookieDb, masterKey);
            if (!string.IsNullOrWhiteSpace(key))
                sessions.Add(new DiscoveredSession
                {
                    BrowserName = browserName,
                    ProfileName = profileName,
                    SessionKey  = key,
                });
        }

        return sessions;
    }

    /// <summary>Decrypts the Chromium master AES key from Local State using DPAPI.</summary>
    private static byte[]? GetChromiumMasterKey(string localStatePath)
    {
        try
        {
            if (!File.Exists(localStatePath)) return null;

            using var doc = JsonDocument.Parse(File.ReadAllText(localStatePath));
            if (!doc.RootElement.TryGetProperty("os_crypt", out var osCrypt)) return null;
            if (!osCrypt.TryGetProperty("encrypted_key", out var encKeyEl)) return null;

            var encKeyB64 = encKeyEl.GetString();
            if (string.IsNullOrEmpty(encKeyB64)) return null;

            var encKeyBytes = Convert.FromBase64String(encKeyB64);

            // Strip the "DPAPI" prefix (5 bytes)
            if (encKeyBytes.Length < 5) return null;
            var encKeyNoDpapi = encKeyBytes[5..];

            return ProtectedData.Unprotect(encKeyNoDpapi, null, DataProtectionScope.CurrentUser);
        }
        catch { return null; }
    }

    /// <summary>Reads and decrypts the Claude sessionKey cookie from a Chromium Cookies SQLite file.</summary>
    private static string? ReadChromiumCookie(string cookiePath, byte[] masterKey)
    {
        // Copy to temp because the browser may have the file locked
        var temp = Path.GetTempFileName();
        try
        {
            File.Copy(cookiePath, temp, overwrite: true);

            using var conn = new SqliteConnection($"Data Source={temp};Mode=ReadOnly;");
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT encrypted_value
                FROM   cookies
                WHERE  host_key LIKE '%.claude.ai'
                  AND  name = 'sessionKey'
                LIMIT  1
                """;

            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return null;

            var encValue = (byte[])reader.GetValue(0);
            return DecryptChromiumCookieValue(encValue, masterKey);
        }
        catch { return null; }
        finally
        {
            try { File.Delete(temp); } catch { }
        }
    }

    /// <summary>
    /// Decrypts a Chromium cookie value.
    /// v10/v11 prefix → AES-256-GCM with master key.
    /// No prefix → legacy DPAPI blob.
    /// </summary>
    private static string? DecryptChromiumCookieValue(byte[] encValue, byte[] masterKey)
    {
        try
        {
            if (encValue.Length > 3 &&
                encValue[0] == 'v' &&
                (encValue[1] == '1' && (encValue[2] == '0' || encValue[2] == '1')))
            {
                // AES-256-GCM: [3 bytes version][12 bytes IV][ciphertext][16 bytes tag]
                const int ivLen  = 12;
                const int tagLen = 16;

                if (encValue.Length < 3 + ivLen + tagLen) return null;

                var iv         = encValue[3..(3 + ivLen)];
                var tag        = encValue[^tagLen..];
                var ciphertext = encValue[(3 + ivLen)..^tagLen];

                using var aes = new AesGcm(masterKey, tagLen);
                var plaintext = new byte[ciphertext.Length];
                aes.Decrypt(iv, ciphertext, tag, plaintext);
                return Encoding.UTF8.GetString(plaintext);
            }
            else
            {
                // Legacy DPAPI-encrypted cookie (old Chrome / no master key)
                var plain = ProtectedData.Unprotect(encValue, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
        }
        catch { return null; }
    }

    // ── Firefox ───────────────────────────────────────────────────────────────

    private List<DiscoveredSession> ScanFirefox()
    {
        var sessions  = new List<DiscoveredSession>();
        var ffProfiles = Path.Combine(RoamingApp, @"Mozilla\Firefox\Profiles");
        if (!Directory.Exists(ffProfiles)) return sessions;

        foreach (var profileDir in Directory.EnumerateDirectories(ffProfiles))
        {
            var cookiePath = Path.Combine(profileDir, "cookies.sqlite");
            if (!File.Exists(cookiePath)) continue;

            var profileName = Path.GetFileName(profileDir);
            var temp = Path.GetTempFileName();
            try
            {
                File.Copy(cookiePath, temp, overwrite: true);

                using var conn = new SqliteConnection($"Data Source={temp};Mode=ReadOnly;");
                conn.Open();

                using var cmd = conn.CreateCommand();
                // Firefox stores cookie values in plaintext in the `value` column
                cmd.CommandText = """
                    SELECT value
                    FROM   moz_cookies
                    WHERE  host LIKE '%.claude.ai'
                      AND  name = 'sessionKey'
                    LIMIT  1
                    """;

                using var reader = cmd.ExecuteReader();
                if (!reader.Read()) continue;

                var val = reader.GetString(0);
                if (!string.IsNullOrWhiteSpace(val))
                    sessions.Add(new DiscoveredSession
                    {
                        BrowserName = "Firefox",
                        ProfileName = profileName,
                        SessionKey  = val,
                    });
            }
            catch { }
            finally
            {
                try { File.Delete(temp); } catch { }
            }
        }

        return sessions;
    }
}
