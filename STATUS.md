# Claude Usage App — Project Status & Issue Tracker

**Last updated:** 2026-04-25  
**Branch:** main  
**Build status:** ✅ Compiles with 0 errors (2 harmless DPI warnings)

---

## What This App Does

A Windows system-tray app that shows your Claude AI usage limits (current session, weekly, weekly Sonnet) as a popup — so you don't have to open claude.ai every time. Refreshes every 60 seconds automatically.

---

## Current State Summary

| Area | Status | Notes |
|------|--------|-------|
| Build | ✅ Works | 0 errors |
| UI / Tray icon | ✅ Works | Popup, gear, refresh, quit all wired |
| Auto browser scan | ✅ Works (logic) | Chrome, Edge, Brave, Firefox, Claude Desktop |
| Cookie decryption | ✅ Works | DPAPI + AES-GCM for Chromium |
| Cloudflare bypass | ⚠️ Partial | Full cookie approach coded but blocks UI thread |
| API calls | ❌ Blocked | Cloudflare returns HTML instead of JSON |
| UI responsiveness | ❌ Freezes | Browser scan runs on UI thread (2–10 sec freeze) |
| Org ID detection | ✅ Works | Reads Claude CLI + Desktop config; API fallback added |
| Setup window | ✅ Works | Auto-scan, picker, manual entry, close button |
| Credentials save | ✅ Works | Saved to %APPDATA%\ClaudeUsage\settings.json |

---

## Issues (Detailed)

---

### ISSUE 1 — Cloudflare Blocks API Requests
**Severity:** HIGH — App shows no data at all  
**Status:** ⚠️ Fix coded but not yet fully applied

**What happens:**  
The app makes a direct HTTP GET request to `https://claude.ai/api/organizations/{id}/usage`.  
Cloudflare intercepts this and returns a `<!DOCTYPE html>` challenge page instead of JSON.  
The app detects this and shows the error: *"Blocked by Cloudflare challenge."*

**Why it happens:**  
Cloudflare protects claude.ai. When an HTTP client (not a real browser) makes a request, Cloudflare challenges it with a JS-based bot check. A plain `HttpClient` cannot pass this check.

**The coded fix:**  
Instead of sending only `sessionKey={value}`, the app now reads ALL Claude cookies from the user's actual browser (Chrome/Edge/Firefox) — including Cloudflare's own `cf_clearance` cookie — and sends those in the request header. Cloudflare then sees a cookie it issued to a real browser and allows the request through.

**Will this work?**  
YES, under these conditions:
- User is signed into claude.ai in Chrome, Edge, Firefox, or Brave on this machine
- The `cf_clearance` cookie exists and is not expired (valid ~1 hour after last browser visit)
- The `sessionKey` cookie is present in the browser

**Will NOT work if:**
- User never opened claude.ai in a browser on this machine
- Browser cookies are cleared or expired
- Cloudflare issues a brand-new JS challenge (rare; visiting claude.ai in the browser resets it)

**What to do if still blocked:**  
Open claude.ai in your browser, wait for the page to fully load (Cloudflare runs its check), then click Refresh in the app. The `cf_clearance` cookie is now in your browser and the app will pick it up.

---

### ISSUE 2 — Browser Scanning Freezes the UI Thread
**Severity:** HIGH — App appears "Not Responding" for seconds  
**Status:** ❌ Partially fixed in code, not yet committed

**What happens:**  
`ClaudeApiService.BuildCookieHeader()` calls `BrowserCookieReader.FindContextBySessionKey()` which scans 7 Chromium browser installs + Firefox. It opens SQLite cookie databases, creates temp file copies, runs SQL queries, and decrypts AES keys — all synchronously.

This code is called BEFORE the first `await` in `FetchUsageAsync` and `FetchOrganizationIdAsync`. In WPF, everything before the first `await` runs on the UI thread. So the window freezes for 2–10 seconds on:
- App startup (first fetch)
- Every 60-second auto-refresh
- Every manual Refresh click

