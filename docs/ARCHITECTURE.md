# SleepyChat 1.0.0 Architecture

## Desktop process

SleepyChat runs as one Windows process. WinForms owns the native window and tray. An in-process Kestrel server exposes the local application UI/API only on loopback. WebView2 renders that local UI.

## Source responsibilities

- `CSharpHost/App` — process startup, single-instance behavior, app paths, DPAPI utilities.
- `CSharpHost/Window` — borderless window, title bar, manual resizing, lifecycle, tray, WebView2 setup.
- `CSharpHost/Backend` — local HTTP host, request restrictions, static files, badge/emote proxy endpoints.
- `CSharpHost/Kick` — hosted OAuth connection, connection state, realtime delivery, polling fallback, chat send, encrypted local credential storage.
- `CSharpHost/Updates` — GitHub Releases version check and safe repository/release launching.
- `CSharpHost/web` — markup, CSS, and JavaScript for the UI.
- `CSharpHost/assets` — image/icon assets only.

## Local boundary

The local service binds to `127.0.0.1:17892`. State-changing browser requests are limited to the local SleepyChat origin. Remote media proxy routes validate IDs/hosts and enforce response size limits.

## Hosted Kick boundary

The desktop never stores the Kick developer Client Secret and never receives the raw Kick OAuth access token. The hosted service returns an opaque connection credential, which SleepyChat protects with Windows DPAPI before local storage.
