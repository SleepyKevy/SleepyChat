# SleepyChat 1.0.0

**Made by SleepyKev • 2026**

SleepyChat is a standalone Windows desktop chat viewer that brings Kick and Twitch chat into one unified interface. The 1.0.0 source uses a single C#/.NET 8 desktop architecture: WinForms for the native shell, Microsoft WebView2 for the UI, and an in-process ASP.NET Core/Kestrel service bound to the local computer.

## What is included

- Unified **All / Kick / Twitch / Mentions** chat views
- Search and role filtering
- Twitch and Kick username colors
- Twitch badge catalog support and channel badges
- Kick role/subscriber badge discovery with restricted image proxying
- Twitch native/subscriber emotes
- Kick relay emote support when the standalone Kick backend is configured
- 7TV global and channel emotes
- Compact and comfortable message density
- Five SleepyChat themes with matching raccoon artwork
- Demo chat and transcript export
- Native Windows tray support
- Custom SleepyChat title bar, taskbar identity, dark-window polish, and Windows 11 rounded-corner preference
- Browser back/forward mouse-button protection inside WebView2

## Desktop architecture

SleepyChat 1.0.0 has one active desktop implementation:

- **C# / .NET 8**
- **WinForms** native application shell
- **Microsoft WebView2** embedded UI
- **ASP.NET Core / Kestrel** local backend
- Local service: `http://127.0.0.1:17892/`
- Target: **Windows x64**
- Publish mode: **self-contained single-file executable** plus required UI/assets

SleepyChat is a separate project from SleepySource. It has its own executable, data folder, source tree, branding, features, packaging, and version policy.

## Requirements

For normal published builds:

- Windows 10 or Windows 11, 64-bit
- Microsoft Edge WebView2 Runtime
- Internet access for live Twitch/Kick/7TV features that contact their services

For building from source:

- .NET 8 SDK
- Internet access during restore unless required NuGet packages are already cached

## Build from source

From PowerShell at the repository root:

```powershell
.\BUILD_RELEASE.ps1
```

The script restores and publishes the exact C# project to:

```text
dist\SleepyChat 1.0.0\
```

The build script deliberately rejects unexpected legacy-host source or markers before publishing.

## GitHub validation

`.github/workflows/csharp-runtime-smoke.yml` runs on `windows-latest` and performs the release checks that require a real .NET/Windows environment:

1. Rejects legacy-host source.
2. Checks the public UI for stale prototype wording and validates its JavaScript/manifest syntax.
3. Restores and publishes the C# project as self-contained Windows x64.
4. Verifies the executable version, icon, UI, platform art, and theme assets.
5. Runs `SleepyChat.exe --headless` and probes the local HTTP contracts.
6. Verifies the native close/minimize/tray/window identity source contracts.
7. Uploads the smoke-tested Windows build as the `SleepyChat-1.0.0-Windows-x64` workflow artifact.

## Window behavior

- **X** fully exits SleepyChat.
- **Alt+F4** fully exits SleepyChat.
- Minimize leaves SleepyChat on the Windows taskbar and keeps the tray icon available.
- Double-clicking the tray icon restores the window.
- Tray menu includes **Open SleepyChat**, **Open SleepyChat_Data**, and **Exit SleepyChat**.
- The app starts at **1280 × 820** with a **980 × 680** minimum size.
- External links are opened with the system browser instead of navigating the application UI away from SleepyChat.

## Local data

SleepyChat stores runtime data beside the executable in:

```text
SleepyChat_Data\
```

The WebView2 user-data directory is kept inside `SleepyChat_Data\WebView2\` so the current application owns one predictable portable data location.

Do not commit `SleepyChat_Data`, `bin`, `obj`, or `dist`; the repository `.gitignore` excludes them.

## Kick backend configuration

Normal users do not enter a relay/WebSocket address in the UI. A SleepyChat operator may configure the standalone Kick service with either the internal constants in `CSharpHost/IntegrationConfig.cs` or these environment variables:

```text
SLEEPYCHAT_KICK_AUTH_URL
SLEEPYCHAT_KICK_RELAY_URL
```

Production relay endpoints must use `wss://`. Loopback `ws://127.0.0.1:` is accepted for local development only. If no relay is configured, the app reports the Kick backend as unavailable rather than inventing or exposing a user-configurable relay URL.

## Local service and proxy safety

- Kestrel binds to loopback only at `127.0.0.1:17892`.
- Requests with non-local Host values are rejected.
- Browser-origin state-changing requests are restricted to the local SleepyChat origin.
- 7TV requests only accept supported platform names and numeric IDs.
- Twitch badge channel IDs must be numeric.
- Kick channel slugs are length/character validated.
- Remote JSON responses are size-limited.
- Kick badge image responses are size-limited and must be image content.
- Badge image URLs are restricted to explicitly approved HTTPS hosts, including redirect validation.

## Version policy

The public product version is fixed at **SleepyChat 1.0.0** unless the project owner explicitly changes it. Rebuilds, refactors, cleanup, and hotfixes remain labeled 1.0.0 under that policy.

## Repository note

No software license file is included in this release source. Add the license you want before granting third parties explicit reuse/contribution rights.

Window behavior note: F11 is intentionally not used by SleepyChat. Use the normal maximize/restore control, title-bar double-click, or Windows snap.
