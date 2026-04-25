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

/// <summary>Claude cookie context for a browser profile.</summary>
public class BrowserCookieContext
{
    public string BrowserName { get; init; } = string.Empty;
    public string ProfileName { get; init; } = string.Empty;
    public Dictionary<string, string> Cookies { get; init; } = [];

    public string? SessionKey =>
        Cookies.TryGetValue("sessionKey", out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    public string CookieHeader =>
        string.Join("; ", Cookies
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => $"{pair.Key}={pair.Value}"));
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
        ("Claude Desktop", Path.Combine(RoamingApp, "Claude")),
    ];

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Scans all supported browsers and returns every Claude session found.
    /// Never throws — failures are silently skipped.
    /// </summary>
    public List<DiscoveredSession> ScanAllBrowsers()
    {
        var results = new List<DiscoveredSession>();

        foreach (var context in ScanAllBrowserContexts())
        {
            if (!string.IsNullOrWhiteSpace(context.SessionKey))
            {
                results.Add(new DiscoveredSession
                {
                    BrowserName = context.BrowserName,
                    ProfileName = context.ProfileName,
                    SessionKey = context.SessionKey,
                });
            }
        }

        return results;
    }

    /// <summary>
    /// Returns the first browser profile whose Claude cookies match the supplied session key.
    /// Never throws.
    /// </summary>
    public BrowserCookieContext? FindContextBySessionKey(string sessionKey)
    {
        if (string.IsNullOrWhiteSpace(sessionKey)) return null;

        try
        {
            return ScanAllBrowserContexts()
                .FirstOrDefault(ctx => string.Equals(ctx.SessionKey, sessionKey, StringComparison.Ordinal));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Scans all supported browsers and returns every Claude cookie context found.
    /// Never throws.
    /// </summary>
    public List<BrowserCookieContext> ScanAllBrowserContexts()
    {
        var results = new List<BrowserCookieContext>();

        foreach (var (name, userDataPath) in GetChromiumBrowsers())
            results.AddRange(ScanChromiumBrowser(name, userDataPath));

        results.AddRange(ScanFirefox());

        return results;
    }

    // ── Chromium (Chrome / Edge / Brave / …) ─────────────────────────────────

    private static List<BrowserCookieContext> ScanChromiumBrowser(string browserName, string userDataPath)
    {
        var contexts = new List<BrowserCookieContext>();
        if (!Directory.Exists(userDataPath)) return contexts;

        byte[]? masterKey = GetChromiumMasterKey(Path.Combine(userDataPath, "Local State"));
        if (masterKey == null) return contexts;

        // Scan every profile folder (Default, Profile 1, Profile 2, …)
        var profileDirs = Directory.EnumerateDirectories(userDataPath)
            .Where(d =>
            {
                var name = Path.GetFileName(d);
                return name == "Default" || name.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        if (HasCookieDatabase(userDataPath))
            profileDirs.Add(userDataPath);

        foreach (var profileDir in profileDirs)
        {
            var isRootProfile = string.Equals(profileDir, userDataPath, StringComparison.OrdinalIgnoreCase);
            var profileName = isRootProfile ? "Default" : Path.GetFileName(profileDir);

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

            var cookies = ReadChromiumCookies(cookieDb, masterKey);
            if (cookies.Count > 0)
                contexts.Add(new BrowserCookieContext
                {
                    BrowserName = browserName,
                    ProfileName = profileName,
                    Cookies = cookies,
                });
        }

        return contexts;
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

    /// <summary>Reads and decrypts Claude cookies from a Chromium Cookies SQLite file.</summary>
    private static Dictionary<string, string> ReadChromiumCookies(string cookiePath, byte[] masterKey)
    {
        var cookies = new Dictionary<string, string>(StringComparer.Ordinal);
        var snapshotPath = TryCreateSqliteSnapshot(cookiePath);
        if (string.IsNullOrWhiteSpace(snapshotPath)) return cookies;

        try
        {
            using var conn = new SqliteConnection($"Data Source={snapshotPath};Mode=ReadOnly;");
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT name, value, encrypted_value
                FROM   cookies
                WHERE  host_key = 'claude.ai'
                   OR  host_key LIKE '%.claude.ai'
                """;

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var name = reader.GetString(0);
                if (string.IsNullOrWhiteSpace(name)) continue;

                string? value = null;

                if (!reader.IsDBNull(1))
                {
                    value = reader.GetString(1);
                }

                if (string.IsNullOrWhiteSpace(value) && !reader.IsDBNull(2))
                {
                    var encValue = (byte[])reader.GetValue(2);
                    value = DecryptChromiumCookieValue(encValue, masterKey);
                }

                if (!string.IsNullOrWhiteSpace(value))
                    cookies[name] = value;
            }
        }
        catch { }
        finally
        {
            CleanupSqliteSnapshot(snapshotPath);
        }

        return cookies;
    }

    private static bool HasCookieDatabase(string profileDir)
    {
        var networkCookies = Path.Combine(profileDir, "Network", "Cookies");
        var rootCookies = Path.Combine(profileDir, "Cookies");
        return File.Exists(networkCookies) || File.Exists(rootCookies);
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

    private List<BrowserCookieContext> ScanFirefox()
    {
        var contexts  = new List<BrowserCookieContext>();
        var ffProfiles = Path.Combine(RoamingApp, @"Mozilla\Firefox\Profiles");
        if (!Directory.Exists(ffProfiles)) return contexts;

        foreach (var profileDir in Directory.EnumerateDirectories(ffProfiles))
        {
            var cookiePath = Path.Combine(profileDir, "cookies.sqlite");
            if (!File.Exists(cookiePath)) continue;

            var profileName = Path.GetFileName(profileDir);
            var snapshotPath = TryCreateSqliteSnapshot(cookiePath);
            if (string.IsNullOrWhiteSpace(snapshotPath)) continue;

            try
            {
                using var conn = new SqliteConnection($"Data Source={snapshotPath};Mode=ReadOnly;");
                conn.Open();

                using var cmd = conn.CreateCommand();
                // Firefox stores cookie values in plaintext in the `value` column
                cmd.CommandText = """
                    SELECT name, value
                    FROM   moz_cookies
                    WHERE  host = 'claude.ai'
                       OR  host LIKE '%.claude.ai'
                    """;

                using var reader = cmd.ExecuteReader();
                var cookies = new Dictionary<string, string>(StringComparer.Ordinal);
                while (reader.Read())
                {
                    var name = reader.GetString(0);
                    var value = reader.GetString(1);
                    if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(value))
                        cookies[name] = value;
                }

                if (cookies.Count > 0)
                    contexts.Add(new BrowserCookieContext
                    {
                        BrowserName = "Firefox",
                        ProfileName = profileName,
                        Cookies = cookies,
                    });
            }
            catch { }
            finally
            {
                CleanupSqliteSnapshot(snapshotPath);
            }
        }

        return contexts;
    }

    private static string? TryCreateSqliteSnapshot(string sourcePath)
    {
        var snapshotDir = Path.Combine(Path.GetTempPath(), $"claudeusage-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(snapshotDir);

            var snapshotPath = Path.Combine(snapshotDir, Path.GetFileName(sourcePath));
            CopyFileShared(sourcePath, snapshotPath);

            CopyFileSharedIfExists($"{sourcePath}-wal", $"{snapshotPath}-wal");
            CopyFileSharedIfExists($"{sourcePath}-shm", $"{snapshotPath}-shm");

            return snapshotPath;
        }
        catch
        {
            try { Directory.Delete(snapshotDir, recursive: true); } catch { }
            return null;
        }
    }

    private static void CleanupSqliteSnapshot(string? snapshotPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(snapshotPath);
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch { }
    }

    private static void CopyFileSharedIfExists(string sourcePath, string destinationPath)
    {
        if (File.Exists(sourcePath))
            CopyFileShared(sourcePath, destinationPath);
    }

    private static void CopyFileShared(string sourcePath, string destinationPath)
    {
        using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        input.CopyTo(output);
    }
}
