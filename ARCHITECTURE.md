# Architecture Deep Dive

This document explains **every technical decision** in this project — not just what the code does, but **why** it was built this way. It is written for someone learning WPF and Windows desktop development.

---

## 1. What Is WPF?

**WPF (Windows Presentation Foundation)** is Microsoft's framework for building graphical Windows desktop applications using .NET. It was introduced in 2006 and remains the most capable native Windows UI framework for .NET.

### How WPF Differs From Other Approaches

| Framework | Rendering | Styling | Transparency | Best For |
|-----------|-----------|---------|--------------|----------|
| **WPF** | DirectX GPU | XAML (rich) | Yes | Rich Windows apps |
| WinForms | GDI+ (old) | Code-only | No | Simple tools |
| .NET MAUI | Platform-native | XAML | Partial | Cross-platform |
| Blazor Hybrid | Web/HTML | CSS | Yes | Web devs |

**This project uses WPF because:**
- Transparent, rounded-corner windows (needed for the popup card look)
- XAML lets you style buttons and controls with templates without writing Win32 code
- Full access to Windows APIs (DPAPI for cookie decryption)
- The `Hardcodet.Wpf.TaskbarNotification` library only targets WPF

### XAML — The Markup Language

WPF uses **XAML** (eXtensible Application Markup Language) to describe UI:
```xml
<!-- This XAML declares a button with orange background and rounded corners -->
<Button Content="Save &amp; Connect" Background="#E8441A">
    <Button.Template>
        <ControlTemplate TargetType="Button">
            <Border Background="{TemplateBinding Background}" CornerRadius="9">
                <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
            </Border>
        </ControlTemplate>
    </Button.Template>
</Button>
```

Every XAML file has a **code-behind** `.cs` file of the same name that handles events and logic. The `x:Class` attribute in XAML links them:
```xml
<Window x:Class="ClaudeUsage.UI.PopupWindow" ...>
```
```csharp
public partial class PopupWindow : Window { ... } // "partial" = split across XAML + .cs
```

---

## 2. Project Structure — Every File Explained

### Root Files

**`App.xaml` + `App.xaml.cs`**  
The application's entry point. `App.xaml` declares the `Application` class and sets global properties (font, shutdown mode). `App.xaml.cs` is where `OnStartup` runs when the exe launches — it creates all services and windows.

`ShutdownMode="OnExplicitShutdown"` means the app keeps running even when all windows are closed (it lives in the system tray).

**`ClaudeUsage.csproj`**  
The MSBuild project file. Key settings:
```xml
<TargetFramework>net8.0-windows</TargetFramework>  <!-- Windows-only, .NET 8 -->
<OutputType>WinExe</OutputType>                    <!-- No console window on launch -->
<UseWPF>true</UseWPF>                              <!-- Enable WPF -->
<UseWindowsForms>true</UseWindowsForms>            <!-- Enable WinForms (for Screen.PrimaryScreen) -->
```
WinForms is enabled *only* to use `System.Windows.Forms.Screen.PrimaryScreen` for taskbar position detection. WPF doesn't have a built-in equivalent.

**`app.manifest`**  
Tells Windows how to run the app:
- DPI awareness: renders crisply on high-DPI displays (4K monitors)
- UAC: runs as a normal user (no admin elevation needed)

---

### `Core/Models/` — Data Shapes

These are simple C# classes that hold data. They have no logic — they're just structured containers.

**`LimitData.cs`**
```csharp
public class LimitData
{
    public string Label { get; set; }       // "Current session"
    public double Percentage { get; set; }  // 0.0 to 100.0
    public DateTime? ResetsAt { get; set; } // When this limit resets
}
```
One instance = one row in the popup. Three instances cover the three limits.

**`UsageInfo.cs`**
```csharp
public class UsageInfo
{
    public List<LimitData> Limits { get; set; }  // the three rows
    public DateTime LastUpdated { get; set; }    // for "X minutes ago" footer
    public string? Error { get; set; }           // non-null = show error panel
    public bool IsLoading { get; set; }          // show spinner
}
```
This is the **single object the UI observes**. When `UsageFetcher` sets a new `UsageInfo`, `PopupWindow` re-renders.

**`UsageResponse.cs`**
```csharp
public class UsageResponse
{
    [JsonPropertyName("five_hour")]
    public UsagePeriod? FiveHour { get; set; }

    [JsonPropertyName("seven_day")]
    public UsagePeriod? SevenDay { get; set; }

    [JsonPropertyName("seven_day_sonnet")]
    public UsagePeriod? SevenDaySonnet { get; set; }
}
```
`[JsonPropertyName]` maps the snake_case JSON field names from the API to PascalCase C# properties. `System.Text.Json` uses these attributes during deserialization.

