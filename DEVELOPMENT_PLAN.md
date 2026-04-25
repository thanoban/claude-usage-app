# Development Plan

This document tracks every known issue, the full roadmap, and exact instructions for fixing each problem. Anyone picking up this project should start here.

---

## Current Build Status

| Item | Status |
|------|--------|
| `dotnet build` | ✅ 0 errors, 2 harmless warnings |
| Runtime (API fetch) | ❌ Cloudflare blocks most requests |
| Runtime (UI thread) | ❌ Freezes 2–10s on every refresh |
| Uncommitted changes | ⚠️ 5 files modified but not committed |

---

## Known Issues

---

### ISSUE 1 — Cloudflare Blocks API Requests
**Severity:** HIGH  
**Status:** Fix partially coded (full-cookie approach) — needs testing

**Symptom:**  
App shows: *"Blocked by Cloudflare challenge. Open claude.ai in your browser, then click Refresh."*

**Root cause:**  
`claude.ai` is protected by Cloudflare. When a plain `HttpClient` (not a real browser) calls `https://claude.ai/api/organizations/{id}/usage`, Cloudflare returns an HTML challenge page (`<!DOCTYPE html>`) instead of JSON.

**The coded fix:**  
`BrowserCookieReader` reads ALL cookies for `claude.ai` from the user's browser — including Cloudflare's own `cf_clearance` cookie. `ClaudeApiService.BuildCookieHeaderAsync()` sends this full cookie set with every request. Cloudflare recognises its own cookie and lets the request through.

**This fix works when:**
- The user has visited claude.ai in their browser recently (within the last ~1 hour)
- Chrome/Edge/Firefox has a `cf_clearance` cookie for `claude.ai`
- The `sessionKey` cookie is also present

**This fix does NOT work when:**
- User has never opened claude.ai in a browser on this machine
- Browser cookies were cleared
- Cloudflare issued a fresh JS challenge (open the browser and visit claude.ai to resolve)

**Manual workaround for end users:**
> Open claude.ai in Chrome or Edge, wait for it to fully load (Cloudflare runs silently), then click ↺ Refresh in the app.

**Remaining work:**
- [ ] Test on a machine where Cloudflare is actively blocking
- [ ] Verify `cf_clearance` cookie is being picked up correctly by `ReadChromiumCookies()`
- [ ] If still blocked, investigate whether Cloudflare requires additional headers (`sec-ch-ua`, `sec-ch-ua-platform`, etc.)

---

### ISSUE 2 — Browser Scanning Freezes the UI Thread
**Severity:** HIGH  
**Status:** Fix written in `ClaudeApiService.cs` — `BuildCookieHeaderAsync()` uses `Task.Run` + 5-min cache

**Symptom:**  
The app window becomes unresponsive ("Not Responding" in Task Manager) for 2–10 seconds on startup, on every 60-second auto-refresh, and on every manual Refresh click.

**Root cause:**  
`BuildCookieHeader()` (original synchronous version) called `BrowserCookieReader.FindContextBySessionKey()` → `ScanAllBrowserContexts()` — which scans 7 Chromium browser installs + Firefox, opens SQLite databases, creates temp file copies, and runs SQL queries. All synchronously.

In WPF, any code that runs before the first `await` in an async method executes on the **UI thread**. Both `FetchUsageAsync` and `FetchOrganizationIdAsync` called `BuildCookieHeader` before their first `await` — so all that I/O ran on the UI thread, blocking it.

**The fix (coded, in working tree):**

```csharp
// ClaudeApiService.cs
private async Task<(string header, bool hasContext)> BuildCookieHeaderAsync(string sessionKey)
{
    await _scanLock.WaitAsync();
    try
    {
        if (_cachedSessionKey != sessionKey || DateTime.UtcNow > _cacheExpiry)
        {
            // Task.Run moves the synchronous I/O off the UI thread
            _cachedContext = await Task.Run(() =>
                _cookieReader.FindContextBySessionKey(sessionKey));
            _cachedSessionKey = sessionKey;
            _cacheExpiry = DateTime.UtcNow.AddMinutes(5); // cache for 5 min
        }
    }
    finally { _scanLock.Release(); }
    // ... build and return header
}
```

**Why `Task.Run`?**  
`FindContextBySessionKey` is synchronous code that does disk I/O. You cannot simply `await` a synchronous method. `Task.Run` moves it to the thread pool — freeing the UI thread to process messages (keep the window responsive) while the scan runs in the background.

