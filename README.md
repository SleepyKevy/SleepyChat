# SleepyChat 1.0.0

**Made by SleepyKev • 2026**

SleepyChat is a standalone Windows x64 unified Kick + Twitch chat application built with C#/.NET 8, WinForms, WebView2, and an in-process ASP.NET Core/Kestrel loopback service.

## What it does

- Unified **All / Kick / Twitch / Mentions** chat views
- **Connect with Kick** OAuth flow through the hosted shared API
- Kick chat receiving and sending (`chat:write`)
- Twitch read-only chat by channel
- Twitch/Kick badges and 7TV support
- Search, role filters, mention highlighting, density controls, export, and five themes
- Theme-matched SleepyChat raccoon branding for blue, red, purple, green, and pink themes
- GitHub Releases update checker
- Custom dark title bar, tray support, and manual edge/corner resizing

SleepyChat is its own standalone application. It does not require SleepySource to run.

## Current architecture

- **C# / .NET 8** native host
- **WinForms** window, tray, resizing, and lifecycle
- **WebView2** desktop UI
- **ASP.NET Core / Kestrel** local service at `http://127.0.0.1:17892/`
- **Hosted Kick OAuth/API** at `https://sleepysource-api.sleepyservices.workers.dev`
- **Windows x64**, self-contained single-file publish

The hosted Kick OAuth/API is shared infrastructure. Its deployment source is intentionally not bundled into this SleepyChat source package.

## Source layout

```text
CSharpHost/
  App/                  application startup + shared utilities
  Backend/              local Kestrel host + safe media proxy routes
  Kick/                 hosted Kick OAuth, delivery, storage, models
  Updates/              GitHub Releases update checker
  Window/               WinForms shell, resize, WebView2, caption controls
  assets/               application + theme branding assets
  web/
    css/                 UI styles
    js/                  UI application logic
    index.html           UI markup
    manifest.webmanifest

docs/
  ARCHITECTURE.md
  CODE_AUDIT.md
  RELEASE_AUDIT.md
  SHARED_KICK_BACKEND.md
```

## Build from source

Requirements:

- Windows 10/11 x64
- .NET 8 SDK
- Microsoft WebView2 Runtime for normal GUI use

From PowerShell in the repository root:

```powershell
.\BUILD_RELEASE.ps1
```

The clean Windows x64 publish is created at:

```text
dist\SleepyChat 1.0.0\
```

To build and create the Public + Source ZIPs in one pass:

```powershell
.\PACKAGE_RELEASE.ps1
```

The release scripts validate the current source layout, version metadata, hosted Kick contracts, JavaScript syntax, required runtime files, application icon assets, and all five theme logo assets before packaging.

## Local data

Runtime data is stored beside the executable in:

```text
SleepyChat_Data\
```

The opaque hosted Kick connection credential is protected with Windows DPAPI before being written locally.

## Version policy

The public product version remains **SleepyChat 1.0.0** unless explicitly changed by the project owner. Rebuilds, refactors, and hotfixes remain labeled 1.0.0.

## License

No software license file is included. Add the license you want before granting third parties explicit reuse or contribution rights.