---

### `Core/Services/` — Business Logic

**`CredentialsStore.cs`**  
Reads and writes `%APPDATA%\ClaudeUsage\settings.json`. This file stores:
```json
{
  "sessionKey": "sk-ant-...",
  "organizationId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
}
```
`IsConfigured()` returns `true` only when both fields are non-empty — this determines whether to show the Setup window or go straight to the popup.

**`ClaudeConfigReader.cs`**  
Tries to auto-detect the org ID from three sources in order:
1. `~/.claude.json` — Claude CLI config file (if Claude Code is installed)
2. `%APPDATA%\Claude\extensions-blocklist.json` — Claude Desktop app blocklist (contains org UUID in URLs)
3. `%APPDATA%\Claude\config.json` — Claude Desktop app config

Uses a UUID regex `[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-...` to extract the org ID from whichever file contains it. This saves the user from having to find and copy their org ID manually.

**`BrowserCookieReader.cs`**  
The most complex service. It reads encrypted cookies from browsers. Full explanation in Section 7.

Key classes:
```csharp
public class DiscoveredSession   // One found login: browser name + profile + sessionKey
public class BrowserCookieContext // Full cookie set from one browser profile
```

Key methods:
- `ScanAllBrowsers()` → returns `List<DiscoveredSession>` (used by SetupWindow to show picker)
- `ScanAllBrowserContexts()` → returns `List<BrowserCookieContext>` (used for Cloudflare bypass)
- `FindContextBySessionKey(key)` → finds the browser profile that matches a known session key

**`ClaudeApiService.cs`**  
Makes HTTP requests to claude.ai. Key responsibilities:
1. Builds the right cookie header (either full browser cookies or fallback to just `sessionKey=...`)
2. `FetchOrganizationIdAsync(sessionKey)` — calls `/api/auth/current_account` to auto-detect org ID
3. `FetchUsageAsync(orgId, sessionKey)` — calls `/api/organizations/{id}/usage`
4. Detects Cloudflare HTML responses and throws descriptive errors
5. Caches the browser cookie context for 5 minutes (see Issue 2 fix)

**`UsageFetcher.cs`**  
The bridge between the API and the UI. It:
- Owns a `DispatcherTimer` set to 60 seconds
- On each tick, calls `ClaudeApiService.FetchUsageAsync()`
- Stores the result in a `UsageInfo` property
- Implements `INotifyPropertyChanged` — whenever `Usage` changes, WPF's data binding sees it

```csharp
public class UsageFetcher : INotifyPropertyChanged
{
    public UsageInfo Usage
    {
        get => _usage;
        private set { _usage = value; OnPropertyChanged(); } // fires PropertyChanged event
    }
}
```

---

### `Core/UsageFetcher.cs` — INotifyPropertyChanged Pattern

This is a core WPF pattern for **reactive UI updates**.

```csharp
// When the property setter fires OnPropertyChanged("Usage"),
// WPF's binding engine automatically calls RenderUsage() in PopupWindow:
_fetcher.PropertyChanged += (_, args) =>
{
    if (args.PropertyName == nameof(UsageFetcher.Usage))
        Dispatcher.Invoke(RenderUsage);
};
```

`Dispatcher.Invoke` is needed because `PropertyChanged` fires on the timer thread, but WPF controls can only be updated from the UI thread. `Dispatcher.Invoke` marshals the call back to the UI thread.

---

### `UI/` — The User Interface

**`TrayManager.cs`**  
Creates the system tray icon using `Hardcodet.Wpf.TaskbarNotification.TaskbarIcon`. This NuGet library wraps the Windows Shell tray API. Key behaviors:
- Left-click → toggle popup visibility
- Right-click → context menu (Open / Quit)
- Icon loaded from `pack://application:,,,/Resources/icon.ico` (a WPF pack URI)

**`PopupWindow.xaml` + `.cs`**  
The main usage popup. Notable WPF techniques used:

*SizeToContent:*  
```xml
<Window SizeToContent="Height" Width="310" ...>
```
The window width is fixed; the height grows to fit its content automatically.

*WindowStyle="None" + AllowsTransparency="True":*  
Removes the standard Windows title bar, allowing a fully custom-shaped window with rounded corners and a dark background.

