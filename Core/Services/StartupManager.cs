using Microsoft.Win32;

namespace ClaudeUsage.Core.Services;

/// <summary>
/// Manages the Windows "run at login" registry entry for this app.
/// Writes to HKCU\Software\Microsoft\Windows\CurrentVersion\Run — no elevation needed.
/// </summary>
public static class StartupManager
{
    private const string AppName = "ClaudeUsage";
    private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
            return key?.GetValue(AppName) != null;
        }
        catch { return false; }
    }

    public static void Enable()
    {
        try
        {
            var exePath = Environment.ProcessPath
                ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(exePath)) return;

            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, writable: true);
            key?.SetValue(AppName, $"\"{exePath}\"");
        }
        catch { }
    }

    public static void Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, writable: true);
            key?.DeleteValue(AppName, throwOnMissingValue: false);
        }
        catch { }
    }

    public static void Toggle()
    {
        if (IsEnabled()) Disable(); else Enable();
    }
}