**The fix (coded, not yet built/committed):**  
- Move browser scanning to a background thread using `Task.Run`
- Cache the result for 5 minutes (don't rescan on every refresh)
- Use a `SemaphoreSlim` to prevent concurrent scans

**Files changed for this fix:**
- [Core/Services/ClaudeApiService.cs](Core/Services/ClaudeApiService.cs) — `BuildCookieHeaderAsync()` replaces `BuildCookieHeader()`

---

### ISSUE 3 — Dead Code: `_api` Field in PopupWindow
**Severity:** LOW — No functional impact, just cleanup  
**Status:** ❌ Fix coded, not yet committed

**What happens:**  
`PopupWindow` constructor accepts a `ClaudeApiService api` parameter and stores it as `_api`, but never uses it anywhere in the class. `SetupWindow` (opened from the gear button) creates its own `ClaudeApiService` instance.

**The fix:**  
Remove `_api` field, remove the parameter from `PopupWindow` constructor, update the call site in `App.xaml.cs`.

---

### ISSUE 4 — Uncommitted Working-Tree Changes
**Severity:** LOW — Changes are correct but unsaved to git  
**Status:** ❌ Not committed

**Files with good changes not yet committed:**

| File | What changed | Staged? |
|------|-------------|---------|
| `Core/Services/BrowserCookieReader.cs` | Added `BrowserCookieContext` class, `ScanAllBrowserContexts()`, `FindContextBySessionKey()` | ✅ Staged |
| `Core/Services/ClaudeApiService.cs` | Windows User-Agent, full cookie header, better Cloudflare error messages, async fix | ❌ Unstaged |
| `Core/Services/ClaudeConfigReader.cs` | Claude Desktop config + blocklist file as org ID sources | ❌ Unstaged |
| `UI/SetupWindow.xaml.cs` | API-based org ID detection as fallback when config files don't have it | ❌ Unstaged |
| `README.md` | Improved user docs with troubleshooting, cleaner format | ❌ Unstaged |

---

## Git History

```
839e666  fix: Resolve content-type header issue and add Close button to SetupWindow
87e3c0e  fix: Update SetupWindow constructor references
21b5a34  feat: Automate browser cookie session extraction
6aa3f13  Initial commit - Claude Usage Windows app
```

**Stash:** `stash@{0}: On main: pre-update-2026-04-17`  
(A saved snapshot from before the browser cookie feature was added)

---

## What Needs to Happen to Make the App Work

### Step 1 — Complete the async fix (Issue 2)
In [Core/Services/ClaudeApiService.cs](Core/Services/ClaudeApiService.cs):
- Replace synchronous `BuildCookieHeader(string, out bool)` with async `BuildCookieHeaderAsync(string)` returning `Task<(string header, bool hasContext)>`
- Add cache fields: `_cachedContext`, `_cachedSessionKey`, `_cacheExpiry`, `_scanLock`
- Wrap `_cookieReader.FindContextBySessionKey(sessionKey)` in `Task.Run(...)`
- Update callers in `FetchOrganizationIdAsync` and `FetchUsageAsync`

### Step 2 — Clean up PopupWindow (Issue 3)
In [UI/PopupWindow.xaml.cs](UI/PopupWindow.xaml.cs):
- Remove `private readonly ClaudeApiService _api;`
- Remove `_api = api;` from constructor
- Change constructor signature: remove `ClaudeApiService api` parameter

In [App.xaml.cs](App.xaml.cs):
- Change: `new PopupWindow(_fetcher, store, configReader, api)` → `new PopupWindow(_fetcher, store, configReader)`

### Step 3 — Commit everything
Stage all modified files and commit with a clear message.

### Step 4 — Test
1. Run `dotnet build` — must be 0 errors
2. Open claude.ai in your browser first (ensures fresh Cloudflare cookie)
3. Launch the app
4. Popup appears without freezing
5. Usage data loads (no Cloudflare error)

---

## Technical Architecture (Quick Reference)

```
App.xaml.cs          — Startup, single-instance guard, service wiring
│
├── CredentialsStore     — Reads/writes %APPDATA%\ClaudeUsage\settings.json
├── ClaudeConfigReader   — Reads org ID from ~/.claude.json or Claude Desktop config
├── ClaudeApiService     — HTTP client for claude.ai API, cookie building
│    └── BrowserCookieReader — Decrypts & reads cookies from Chrome/Edge/Firefox
├── UsageFetcher         — 60s timer, calls API, exposes UsageInfo via INotifyPropertyChanged
│
├── TrayManager          — System tray icon, context menu, left-click toggle
├── PopupWindow          — Main usage popup (3 limit rows, error/loading states)
│    └── LimitRowControl — Single row: label, progress bar, reset time
└── SetupWindow          — First-launch setup: browser scan, picker, manual entry
```

---

## Cloudflare Bypass — How It Works (Full Detail)

1. User opens claude.ai in Chrome → Cloudflare validates their browser via JavaScript → stores `cf_clearance` cookie
2. This app's `BrowserCookieReader` copies Chrome's Cookies SQLite file to a temp folder
3. Reads all cookies for `claude.ai` domain, decrypts them (Chrome encrypts cookies with DPAPI + AES-GCM)
4. Sends ALL cookies in the HTTP request header: `Cookie: sessionKey=...; cf_clearance=...; __cf_bm=...; CH-prefers-color-scheme=dark; ...`
5. Cloudflare sees its own `cf_clearance` cookie → treats the request as coming from a validated browser → allows through
6. Claude API returns proper JSON with usage data

**If the `cf_clearance` cookie is missing or expired:**  
App falls back to sending only `sessionKey={value}` → Cloudflare blocks it → error shown.  
**Solution:** Visit claude.ai in your browser to refresh the Cloudflare cookie.

---

## Known Limitations

- Only works on Windows (DPAPI for cookie decryption is Windows-only)
- Requires the user to have claude.ai open in a local browser (for Cloudflare bypass)
- Firefox cookie decryption not implemented (Firefox uses NSS crypto, not DPAPI) — Firefox cookies are read but only if stored in plaintext (older profiles)
- No support for browser profiles that use a separate encryption key (enterprise setups)