*Spin animation (Storyboard):*  
```xml
<Storyboard x:Key="SpinAnimation" RepeatBehavior="Forever">
    <DoubleAnimation Storyboard.TargetName="RefreshRotation"
                     Storyboard.TargetProperty="Angle"
                     From="0" To="360" Duration="0:0:0.8"/>
</Storyboard>
```
This animates the `Angle` property of a `RotateTransform` on the refresh button — making it spin while loading.

*Smart taskbar-aware positioning:*  
```csharp
public void PositionWindow()
{
    // Detect if taskbar is on right, left, top, or bottom
    // Position popup near the tray area, avoiding overlap
}
```
`Screen.PrimaryScreen.WorkingArea` vs `Screen.PrimaryScreen.Bounds` reveals which edge the taskbar is on.

**`SetupWindow.xaml` + `.cs`**  
A multi-panel modal dialog. Uses `Visibility.Collapsed` / `Visibility.Visible` to show one panel at a time:
- `ScanningPanel` — shown while scanning browsers (animated spinner)
- `PickerPanel` — lists found sessions as clickable cards
- `ConnectingPanel` — shown while auto-connecting
- `ManualPanel` — fallback: session key + org ID text fields

The session picker uses an `ItemsControl` with a `DataTemplate` — WPF's way of rendering a list of objects as repeating UI:
```xml
<ItemsControl x:Name="SessionList">
    <ItemsControl.ItemTemplate>
        <DataTemplate>
            <!-- This template renders once per DiscoveredSession object in the list -->
            <Button Tag="{Binding}" Click="SessionCard_Click">
                <TextBlock Text="{Binding BrowserName}"/>
            </Button>
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

**`UI/Controls/LimitRowControl.xaml` + `.cs`**  
A reusable `UserControl` that renders one usage row. It has a custom **DependencyProperty**:
```csharp
public static readonly DependencyProperty LimitProperty =
    DependencyProperty.Register(nameof(Limit), typeof(LimitData), typeof(LimitRowControl),
        new PropertyMetadata(null, OnLimitChanged));
```

`DependencyProperty` is WPF's extended property system. It enables:
- Data binding: `<controls:LimitRowControl x:Name="Row1"/>`
- Change callbacks: `OnLimitChanged` is called when the parent sets a new `LimitData`
- Property inheritance, animation, and styling hooks

---

## 3. Startup Flow

```
Program starts (WinExe, no console)
    │
    ▼
App.xaml.cs → OnStartup()
    │
    ├── Single-instance guard (Mutex)
    │       If another instance is running → Shutdown() immediately
    │
    ├── Create services:
    │       CredentialsStore store
    │       ClaudeConfigReader configReader
    │       ClaudeApiService api
    │       UsageFetcher fetcher(api, store)
    │
    ├── Create UI:
    │       PopupWindow popup(fetcher, store, configReader)
    │       TrayManager tray(popup, fetcher, store, configReader)
    │
    ├── Load credentials from %APPDATA%\ClaudeUsage\settings.json
    │
    ├── IF not configured (first run):
    │       Wait 500ms (let tray icon appear first)
    │       Show SetupWindow → user connects → saves credentials
    │
    └── IF already configured:
            Start 60s timer
            Fetch usage immediately
            Show popup
```

---

## 4. Data Flow — End to End

```
Browser on disk
  Chrome: %LOCALAPPDATA%\Google\Chrome\User Data\Default\Network\Cookies  (SQLite, encrypted)
  Edge:   %LOCALAPPDATA%\Microsoft\Edge\User Data\Default\Network\Cookies
  Firefox: %APPDATA%\Mozilla\Firefox\Profiles\*.default\cookies.sqlite
    │
    │ BrowserCookieReader
    │   1. Copy Cookies file to %TEMP%\claudeusage-{guid}\Cookies
    │   2. Open copy with SQLite (read-only, avoids locking the live browser file)
    │   3. SELECT name, encrypted_value FROM cookies WHERE host_key = 'claude.ai'
    │   4. Decrypt each value: AES-256-GCM with master key from Local State (DPAPI-unwrapped)
    │   5. Build Dictionary<string, string> of cookie name → plaintext value
    │   6. Delete temp copy
    │
    ▼
BrowserCookieContext { Cookies = { "sessionKey": "...", "cf_clearance": "...", ... } }
    │
    │ ClaudeApiService.BuildCookieHeaderAsync()
    │   Cached for 5 minutes; runs on thread pool (Task.Run)
    │   Builds: "sessionKey=...; cf_clearance=...; CH-prefers-color-scheme=dark; ..."
    │
    ▼
