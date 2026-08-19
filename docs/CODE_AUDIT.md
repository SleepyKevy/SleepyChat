# SleepyChat 1.0.0 Code Audit

Audit scope: current C# desktop source, WebView UI, build script, validation workflow, packaged documentation, and bundled assets.

## Findings corrected in this cleanup

1. **Oversized mixed-responsibility source files**
   - `MainForm.cs`, `HostedKickService.cs`, and `BackendHost.cs` had unrelated responsibilities combined in single files.
   - Refactored into focused partial files/folders without changing namespaces or public runtime contracts.

2. **Monolithic web bundle**
   - UI CSS and JavaScript were embedded in `index.html`.
   - Extracted to `web/css/app.css`, `web/css/ui-polish.css`, and `web/js/app.js`.
   - `index.html` now contains markup only plus external style/script references.

3. **Stale backend deployment material**
   - The SleepyChat source ZIP still bundled a shared Cloudflare Worker package and documentation containing superseded route/setup guidance.
   - Removed from SleepyChat source. Replaced with `docs/SHARED_KICK_BACKEND.md` describing the current hosted contract.

4. **Stale release validation**
   - `BUILD_RELEASE.ps1` and the old smoke workflow still checked obsolete `/kick/events/ensure-chat`, old Worker files, and native frame markers that no longer represent the working app.
   - Replaced with checks for the current `/kick/events/sync`, `/kick/chat/send`, logic-only resize system, update checker, external web bundle, and current folder layout.

5. **Unused assets**
   - Removed unused `sleepychat-mark.webp`, `app-64.png`, and duplicate `app-192.png` from `assets`.
   - Required favicon/manifest and application icon sizes remain.

## Security / safety review

- No Kick Client Secret or raw OAuth access token is embedded in the desktop source.
- Local Kestrel binding remains loopback-only.
- State-changing browser requests remain restricted to the local app origin.
- Hosted connection credentials remain protected with Windows DPAPI.
- Media proxy routes retain host validation and response-size limits.
- External navigation remains handed to the system browser instead of replacing the app UI.
- No native `WS_THICKFRAME`/`CreateParams` title-bar workaround is reintroduced.

## Intentionally unchanged behavior

- Public version remains 1.0.0.
- Kick OAuth/send architecture remains hosted and shared.
- Manual resizing remains logic-only.
- Update checker behavior remains GitHub Releases based.
- UI themes, chat rendering, badge/emote behavior, and local port remain unchanged.