**Why cache for 5 minutes?**  
Without caching, browser scanning would happen on every 60-second refresh (and on every manual refresh), consuming significant CPU and disk I/O. The `cf_clearance` cookie is valid for ~1 hour, so a 5-minute cache is safe and reduces overhead 5×.

**Why `SemaphoreSlim`?**  
Prevents two simultaneous refreshes from triggering two parallel browser scans. The second waits for the first to finish, then uses the cache.

**Remaining work:**
- [ ] Commit the working-tree changes
- [ ] Verify the UI stays responsive during refresh by observing the app

---

### ISSUE 3 — Dead Code: `_api` Field in PopupWindow
**Severity:** LOW  
**Status:** Fix applied (field and parameter removed from `PopupWindow.xaml.cs` and `App.xaml.cs`)

**Symptom:**  
No user-visible symptom. Wasted memory and confusing code.

**Root cause:**  
`PopupWindow` constructor accepted a `ClaudeApiService api` parameter and stored it as `_api`, but never used it. The gear button opens `SetupWindow` which creates its own `ClaudeApiService` internally.

**Fix applied:**
- Removed `private readonly ClaudeApiService _api;` from `PopupWindow.xaml.cs`
- Removed `_api = api;` assignment
- Removed `ClaudeApiService api` parameter from constructor
- Updated `App.xaml.cs` call site

---

### ISSUE 4 — Uncommitted Working Tree Changes
**Severity:** LOW  
**Status:** Pending commit

**Files with good, correct changes not yet saved to git:**

| File | What changed | Staged? |
|------|-------------|---------|
| `Core/Services/BrowserCookieReader.cs` | New `BrowserCookieContext` class, `ScanAllBrowserContexts()`, `FindContextBySessionKey()` | Staged |
| `Core/Services/ClaudeApiService.cs` | Async cookie building, Windows UA, full cookie header, cache, Cloudflare messages | Unstaged |
| `Core/Services/ClaudeConfigReader.cs` | Claude Desktop config + blocklist as org ID sources | Unstaged |
| `UI/SetupWindow.xaml.cs` | Added `_api`, API-based org ID fallback detection | Unstaged |
| `UI/PopupWindow.xaml.cs` | Removed dead `_api` field (fix applied) | Unstaged |
| `App.xaml.cs` | Updated PopupWindow constructor call (fix applied) | Unstaged |
| `README.md` | Full rewrite | Unstaged |
| `ARCHITECTURE.md` | New file | Untracked |
| `DEVELOPMENT_PLAN.md` | This file | Untracked |
| `STATUS.md` | Status snapshot | Untracked |
| `.claude/settings.json` | Claude Code permissions config | Untracked |

---

## Roadmap

### Now — Must Fix Before the App Is Usable

1. **Commit all changes** (Issue 4) — nothing is lost to git yet
2. **Test Issue 1 fix** — launch the app on a machine with Chrome signed into claude.ai; verify data loads without Cloudflare error
3. **Verify Issue 2 fix** — confirm the window stays responsive during refresh

### Soon — Nice to Have

- **Show absolute numbers, not just percentages**  
  The API might return raw token counts — display "42k / 100k tokens" instead of just "42%"

- **Firefox cookie decryption**  
  Firefox uses NSS (Network Security Services) for encryption — a separate library from DPAPI. Implementing NSS decryption in C# would allow Firefox users without Chrome/Edge to use the app.