HTTP GET https://claude.ai/api/organizations/{orgId}/usage
  Headers:
    Cookie: [full cookie header]
    User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/135.0.0.0
    anthropic-client-platform: web_claude_ai
    origin: https://claude.ai
    referer: https://claude.ai/settings/usage
    │
    ▼ (if Cloudflare passes)
JSON response:
{
  "five_hour":        { "utilization": 0.42, "resets_at": "2026-04-26T14:30:00Z" },
  "seven_day":        { "utilization": 0.18, "resets_at": "2026-04-28T00:00:00Z" },
  "seven_day_sonnet": { "utilization": 0.31, "resets_at": "2026-04-28T00:00:00Z" }
}
    │
    │ ClaudeApiService.BuildLimitList()
    │   Deserializes JSON → UsageResponse → List<LimitData>
    │   Converts UTC resets_at to local time
    │
    ▼
UsageFetcher.Usage = new UsageInfo { Limits = [...], LastUpdated = now }
    │
    │ INotifyPropertyChanged fires PropertyChanged("Usage")
    │
    ▼
PopupWindow.RenderUsage()  [marshalled to UI thread via Dispatcher.Invoke]
    │
    ├── Row1.Limit = limits[0]  → "Current session"  42%  bar
    ├── Row2.Limit = limits[1]  → "Current week (all)" 18% bar
    └── Row3.Limit = limits[2]  → "Current week (Sonnet)" 31% bar
                                   └── LimitRowControl.OnLimitChanged()
                                         Sets bar width = percentage × track width
                                         Sets bar color: green / amber / red
                                         Formats reset time: "Resets in 4h at 2:30 PM GMT+3"
```

---

## 5. Security Model

### Cookie Encryption (Chromium Browsers)

Chrome, Edge, and Brave encrypt cookie values using a two-layer scheme:

**Layer 1 — Master Key (per-user, per-machine):**
- Stored in `%LOCALAPPDATA%\Google\Chrome\User Data\Local State` as a Base64 string
- Encrypted using **Windows DPAPI** (`ProtectedData.Unprotect`)
- DPAPI ties the encryption to the current Windows user account — nobody else can decrypt it, even on the same machine
- After decryption: 32-byte AES key

**Layer 2 — Individual Cookie Value:**
- Encrypted with **AES-256-GCM** using the master key
- Format: `[3 bytes version "v10"/"v11"][12 bytes IV][ciphertext][16 bytes auth tag]`
- The `AesGcm` class in .NET handles this directly

```csharp
// Decryption code in BrowserCookieReader.DecryptChromiumCookieValue:
var iv         = encValue[3..(3 + 12)];   // 12-byte nonce
var tag        = encValue[^16..];          // 16-byte auth tag
var ciphertext = encValue[(3 + 12)..^16]; // the actual encrypted value

using var aes = new AesGcm(masterKey, 16);
var plaintext = new byte[ciphertext.Length];
aes.Decrypt(iv, ciphertext, tag, plaintext);
```

**Why does this work for us?**  
We're running as the same Windows user as Chrome, so DPAPI decrypts the master key successfully. We're not breaking encryption — we're using it exactly as Chrome itself would.

### Firefox

Firefox does NOT use DPAPI or AES-GCM. It uses **Mozilla NSS** (Network Security Services) with a key stored in `key4.db` (SQLite) protected by a master password. This requires a native NSS library or reimplementation. This project currently only reads Firefox cookies in plaintext (old profiles without encryption). Full Firefox support would require shipping `nss3.dll` or using P/Invoke — a significant addition.

### Credential Storage

The session key is saved to `%APPDATA%\ClaudeUsage\settings.json` in plaintext. This is:
- **Acceptable**: The file is protected by NTFS ACLs to the current user only
- **Same security level as browsers**: Chrome stores session cookies in an SQLite file with the same NTFS protection (encryption only prevents other Windows accounts from reading it)
- **Not perfect**: A malicious process running as the same user could read the file — same threat model as any browser cookie

---

## 6. Cloudflare Bypass — Full Technical Explanation

### What Cloudflare Does

When you visit `claude.ai` in Chrome, Cloudflare runs a JavaScript challenge invisibly in the background. It:
1. Checks browser fingerprint (JavaScript capabilities, timing, etc.)
2. If it looks like a real browser: sets a `cf_clearance` cookie (valid ~1 hour)
3. If it doesn't (plain HTTP client): returns an HTML challenge page

### Why Plain HttpClient Fails

```csharp
// This request will get a Cloudflare HTML page, not JSON:
var response = await _http.GetAsync("https://claude.ai/api/organizations/.../usage");
```

`HttpClient` has no JavaScript engine. Cloudflare sees:
- No `cf_clearance` cookie (never ran the JS challenge)
- User-Agent doesn't match a real browser
- No `sec-ch-ua` client hints headers

### How We Bypass It

The app reads `cf_clearance` (and all other `claude.ai` cookies) from the user's Chrome profile — cookies Chrome got after Cloudflare verified it was a real browser. We then send those cookies in our HTTP request:

```
Cookie: sessionKey=sk-ant-...; cf_clearance=ABC123...; __cf_bm=XYZ...; CH-prefers-color-scheme=dark
```

Cloudflare sees its own `cf_clearance` cookie and allows the request through — because that cookie proves a real browser already passed the challenge.

### The Arms Race

This works today but is fragile:
- Cloudflare can invalidate `cf_clearance` more aggressively
- Cloudflare can add fingerprinting that checks browser TLS fingerprint (not just cookies)
- Anthropic can move the API endpoint
- The JSON response format can change

This is why this approach is documented as a known limitation.

---

## 7. WPF Patterns Reference

### Pattern: Styles and ControlTemplates

WPF lets you completely redefine how a control looks using a `ControlTemplate`:
```xml
<Style x:Key="SaveButton" TargetType="Button">
    <Setter Property="Template">
        <Setter.Value>
            <ControlTemplate TargetType="Button">
                <!-- The entire visual tree of the button is replaced -->
                <Border x:Name="Bd" Background="{TemplateBinding Background}" CornerRadius="9">
                    <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                </Border>
                <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                        <Setter TargetName="Bd" Property="Background" Value="#FF5722"/>
                    </Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>
