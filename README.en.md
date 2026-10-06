# Dashboard

[简体中文](README.md) | [English](README.en.md)

A Windows desktop proxy dashboard based on [zashboard](https://github.com/Zephyruso/zashboard). WinForms and WebView2 host the local UI; C# owns core processes, tray/window behavior, autostart, credentials and upgrades. Panel data uses the Clash-compatible API.

## Version 2 format

**2.0 is a breaking update. It uses schemaVersion=2 settings.json and `resources/webview-data-v2/`. There is no migration of old settings, history, backgrounds, plaintext secrets or startup registrations.**

Extract into a new directory and configure the core paths and API again. Old or corrupt settings produce an explicit error, not an automatic overwrite. Existing v2 documents must retain all serialized root/profile/desktop-option fields; missing fields are not silently filled with defaults. Preserve the old directory and do not delete your only copy.

Same-version view recreation, application restarts and frontend resource replacements preserve browser data. A changed application version or missing valid version marker resets only v2's `resources/webview-data-v2/EBWebView` before browser startup, then records the plain version. The separate 1.x `resources/EBWebView` is untouched. Browser-local history and uploaded backgrounds are cleared; settings.json, cores, configuration, icon-cache and logs are retained, and host-saved preferences are restored. Failure blocks browser startup without advancing the marker. Transitioning from the old cache policy also resets once.

Secrets are protected with current-user Windows DPAPI. If a different user cannot decrypt a credential, explicitly replace it in Core, including when the intended replacement is empty. Ordinary saves retain unreadable ciphertext.

## Requirements and setup

- Windows x64, [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0).
- [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/).
- Your own mihomo or sing-box executable and configuration. sing-box must include Clash API support.

```text
Dashboard/
  Dashboard.exe
  mihomo/mihomo.exe
  mihomo/config.yaml
  sing-box/sing-box.exe
  sing-box/config.json
```

Select a profile in Core, choose its executable/configuration and enter its API address and Secret. File selection edits the draft without saving it. Desktop option changes do not commit unrelated core drafts. UAC is requested when starting/restarting a core requires elevation.

Enable mihomo `external-controller`, or sing-box `experimental.clash_api.external_controller`. A typical URL is `http://127.0.0.1:9090`. Process existence and API readiness are separate states; correct the address/credential and retry if necessary.

## Capabilities and limits

- One active core and two independent profiles, start/stop/restart/switch, PID and output logs.
- Embedded Core settings, proxies, connections, overview, rules, logs, themes, labels, latency testing, backgrounds, preference import/export and current-format history.
- Native window actions and tray. Lightweight mode releases the view after 60 seconds hidden; reopening cancels disposal.
- Preferences, history and the full image read/decode/commit workflow are awaited before view suspension/disposal. Unconfirmed exit saves produce feedback. Elevation is requested only after flush/exit consent; a cancelled or failed replacement keeps the old host alive.
- Current-user Task Scheduler registration `\Dashboard\Autostart`, approximately five seconds after logon. Writes/deletes verify the user, executable and working directory; foreign or unverifiable same-name tasks are not overwritten. No legacy registry startup migration.
- mihomo upgrades use the running core's `/upgrade`. Built-in sing-box upgrades select only reF1nd Windows amd64v3 builds. Users who want to keep official/other builds should update them manually.
- Published SHA256 digests are verified. Missing digests require explicit confirmation before candidate execution/replacement. Download, extraction, validation and atomic replacement have defined limits and recovery.
- Dashboard updates check Releases and open the download page; they do not replace Dashboard.exe automatically.
- The unreliable TUN sleep-resume auto-restart workaround is withdrawn. Automatic recovery needs a separately implemented and validated replacement; this version does not claim to solve it.
- No desktop native sing-box API, Tools or Terminal. Browser Clash preview remains available without native privileges.
- The UI uses `http://127.0.0.1:33291/`; port conflicts are explicit. Secure virtual-host mapping blocks supported remote plaintext APIs, so this version does not disable browser security to work around it.

For same-format updates, fully exit and overlay application files; do not delete the directory. Packages exclude settings, profile data, cores and logs. Logs are under `resources/logs`; `--diagnostic-log` enables detailed diagnostics. Both the queue and rotated files are bounded.

## Development

Requires **PowerShell 7** (`pwsh`), .NET 9 SDK, Node.js **24**, pnpm **11.20.0**. Build and test scripts support PowerShell 7 only. The packaged Dashboard does not require PowerShell. Choose one application entry point for your goal; do not run a full Check immediately before Release:

```powershell
# Prepare dependencies, build, check types, run .NET/wire/frontend tests
pwsh -NoProfile -ExecutionPolicy Bypass -File .\tools\check.ps1

# Verify this invocation's inputs, publish, inspect resources and create ZIP
pwsh -NoProfile -ExecutionPolicy Bypass -File .\tools\create-release.ps1
```

`artifacts/releases/` contains deliverable ZIPs only. Input manifests and verification summaries go to `artifacts/verification/<package-name>/`; do not install these records. Reuse the same ZIP name for rebuilds and place historical candidates in `artifacts/archive/`.

During edits, run relevant tests and types; add targeted WebView checks for UI changes. Once changes settle, run Release once. Run `pwsh -NoProfile -File .\tools\check-maintenance.ps1` for tooling/document changes. CI runs maintenance checks and the regular Check; application builds do not repeat maintenance checks.

Use `-IncludeWebViewIntegration` for actual WebView/production-window tests. The real 60-second idle-disposal test requires the additional `-IncludeSlowIntegration` switch (which requires WebView). Explicit isolated real-core testing:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .\tests\scripts\PrepareValidationCores.ps1
$env:DASHBOARD_TEST_CORES_DIR = (Resolve-Path .tmp/validation-cores).Path
pwsh -NoProfile -ExecutionPolicy Bypass -File .\tools\check.ps1 -IncludeWebViewIntegration -IncludeRealCoreIntegration
```

For final delivery, pass the same integration switches directly to `create-release.ps1`; add `-IncludeSlowIntegration` when validating the full lifecycle. Do not first repeat Check.

The preparation script downloads digest-verified test cores only. Tests use temporary data and configurations without TUN or proxy listeners, and never take over user core processes.

Add `-IncludePerformanceIntegration` to the complete check above to run a fixed workload in a separate testhost with real WebView (three rounds, 100/1,000/10,000 synthetic connections). The report is `.tmp/performance-v2/desktop.json`; totals include the harness and are not physical-display FPS or pure Dashboard.exe memory. Local calculation benchmarks run from `dashboard-src/` with `node.exe node_modules/vitest/vitest.mjs run --config bench/vitest.config.ts`.

`tools/internal/` contains reusable implementation steps, not additional manual workflows. UI files are generated from `dashboard-src/dist/` into untracked `resources/dashboard/`. .NET rejects missing UI resources. Frontend wire tests consume `.tmp/bridge-fixtures-v2/` generated by current .NET tests; stale fixtures are not validation.

A successful check or ZIP does not imply all manual Windows scenarios passed. Keep Version, InformationalVersion and the eventual Release tag aligned.

## Maintenance records

- [Maintenance](docs/maintenance.md): current configuration contracts, validation/artifact rules and remaining acceptance boundaries, not an implementation diary (Chinese).
- [Upstream guide](docs/upstream-merge.md): the sole upstream workflow; preserve product guarantees while allowing better implementations.
- [UI rules](docs/style.md): visual principles and current defaults.
- [Upstream baseline](docs/upstream/v3.26.0.md): exact revision and retained decisions for the next merge (Chinese).

MIT License. Upstream frontend copyright is retained in `dashboard-src/LICENSE`.
