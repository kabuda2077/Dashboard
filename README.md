# Dashboard

A Windows desktop launcher and dashboard for mihomo and sing-box, based on zashboard.

Dashboard uses a WinForms + WebView2 host to run a locally bundled zashboard UI, while the desktop host manages the proxy core process, tray behavior, startup settings, and Windows integration.

## Features

- Bundled zashboard UI with desktop-specific integration.
- Single active core model: choose either `mihomo` or `sing-box`.
- Independent core path, config path, API URL, and Secret for each core type.
- Start, stop, restart, switch, and inspect the active core from the Core page.
- Show core PID, running state, stdout/stderr logs, and recent active downloads.
- Upgrade `mihomo` from MetaCubeX releases.
- Upgrade `sing-box` from the reF1nd `sing-box-releases` Windows amd64v3 build.
- Use Clash-compatible API as the main UI channel for both `mihomo` and `sing-box`.
- Tray menu for showing the window, restarting/stopping the core, and exiting.
- Minimize-to-tray and lightweight mode for WebView lifecycle control.
- Per-user autostart via `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
- Settings stored in `%LOCALAPPDATA%\Dashboard\settings.json`.
- Secrets protected with Windows DPAPI.
- UI style constraints documented in `STYLE.md`.
- Upstream zashboard merge workflow documented in `UPSTREAM_MERGE.md`.

## Project Layout

- `src/`: Windows desktop host source code.
- `dashboard-src/`: zashboard-based frontend source with Dashboard-specific changes.
- `resources/dashboard/`: built frontend assets embedded into the desktop app.
- `resources/app.ico`: desktop app icon.
- `resources/tray.ico`: tray icon.
- `tools/build-zashboard.ps1`: builds `dashboard-src` and syncs it into `resources/dashboard`.
- `build.ps1`: builds the frontend and publishes the .NET desktop app.
- `create-release.ps1`: creates a ZIP release package from a fresh publish.
- `STYLE.md`: visual style rules used by the customized UI.
- `UPSTREAM_MERGE.md`: rules for following upstream zashboard updates.

## Requirements

Runtime:

- Windows 10/11
- .NET 9 Desktop Runtime
- Microsoft Edge WebView2 Runtime
- A configured `mihomo` or `sing-box` executable

Development:

- .NET 9 SDK
- Node.js 24
- pnpm 10.15.0

Install pnpm if needed:

```powershell
npm install -g pnpm@10.15.0
```

## Core Setup

Dashboard does not bundle proxy cores by default. Select the executable and config file from the Core page.

Recommended portable layout:

```text
Dashboard.exe
mihomo/
  mihomo.exe
  config.yaml
sing-box/
  sing-box.exe
  config.json
```

Default paths currently point to:

```text
E:\APP\Dashboard\mihomo\mihomo.exe
E:\APP\Dashboard\mihomo\config.yaml
E:\APP\Dashboard\sing-box\sing-box.exe
E:\APP\Dashboard\sing-box\config.json
```

`mihomo` should expose an external controller, for example:

```yaml
external-controller: 127.0.0.1:9090
secret: ""
```

`sing-box` should expose a Clash-compatible external controller. Dashboard uses this Clash API path for overview, proxies, rules, connections, logs, configuration reload, and related actions. sing-box native API / Tools integration is intentionally not included.

## Build

Build and publish the desktop app:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Configuration Release -Runtime win-x64
```

Output:

```text
artifacts\publish\Dashboard-Release-win-x64
```

Copy the whole output directory when installing or replacing an existing portable installation. Do not copy only `Dashboard.exe`.

## Create A Release Package

Create a ZIP package:

```powershell
powershell -ExecutionPolicy Bypass -File .\create-release.ps1 -OutputZip Dashboard-v1.0.0-win-x64.zip
```

Output:

```text
artifacts\releases\Dashboard-v1.0.0-win-x64.zip
artifacts\releases\RELEASE_NOTES_yyyyMMdd.txt
```

## Standard Release Flow

Use this flow for future releases:

1. Start from `main`.
2. Create a release branch, for example `codex/release-v1.1.0`.
3. Make code changes and update `README.md`, `STYLE.md`, or `UPSTREAM_MERGE.md` if behavior or workflow changed.
4. Run verification:

```powershell
cd dashboard-src
cmd /c npm run type-check
cmd /c npm run build
cd ..
dotnet build -c Release
```

5. Create the release ZIP:

```powershell
powershell -ExecutionPolicy Bypass -File .\create-release.ps1 -OutputZip Dashboard-v1.1.0-win-x64.zip
```

6. Smoke test the package from `artifacts\publish\Dashboard-Release-win-x64`.
7. Commit the final source and generated `resources/dashboard` assets.
8. Merge the release branch into `main`.
9. Tag `main` with the release version, for example `v1.1.0`.
10. Push `main` and the tag.
11. Create a GitHub Release and upload the ZIP from `artifacts\releases`.

Version tags should use semantic versioning: `vMAJOR.MINOR.PATCH`.

## Upstream zashboard

Dashboard is based on zashboard, but it is not a direct mirror. The frontend keeps zashboard's main dashboard experience while adding desktop-host integration and removing features that are not used by this app.

When following upstream:

- Always work on a new branch.
- Review upstream commits, not only changelog entries.
- Preserve Dashboard-specific host messaging, Core page behavior, window controls, visual style rules, and release packaging.
- Use `UPSTREAM_MERGE.md` as the checklist.

## Notes

- `resources/dashboard` is generated from `dashboard-src`; rebuild it after frontend changes.
- `dashboard-src/dist`, `bin`, `obj`, and `artifacts/publish` are generated outputs.
- The portable package is self-contained for the Dashboard app files, but it still requires WebView2 Runtime and the chosen proxy core executable.
