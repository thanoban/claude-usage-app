# Claude Usage — Windows System Tray App

A lightweight Windows desktop app that shows your **Claude AI usage limits** in a tray popup — so you never have to open a browser tab to check how much of your Pro/Max plan you've used.

---

## What Problem Does This Solve?

Claude Pro and Max subscribers have usage limits across three rolling windows:

| Limit | Resets every |
|-------|-------------|
| Current session | 5 hours |
| Weekly (all models) | 7 days |
| Weekly (Sonnet only) | 7 days |

The only official way to see these is to navigate to **claude.ai → Settings → Usage** — a manual step that heavy users repeat many times a day. This app puts those three numbers one click away in your Windows system tray.

---

## Features

- **System tray icon** — sits quietly in your taskbar notification area
- **One-click popup** — left-click the icon to show/hide usage
- **Color-coded bars** — green (under 80%), amber (80–94%), red (95%+)
- **Auto-refresh** — updates every 60 seconds automatically
- **Manual refresh** — click ↺ to force an immediate update
- **Auto-detection** — finds your Claude session from your browser (Chrome, Edge, Firefox, Brave) without you typing anything
- **Last-updated timestamp** — shows how fresh the data is
- **Settings window** — gear icon opens setup to update your session

---

## How It Works (Plain English)

1. **Your browser has a session cookie** called `sessionKey` that proves you're logged into claude.ai. This app reads that cookie directly from Chrome/Edge/Firefox's cookie database on your disk.

2. **Cloudflare protects claude.ai** from bots. To get past it, the app reads ALL your browser's claude.ai cookies (including Cloudflare's own `cf_clearance` token) and sends them with each request — making the request look like it came from your real browser session.

3. **The app calls an internal Claude API** (`/api/organizations/{orgId}/usage`) to fetch the three usage percentages as JSON.

4. **The popup renders** three progress bars with labels and reset times.

---

## Tech Stack

| Layer | Technology |
|-------|-----------|
| UI framework | WPF (Windows Presentation Foundation), .NET 8 |
| Language | C# 12 |
| Tray icon | Hardcodet.Wpf.TaskbarNotification |
| Cookie database | Microsoft.Data.Sqlite |
| Cookie decryption | Windows DPAPI + AES-256-GCM |
| HTTP | System.Net.Http.HttpClient |
| Config | JSON files via System.Text.Json |

---

## Project Structure

```
claude-usage-app/
│
├── App.xaml                    # WPF app entry point (declares the Application)
├── App.xaml.cs                 # Startup logic: services, windows, single-instance guard
├── app.manifest                # Windows manifest: DPI awareness, UAC level
├── ClaudeUsage.csproj          # Project file: targets .NET 8, enables WPF + WinForms
│
├── Core/
│   ├── Models/
│   │   ├── LimitData.cs        # One usage row: label, percentage, reset time
│   │   ├── UsageInfo.cs        # All three limits + loading/error state
│   │   └── UsageResponse.cs    # Raw JSON shape returned by claude.ai API
│   │
│   ├── Services/
│   │   ├── BrowserCookieReader.cs  # Reads + decrypts cookies from Chrome/Edge/Firefox
│   │   ├── ClaudeApiService.cs     # HTTP calls to claude.ai; builds cookie headers
│   │   ├── ClaudeConfigReader.cs   # Reads org ID from ~/.claude.json or Claude Desktop
│   │   └── CredentialsStore.cs     # Saves/loads sessionKey + orgId to %APPDATA%
│   │
│   └── UsageFetcher.cs         # 60s timer; calls API; exposes UsageInfo to UI
│
├── UI/
│   ├── Controls/
│   │   ├── LimitRowControl.xaml    # Reusable control: one row = label + bar + reset time
│   │   └── LimitRowControl.xaml.cs
│   │
│   ├── PopupWindow.xaml        # The usage popup (dark card with three rows)
│   ├── PopupWindow.xaml.cs     # Positioning, rendering, spin animation
│   ├── SetupWindow.xaml        # First-run setup: scan browsers, pick session, manual entry
│   ├── SetupWindow.xaml.cs     # Browser scan logic, auto-connect, save
│   └── TrayManager.cs          # Creates tray icon, context menu, left-click handler
│
├── Resources/
│   └── icon.ico                # App icon (tray + taskbar)
│
├── Installer/
│   └── setup.iss               # InnoSetup script for building a Windows installer
│
├── README.md                   # This file — overview and setup
├── ARCHITECTURE.md             # Deep technical guide for learners
├── DEVELOPMENT_PLAN.md         # Known issues, roadmap, fix instructions
└── STATUS.md                   # Current project status snapshot
```

---

## Setup & Installation

### Requirements
- Windows 10 or 11
- .NET 8 Runtime ([download](https://dotnet.microsoft.com/download/dotnet/8.0))
- Chrome, Edge, Firefox, or Brave — signed into claude.ai
- A Claude Pro or Max subscription

### Run from Source
```bash
git clone https://github.com/thanoban/claude-usage-app.git
cd claude-usage-app
dotnet run
```

### Build a Self-Contained Executable
```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
# Output: bin\Release\net8.0-windows\win-x64\publish\ClaudeUsage.exe
```

---

## First Run

1. Launch the app — a tray icon appears in the bottom-right of your taskbar
2. The **Setup window** opens automatically
3. The app scans Chrome, Edge, Firefox, Brave for an active claude.ai session
4. If found: it connects automatically (takes ~2 seconds)
5. If not found: enter your Session Key manually (see below)
6. The usage popup appears with your current limits

### How to Get Your Session Key Manually

1. Open Chrome and go to `claude.ai`
2. Press `F12` → Application tab → Cookies → `https://claude.ai`
3. Find the cookie named `sessionKey` → copy its value
4. Paste into the Setup window

### How to Find Your Organization ID

Usually auto-detected. If not:
1. Go to `claude.ai/settings/usage`
2. Look for a UUID like `a1b2c3d4-xxxx-xxxx-xxxx-xxxxxxxxxxxx` in the URL or page source — that is your org ID

---

## Known Limitations

| Limitation | Why |
|-----------|-----|
| **Windows only** | Cookie decryption uses Windows DPAPI, a Windows-exclusive API |
| **Requires browser sign-in** | Needs `cf_clearance` Cloudflare cookie from a real browser visit |
| **May be blocked by Cloudflare** | If browser cookies expire, open claude.ai in your browser and click Refresh |
| **Scrapes an internal API** | Anthropic has no public usage API; this uses the same private endpoint the web app uses — it could change without notice |
| **Firefox decryption** | Firefox uses NSS crypto (not DPAPI), so Firefox cookies are only readable if stored in plaintext |

---

## Troubleshooting

**"Blocked by Cloudflare" error**
Open `claude.ai` in your browser, let the page fully load, then click ↺ Refresh in the app.

**"Session expired" error**
Click the gear icon in the popup and re-run setup to pick up a fresh session key.

**App opened but I can't see the window**
Look for the icon in the system tray (bottom-right, click the ^ arrow to expand). Left-click it.

**Usage numbers look wrong**
Claude's API reports rolling windows — the percentage resets gradually, not at midnight.

---

## Contributing

This is a learning project built to explore WPF and Windows desktop development. Issues and PRs are welcome. See [ARCHITECTURE.md](ARCHITECTURE.md) for a deep explanation of how every part works, and [DEVELOPMENT_PLAN.md](DEVELOPMENT_PLAN.md) for the list of known issues and the roadmap.