- **Startup with Windows**  
  Add a registry key (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`) so the app launches automatically on login.

- **Auto-update the session key**  
  When session expires, automatically re-scan browsers for a fresh cookie instead of showing an error.

- **macOS/Linux port**  
  Would require replacing DPAPI decryption with the platform-native equivalents (macOS Keychain, libsecret on Linux) and dropping WPF for a cross-platform UI (MAUI, Avalonia, or Electron).

### Later / Maybe

- **Notification when approaching limits**  
  Windows toast notification when usage crosses 80% or 95%.

- **Usage history chart**  
  Store snapshots locally and show a 7-day usage graph.

- **Multiple account support**  
  Support switching between Claude accounts without re-entering credentials.

### Won't Fix

- **Public API instead of scraping**  
  Anthropic would need to publish an official usage API. We have no control over this; if they do, migrate immediately.

- **Cloudflare-proof without browser cookies**  
  Solving Cloudflare without browser cookies would require embedding a full browser engine (WebView2), which adds ~100MB and significant complexity for a tray app. Not worth it.

---

## Architecture Decisions & Why

### Why WPF?
WPF is the native Windows UI framework for .NET applications. It supports:
- Transparent, rounded windows (via `AllowsTransparency="True"`)
- Rich XAML styling without writing Win32 code
- System tray integration (via the `Hardcodet.Wpf.TaskbarNotification` library)
- Direct access to Windows APIs (DPAPI, file system) since it runs as a full .NET app

Alternatives considered:
- **WinForms** — older, no transparent windows, harder to style
- **.NET MAUI** — cross-platform but poor Windows tray support, larger binary
- **Electron** — cross-platform but ships a full Chromium browser (~150MB) for a tray app showing 3 numbers

### Why read browser cookies instead of asking the user?
Two reasons:
1. **UX**: Users don't know what a "session key" is. Asking them to open DevTools is friction that most users won't complete.
2. **Cloudflare bypass**: To bypass Cloudflare bot protection, we need the full set of cookies — not just the session key. Only the browser has these cookies. Reading them directly is the only way.

### Why SQLite file copy instead of reading the live database?
Chrome/Edge keep their cookie database open and locked. SQLite does not allow two processes to open the same WAL-mode database simultaneously for writing. By copying the file (+ WAL and SHM sidecar files) to a temp folder, we get a consistent snapshot that we can safely open in read-only mode without interfering with the browser.

### Why `SemaphoreSlim` for the cookie scan lock?
Multiple async paths can trigger `BuildCookieHeaderAsync` simultaneously (e.g., timer fires while user clicks Refresh). Without a lock, two parallel browser scans would run — doubling the work and potentially causing file contention in the temp folder. `SemaphoreSlim(1,1)` ensures only one scan runs at a time; the second waiter reuses the result from the cache.

### Why store credentials in `%APPDATA%\ClaudeUsage\settings.json`?
- `%APPDATA%` is the standard Windows location for per-user application data
- It persists across app restarts
- It is user-writable without elevation
- Plain JSON is simple to debug if something goes wrong

**Security note:** The session key is stored in plaintext. This is acceptable because:
- The file is only readable by the current Windows user (NTFS permissions)
- The session key is equivalent to a browser cookie — the same level of protection browsers give their cookies

---

## Step-by-Step Fix Instructions

### Fix Issue 2 (UI thread freeze) — already in working tree, just needs commit

The fix is already written in `Core/Services/ClaudeApiService.cs`. The key change:

```csharp
// BEFORE (blocks UI thread):
private string BuildCookieHeader(string sessionKey, out bool hasLocalSessionContext)
{
    var context = _cookieReader.FindContextBySessionKey(sessionKey); // synchronous I/O on UI thread!
    ...
}

// AFTER (runs on thread pool, cached):
private async Task<(string header, bool hasContext)> BuildCookieHeaderAsync(string sessionKey)
{
    await _scanLock.WaitAsync();
    try
    {
        if (_cachedSessionKey != sessionKey || DateTime.UtcNow > _cacheExpiry)
        {
            _cachedContext = await Task.Run(() =>  // ← off UI thread
                _cookieReader.FindContextBySessionKey(sessionKey));
            _cachedSessionKey = sessionKey;
            _cacheExpiry = DateTime.UtcNow.AddMinutes(5);
        }
    }
    finally { _scanLock.Release(); }
    ...
}
```

Callers updated in the same file:
```csharp
// FetchOrganizationIdAsync:
var (cookieHeader, _) = await BuildCookieHeaderAsync(sessionKey);

// FetchUsageAsync:
var (cookieHeader, hasLocalSessionContext) = await BuildCookieHeaderAsync(sessionKey);
```

### Fix Issue 1 (Cloudflare) — test and iterate

1. Build and run the app
2. Make sure Chrome is open and signed into claude.ai
3. Click Refresh in the popup
4. If still blocked: add `sec-ch-ua`, `sec-ch-ua-platform`, `sec-ch-ua-mobile` headers to `ClaudeApiService` constructor (these are browser client hint headers Cloudflare checks)
5. If still blocked: check that `cf_clearance` is actually present in the cookie header being sent (add temporary debug logging)
