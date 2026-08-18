SLEEPYCHAT 1.0.0
Made by SleepyKev • 2026

OVERVIEW
========
SleepyChat is a standalone Windows x64 desktop chat viewer for Kick and Twitch.
The 1.0.0 source has one active desktop architecture:

- C# / .NET 8
- WinForms native shell
- Microsoft WebView2 UI
- ASP.NET Core / Kestrel local backend at 127.0.0.1:17892
- Self-contained single-file Windows x64 publish target

SleepyChat remains completely separate from SleepySource. It has its own source,
executable, data, branding, features, release packaging, and version policy.

FEATURES
========
- Unified All / Kick / Twitch / Mentions views
- Search and role filtering
- Twitch and Kick username colors
- Twitch global/channel badge discovery
- Kick role/subscriber badge discovery with restricted image proxying
- Twitch native/subscriber emotes
- Kick relay emotes when the standalone Kick backend is configured
- 7TV global/channel emotes
- Comfortable and compact message density
- Five SleepyChat themes with matching raccoon artwork
- Demo messages and transcript export
- Platform icons and final favicon/app icon set

WINDOW BEHAVIOR
===============
- X fully exits SleepyChat.
- Alt+F4 fully exits SleepyChat.
- Minimize keeps SleepyChat on the taskbar and keeps the tray icon available.
- Tray double-click restores the window.
- Tray menu: Open SleepyChat / Open SleepyChat_Data / Exit SleepyChat.
- Startup size: 1280 x 820.
- Minimum size: 980 x 680.
- SleepyChat uses its own compact native title bar and Windows application identity.
- Browser back/forward mouse buttons are blocked from navigating WebView2 history.
- External links open in the system browser.

REQUIREMENTS
============
Published app:
- Windows 10/11 x64
- Microsoft Edge WebView2 Runtime
- Internet access for live online chat/emote/badge features

Source build:
- .NET 8 SDK
- Internet access for package restore unless dependencies are already cached

BUILDING FROM SOURCE
====================
From PowerShell in the source root:

  .\BUILD_RELEASE.ps1

Output:

  dist\SleepyChat 1.0.0\

The release script refuses to publish if unexpected legacy-host source/markers are
present. The GitHub Actions workflow performs the real Windows compile and runtime
smoke test on every push to main and on pull requests.

DATA
====
Runtime data is stored beside SleepyChat.exe:

  SleepyChat_Data\

WebView2 user data is stored at:

  SleepyChat_Data\WebView2\

The repository .gitignore excludes local runtime/build output.

KICK BACKEND CONFIGURATION
==========================
Normal users do not enter relay/WebSocket URLs. The app operator can configure:

  SLEEPYCHAT_KICK_AUTH_URL
  SLEEPYCHAT_KICK_RELAY_URL

or set the internal constants in CSharpHost\IntegrationConfig.cs before building.
Production relay endpoints require wss://. Local development may use loopback
ws://127.0.0.1: endpoints. If no relay is configured, SleepyChat reports the Kick
backend as unavailable.

SECURITY / LOCAL SERVICE
========================
- Local backend binds only to 127.0.0.1:17892.
- Non-local Host values are rejected.
- Browser-origin state-changing requests are local-origin restricted.
- 7TV IDs and Twitch badge channel IDs are numeric-validated.
- Kick channel slugs are character/length validated.
- Remote JSON and badge image responses are size-limited.
- Badge image proxying accepts only explicitly approved HTTPS hosts and validates
  every redirect before following it.
- Badge proxy responses must have image content types.

VERSION
=======
Public product version: SleepyChat 1.0.0.
The version remains 1.0.0 unless the project owner explicitly requests a change.

GITHUB NOTE
===========
README.md is included for the repository front page. A Windows GitHub Actions smoke
workflow compiles and uploads the real win-x64 artifact. No software license file
is included; select one separately if third-party reuse/contributions are intended.

Window behavior note: F11 is intentionally not used by SleepyChat. Use the normal maximize/restore control, title-bar double-click, or Windows snap.