```
`{TemplateBinding Background}` reads the `Background` property from the control itself — so the template is reusable with different colors.

### Pattern: DispatcherTimer

WPF's `DispatcherTimer` fires on the UI thread — unlike `System.Timers.Timer` which fires on a thread pool thread. This matters because only the UI thread can update WPF controls:

```csharp
_timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
_timer.Tick += async (_, _) => await FetchAsync(); // safe to call UI code inside
_timer.Start();
```

### Pattern: async/await in WPF

`async void` is used for event handlers (Loaded, Click) because event handlers return `void`:
```csharp
private async void OnLoaded(object sender, RoutedEventArgs e)
{
    // Can safely await here — resumes on UI thread after each await
    sessions = await Task.Run(() => _cookieReader.ScanAllBrowsers());
    ShowPanel(Panel.Picker); // safe — still on UI thread
}
```

`Task.Run` explicitly pushes synchronous work to the thread pool, freeing the UI thread.

### Pattern: Single-Instance Guard (Mutex)

```csharp
_mutex = new Mutex(true, "ClaudeUsageApp_SingleInstance", out bool isNewInstance);
if (!isNewInstance) { Shutdown(); return; }
```

A named `Mutex` is a Windows kernel object — visible across all processes. If the mutex already exists (another instance is running), `isNewInstance` is `false` and the new instance exits immediately. The mutex name must be unique to your app.

### Pattern: Pack URIs

WPF resources embedded in the assembly are referenced using `pack://` URIs:
```csharp
var uri = new Uri("pack://application:,,,/Resources/icon.ico", UriKind.Absolute);
```
`application:,,,` means "this application's assembly". `/Resources/icon.ico` is the path inside the assembly (matching the file's Build Action = Resource in the .csproj).

---

## 8. Why Each Design Choice Was Made

| Decision | Why |
|----------|-----|
| `SizeToContent="Height"` on windows | Lets WPF calculate height from content automatically — no magic numbers |
| `ShutdownMode="OnExplicitShutdown"` | Keeps the app alive in the tray when all windows are closed |
| `WindowStyle="None"` + `AllowsTransparency` | Required for transparent/rounded custom windows |
| `ShowInTaskbar="False"` | The popup should only be in the tray, not the taskbar |
| `Topmost="True"` on SetupWindow | Modal dialog should stay on top of other apps |
| `UseCookies=false` on HttpClientHandler | We inject cookies manually in headers — letting HttpClient manage them would interfere |
| `FileShare.ReadWrite \| FileShare.Delete` | Allows copying a file even while another process (Chrome) has it open |
| `using var conn = new SqliteConnection(...)` | Ensures the SQLite connection is closed and the temp file can be deleted |
| 5-minute cookie context cache | `cf_clearance` is valid ~1 hour; 5 min cache reduces scan overhead without risk of stale data |
| `SemaphoreSlim(1,1)` for scan lock | Prevents parallel browser scans from the timer and a manual refresh firing simultaneously |
