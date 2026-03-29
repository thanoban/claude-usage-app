# Claude Usage App for Windows

> **Track your Claude AI usage limits right from your Windows desktop. No more switching browser tabs just to check if you're near your limit.**

---

## 🌟 The Problem This Solves

Claude AI enforces rolling usage limits:
- **5-hour session limit** — how much you've used in the last 5 hours
- **7-day all-models limit** — weekly usage across all Claude models
- **7-day Sonnet limit** — weekly usage specifically for Claude Sonnet

The only way to check these is to navigate to the hidden `claude.ai/settings/usage` page in your browser. If you're a heavy Claude user, this becomes tedious — especially when you hit a limit mid-conversation with no warning.

**Claude Usage** sits silently in your Windows system tray (bottom-right near your clock) and shows your real-time limits with a single click. Color-coded progress bars (green → amber → red) let you instantly gauge your remaining quota.

---

## 🎯 Features

| Feature | Detail |
|---|---|
| 🔔 **System tray icon** | Lives quietly next to your clock. |
| 📊 **3 usage rows** | Tracks Session (5h), Weekly all-models, and Weekly Sonnet limits. |
| ♻️ **Auto-refresh** | Updates every 60 seconds automatically. |
| 🖱️ **Instant Access** | Click the tray icon OR the Desktop shortcut to instantly view your limits. |
| ⚙️ **Settings screen** | Update your session key any time. |
| 🔍 **Auto-detect org** | Automatically reads Organization ID from the Claude Code CLI. |
| 🔒 **100% local, Private** | No third-party servers, no telemetry, no tracking. |
| 🚀 **Standalone File** | Just a single `.exe` file. No messy installations needed. |

---

## 📖 User Manual: How to Use the App

### 1. Opening the App
Because this is a **System Tray Application** (like an antivirus, Spotify, or Discord), it is designed to stay out of your way. 
- When you launch the app, look at the **bottom right of your screen** (where your Windows clock and WiFi icons are). 
- Look for the little orange **Claude (C) icon**. *(If you don't see it, click the small UP arrow `^` next to the clock to "Show hidden icons").*
- **Double-clicking the Desktop Shortcut** will forcefully pop open the UI so you can always find it easily!

### 2. The Main Popup Window
Click the Claude icon in the system tray to open the **Limits Panel**. 
- **Progress bar colors:**
  - 🟢 **Green** — Below 80% capacity (you're perfectly fine).
  - 🟡 **Amber** — 80–94% capacity (getting close to your limit).
  - 🔴 **Red** — 95%+ capacity (you're nearly at the limit).
- **Refresh:** Click the ↺ icon to instantly fetch your latest usage.
- **Settings:** Click the ⚙ icon to update your Claude credentials if they expire.
- **Quit:** Click the `Quit` button to fully exit the background application. (Clicking outside the window simply hides it back into the tray).

---

## 🛠️ First-Time Setup

When you launch the app for the very first time, a setup window will appear right in the middle of your screen:

1. **Enter your Session Key** — This acts as your secure password to check your limits. It always starts with `sk-ant-...`. (See below on how to get it).
2. **Organization ID** — If you use the Claude Code CLI, this is auto-filled! Otherwise, enter it manually.
3. Click **Save & Connect** — The panel will immediately connect to Claude and fetch your usage!

---

## 🔑 How to Get Your Session Key

Your session key is the secure authentication cookie Claude's web app uses. Here's how to securely copy it straight from your browser:

**Step-by-step (Chrome/Edge):**
1. Go to **[https://claude.ai](https://claude.ai)** and log in normally.
2. Press **`F12`** on your keyboard to open your browser's Developer Tools.
3. Click the **Application** tab at the top. *(You may need to click the `»` arrows to find it).*
4. In the left sidebar, expand **Cookies** and click on **`https://claude.ai`**.
5. In the table that appears, find the row named **`sessionKey`**.
6. **Double-click the Value column** next to it to select the long text.
7. Press **`Ctrl+C`** to copy it. *(It should look like `sk-ant-api03...`)*.
8. Paste it into the Claude Usage **Setup window**!

> ⚠️ **Security note:** Your session key is equivalent to your password. It is stored securely and ONLY locally on your machine at `%APPDATA%\ClaudeUsage\settings.json`. It is never sent anywhere except directly to `claude.ai`.

---

## 🏢 How to Find Your Organization ID

If you use **Claude Code** (the terminal CLI tool), your Organization ID is **auto-detected** — you don't need to do anything! 

If not, you can find it easily:
1. Log into Claude.ai and navigate to [https://claude.ai/settings](https://claude.ai/settings).
2. Look at the URL in your browser's address bar. 
3. The UUID string inside the URL is your Organization ID! 
   *(Example: `claude.ai/settings/123e4567-e89b-12d3...` -> Your ID is `123e4567-e89b-12d3...`)*

---

## 🔄 Updating Your Session Key

Session keys expire naturally after a few weeks for your security. When this happens, the app will show:
> *"Session expired. Please update your session key."*

1. Click the **⚙ gear icon** in the popup header.
2. Get a fresh session key from Claude.ai using the `F12` browser method above.
3. Paste the new key into the Settings window and click **Save & Connect**.

---

## ❓ FAQ

**Q: I double-clicked the app, but nothing opened!**
A: System Tray apps run silently! Look in the bottom right corner of your screen (near the Windows clock) for the small orange Claude icon. You may need to click the `^` arrow to see hidden icons. (We also recently pushed an update so double-clicking the shortcut will aggressively force the window to pop up for better visibility!)

**Q: Why does usage show 0% even though I've been using Claude a lot?**
A: Make sure your Organization ID is perfectly correct. The usage API is tied to your specific org.

**Q: Does this work with the Claude Free tier?**
A: Yes! The API endpoint is identical. It works beautifully as long as your session cookie is valid.

## Privacy & Security

- ✅ Session key stored **locally only**.
- ✅ API calls go **directly to `claude.ai`** — no third-party intermediary servers.
- ✅ **No telemetry**, no analytics, no crash reporting.
- ✅ **No heavy background processes** taking up your computer's RAM.
- ✅ Full **open-source transparency**.

*Claude Usage App is an independent open-source project and is not affiliated with or endorsed by Anthropic.*
