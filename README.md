# SleepyChat 1.0.0

**Made by SleepyKev • 2026**

SleepyChat is a standalone Windows x64 unified Kick + Twitch chat application built with C#/.NET 8, WinForms, WebView2, and a loopback ASP.NET Core/Kestrel backend.

## Current architecture

- **C# / .NET 8** native host
- **WinForms** window, tray, resizing, and lifecycle
- **WebView2** desktop UI
- **ASP.NET Core / Kestrel** local service at `http://127.0.0.1:17892/`
- **Hosted Kick OAuth/API** at `https://sleepysource-api.sleepyservices.workers.dev`
- **Windows x64**, self-contained single-file publish

SleepyChat remains its own standalone project. The hosted Kick OAuth/API is shared infrastructure; its deployment source is intentionally not bundled into this SleepyChat source package.

## Source layout

```text
CSharpHost/
  App/                  application startup + shared utilities
  Backend/              local Kestrel host + safe media proxy routes
  Kick/                 hosted Kick OAuth, delivery, storage, models
  Updates/              GitHub Releases update checker
  Window/               WinForms shell, resize, WebView2, caption controls
  assets/               image/icon assets only
  web/
    css/                 UI styles
    js/                  UI application logic
    index.html           UI markup
    manifest.webmanifest

docs/
  ARCHITECTURE.md
  CODE_AUDIT.md
  SHARED_KICK_BACKEND.md
```

The refactor deliberately keeps namespaces and runtime contracts unchanged while splitting previously oversized/mixed-responsibility files.

## Main features

- Unified All / Kick / Twitch / Mentions chat views
- Hosted Connect with Kick OAuth
- Kick message receiving and sending (`chat:write`)
- Twitch read-only chat by channel
- Twitch/Kick badges and 7TV support
- Search, role filters, mention highlighting, themes, density, export
- GitHub Releases update checker
- Custom dark title bar and tray support
- Manual edge/corner resizing without restoring the native Windows frame
- WebView2 autofill/password-save prompts disabled

## Build from source

Requirements:

- Windows 10/11 x64
- .NET 8 SDK
- WebView2 Runtime for normal GUI use

From PowerShell in the repository root:

```powershell
.\BUILD_RELEASE.ps1
```

Output:

```text
dist\SleepyChat 1.0.0\
```

The build script validates the current source layout and hosted Kick contracts before publishing.

## Window behavior

- Starts at **1280 × 820**
- Minimum size: **980 × 680**
- Drag any edge or corner to resize
- Custom title bar remains borderless/dark
- Minimize/maximize/restore behave normally
- X and Alt+F4 fully exit
- Tray icon can restore or exit the app

## Local data

Runtime data is stored beside the executable in:

```text
SleepyChat_Data\
```

The opaque hosted Kick connection credential is protected with Windows DPAPI before being written locally.

## Shared Kick backend

End users do not enter a Kick Client ID, Client Secret, token, relay URL, webhook URL, or Cloudflare configuration.

SleepyChat uses the shared hosted API and only stores an opaque connection ID/token locally. See `docs/SHARED_KICK_BACKEND.md` for the contract used by the desktop app.

## Version policy

The public product version remains **SleepyChat 1.0.0** unless explicitly changed by the project owner. Refactors, rebuilds, and hotfixes remain labeled 1.0.0.

## License

No software license file is included. Add the license you want before granting third parties explicit reuse or contribution rights.
