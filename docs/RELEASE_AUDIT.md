# SleepyChat 1.0.0 Release Audit

Prepared on 2026-08-19 for the official GitHub `v1.0.0` release.

## Source checks completed

- Public product version remains `1.0.0` throughout the project and packaging scripts.
- The desktop project is C#/.NET only; no stale Go source or bundled Cloudflare Worker deployment source is included.
- No Kick Client Secret, raw OAuth access token, API key, or similar developer secret is embedded in the desktop source tree.
- The shared Kick API contract remains pointed at `https://sleepysource-api.sleepyservices.workers.dev`.
- The JavaScript bundle is syntax-checked and the web manifest is parsed during the release build.
- The corrected Windows application icon contains dedicated 16, 32, 48, 64, and 128 pixel frames.
- The five supplied raccoon artworks are mapped to the matching blue, red, purple, green, and pink themes. Runtime PNG copies are optimized for the 46-pixel desktop UI logo while preserving each supplied theme colorway.
- Source packaging excludes generated/runtime junk such as `.vs`, `bin`, `obj`, `dist`, `SleepyChat_Data`, PDBs, logs, and temporary files.

## Canonical release build

The official GitHub release is built on a Windows GitHub Actions runner from the exact final release-source commit using `PACKAGE_RELEASE.ps1`. The pipeline performs the clean Windows x64 Release publish, validates the expected runtime files and version metadata, starts the newly built executable in headless mode for a local backend smoke test, packages the Public and Source ZIPs, and only then publishes the `v1.0.0` GitHub Release.
