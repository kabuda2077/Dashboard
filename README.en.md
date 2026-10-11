# Dashboard

[简体中文](README.md) | [English](README.en.md)

A Windows desktop proxy dashboard based on [zashboard](https://github.com/Zephyruso/zashboard). WinForms and WebView2 host the local UI; C# owns core processes, tray/window behavior, autostart, credentials and upgrades. Panel data uses the Clash-compatible API.

## Version 2.x format

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
- Current-user Task Scheduler registration `\Dashboard\Autostart`, approximately five seconds after logon. Writes/deletes verify the user, executable and working directory; foreign or unverifiable same-name tasks are not overwritten. No legacy registry startup migration. Exit waits for an accepted autostart change and its saved result, so slow system task operations can delay exit.
- mihomo upgrades use the running core's `/upgrade`. Built-in sing-box upgrades select only reF1nd Windows amd64v3 builds. Users who want to keep official/other builds should update them manually.
- Published SHA256 digests are verified. Missing digests require explicit confirmation before candidate execution/replacement. Download, extraction, validation and atomic replacement have defined limits and recovery. Upgrading a running sing-box requires the new process's API to become ready within the bounded probe before reporting success; startup/probe failures restore the original file and attempt to restart the original core.
- Dashboard updates check Releases and open the download page; they do not replace Dashboard.exe automatically.
- The unreliable TUN sleep-resume auto-restart workaround is withdrawn. Automatic recovery needs a separately implemented and validated replacement; this version does not claim to solve it.
- No desktop native sing-box API, Tools or Terminal. Browser Clash preview remains available without native privileges.
- The UI uses `http://127.0.0.1:33291/`; port conflicts are explicit. Secure virtual-host mapping blocks supported remote plaintext APIs, so this version does not disable browser security to work around it.

For same-format updates, fully exit and overlay application files; do not delete the directory. Packages exclude settings, profile data, cores and logs. Logs are under `resources/logs`; `--diagnostic-log` enables detailed diagnostics. Both the queue and rotated files are bounded.

## Development

Requires **PowerShell 7** (`pwsh`), .NET 9 SDK and a working pnpm launcher. The exact Node **24** version is declared in `.node-version`; pnpm **11.20.0** is declared by `packageManager` in `dashboard-src/package.json`. Scripts select/check tools from the frontend directory. If the ambient Node differs, pnpm resolves the declared runtime through its shared cache without changing global Node or creating a project-local toolchain. Installation keeps `--frozen-lockfile` and respects normal pnpm store/cache configuration rather than overriding it. Resolved versions, executable paths and store are printed.

Build and test scripts support PowerShell 7 only. The packaged Dashboard does not require PowerShell. Choose one application entry point for your goal; do not run a full Check immediately before Release:

```powershell
# Prepare dependencies, build, check types, run .NET/wire/frontend tests
pwsh -NoProfile -ExecutionPolicy Bypass -File .\tools\check.ps1

# Verify this invocation's inputs, publish, inspect resources and create ZIP
pwsh -NoProfile -ExecutionPolicy Bypass -File .\tools\create-release.ps1
```

`artifacts/releases/` contains deliverable ZIPs only. Input manifests and verification summaries go to `artifacts/verification/<package-name>/`; do not install these records. Reuse the same ZIP name for rebuilds and place historical candidates in `artifacts/archive/`.

Project-owned test data goes to `.tmp/tests/<run-id>/<case-id>/`; investigations and clean validation copies go to `.tmp/experiments/`; logs, screenshots and wire fixtures go to `.tmp/reports/`. Shared helpers release owned browser/process resources before bounded cleanup retries and report failed cleanup paths. Production system-TEMP usage, same-volume core staging and backups remain unchanged; scripts do not change global TEMP/TMP.

During edits, run relevant tests and types; select the Node version from `.node-version` before invoking frontend commands manually. Add targeted WebView checks for UI changes. Once changes settle, run Release once. `check.ps1` defaults to `-Scope Application`. For tooling/document changes, run `pwsh -NoProfile -File .\tools\check.ps1 -Scope Maintenance`; use `-Scope All` for both. CI selects application and maintenance scopes separately; ordinary application checks do not repeat maintenance checks.

Use `-IncludeWebViewIntegration` for actual WebView/production-window tests, including idle disposal, cancellation and reopening. The test checks the production 60-second default, then accelerates the real timer instead of waiting a minute. Explicit isolated real-core testing:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .\tests\scripts\PrepareValidationCores.ps1
$env:DASHBOARD_TEST_CORES_DIR = (Resolve-Path .tmp/tests/fixtures/validation-cores).Path
pwsh -NoProfile -ExecutionPolicy Bypass -File .\tools\check.ps1 -IncludeWebViewIntegration -IncludeRealCoreIntegration
```

For a formal candidate, run `create-release.ps1 -IncludeWebViewIntegration -IncludeRealCoreIntegration` with the prepared test cores. Do not first repeat Check; `-IncludeSlowIntegration` is no longer needed. This one entry builds, tests and packages the candidate, reporting elapsed time for each stage.

The preparation script downloads digest-verified test cores only. Tests use temporary data and configurations without TUN or proxy listeners, and never take over user core processes.

Add `-IncludePerformanceIntegration` to the complete check above to run a fixed workload in a separate testhost with real WebView (three rounds, 100/1,000/10,000 synthetic connections). The report is `.tmp/reports/performance-v2/desktop.json`; totals include the harness and are not physical-display FPS or pure Dashboard.exe memory. Local calculation benchmarks run from `dashboard-src/` with `node.exe node_modules/vitest/vitest.mjs run --config bench/vitest.config.ts`.

`tools/internal/` contains reusable implementation steps, not additional manual workflows. UI files are generated from `dashboard-src/dist/` into untracked `resources/dashboard/`. .NET rejects missing UI resources. Frontend wire tests consume `.tmp/reports/bridge-fixtures-v2/` generated by current .NET tests; stale fixtures are not validation.

A successful check or ZIP does not imply all manual Windows scenarios passed; see [system validation boundaries](docs/maintenance.md#system-boundaries). Keep Version, InformationalVersion and the eventual Release tag aligned.

## Maintenance records

Enduring rules live in the single [maintenance guide](docs/maintenance.md) (Chinese). Use `tools/review-upstream.ps1` for the plan, implementation result and baseline progression; exact baselines, paths and ongoing issues belong to the sole register `docs/upstream/ownership.json`. Record validation does not prove behavior passed or authorize a release.

- [Product/Core workflow](docs/maintenance.md#desktop-shell) and [configuration/persistence](docs/maintenance.md#preferences).
- [Visual rules and accepted UI choices](docs/maintenance.md#desktop-visuals).
- [Current upstream decisions and workflow](docs/maintenance.md#local-tooling).
- [Validation/artifacts](docs/maintenance.md#verification) and [system boundaries](docs/maintenance.md#system-boundaries).

MIT License. Upstream frontend copyright is retained in `dashboard-src/LICENSE`.
