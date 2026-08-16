# Dashboard

[简体中文](README.md) | [English](README.en.md)

A Windows desktop launcher and dashboard for mihomo and sing-box, based on [zashboard](https://github.com/Zephyruso/zashboard).

Dashboard bundles a local zashboard UI in a WinForms + WebView2 desktop shell. The desktop host manages the proxy core process, tray behavior, startup settings, window controls, and local settings.

This project keeps zashboard's main dashboard experience and adds a Windows desktop shell, core start/stop/switch controls, tray and window controls, portable local settings, mihomo / sing-box Clash-compatible API integration, and a few UI adjustments for desktop use.

## Download

Download the latest ZIP from [GitHub Releases](https://github.com/kabuda2077/Dashboard/releases).

The portable package contains Dashboard app files only. You still need:

- Windows 10/11
- [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)
- A configured `mihomo` or `sing-box` executable

## Quick Start

1. Extract the ZIP to a folder such as:

```text
Dashboard/
  Dashboard.exe
  mihomo/
    mihomo.exe
    config.yaml
  sing-box/
    sing-box.exe
    config.json
```

2. Open `Dashboard.exe`, then choose your core executable and config file on the Core page.
3. Make sure the selected core exposes a Clash-compatible API, then click Start.

Default paths are relative to the Dashboard folder:

```text
.\mihomo\mihomo.exe
.\mihomo\config.yaml
.\sing-box\sing-box.exe
.\sing-box\config.json
```

## Core Configuration

For `mihomo`, enable `external-controller` in `config.yaml`:

```yaml
external-controller: 127.0.0.1:9090
secret: ""
```

For `sing-box`, enable its Clash-compatible API. Dashboard uses the Clash API path for overview, proxies, rules, connections, logs, config reload, and related pages. sing-box native API / Tools integration is intentionally not included.

Example `sing-box` config fragment:

```json
{
  "experimental": {
    "clash_api": {
      "external_controller": "127.0.0.1:9090",
      "secret": ""
    }
  }
}
```

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
- Per-user autostart through the `\Dashboard\Autostart` scheduled task, delayed 5 seconds after logon and run at the highest available privilege.
- Settings stored next to the portable app in `settings.json`.
- Secrets protected with Windows DPAPI.

## FAQ

**Dashboard does not start and Windows says .NET is missing.**  
Install the .NET 9 Desktop Runtime, then reopen Dashboard.

**Dashboard opens a WebView2 Runtime prompt.**  
Install Microsoft Edge WebView2 Runtime from the prompt link, then reopen Dashboard.

**The core starts but Dashboard cannot connect to the API.**  
Check that the API address on the Core page matches your core config. For most users this is `http://127.0.0.1:9090`. If your config has a non-empty `secret`, enter the same value on the Core page.

**TUN mode fails or asks for administrator permission.**  
TUN usually needs administrator permission on Windows. Start Dashboard as administrator or allow the UAC relaunch prompt.

**Why does enabling autostart show UAC once?**
Dashboard creates and verifies a highest-privilege scheduled task. Later logons do not prompt again: after about 5 seconds only the tray host, core manager, and local server start; WebView2 is created only when the window is opened.

**Do I need to configure autostart again after moving the portable folder?**
No. The next manual launch detects a stale task path and requests a repair. Legacy registry startup entries are kept until task verification succeeds, and disabling autostart removes both the scheduled task and legacy entries.

**Can I use only sing-box native API?**  
No. This desktop build uses sing-box's Clash-compatible API for the main dashboard pages.

**Where are logs stored, and how can I temporarily enable detailed diagnostics?**

Basic logs are stored under `resources\logs` next to the app. Release builds record only key operations, errors, and WebView cold-restore summaries by default. Start the app with `Dashboard.exe --diagnostic-log` to additionally record window lifecycle, tray timing, frontend startup, and first WebSocket messages for that session. Debug builds enable detailed diagnostics by default. Each log rotates after 2 MB, with the latest 3 archives retained.

## Development

Development requires .NET 9 SDK, Node.js 24, and pnpm 10.15.0.

```powershell
pnpm --dir dashboard-src type-check
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Configuration Release -Runtime win-x64
powershell -ExecutionPolicy Bypass -File .\create-release.ps1 -OutputZip Dashboard-vX.Y.Z-win-x64.zip
```

Main directories:

- `src/`: Windows desktop host.
- `dashboard-src/`: zashboard-based frontend source.
- `resources/dashboard/`: built frontend assets.
- `STYLE.md`: local UI rules.
- `UPSTREAM_MERGE.md`: checklist for following upstream zashboard.

## License

This project is released under the MIT License.

The embedded frontend is based on zashboard, which is also licensed under the MIT License. See `dashboard-src/LICENSE` for the upstream copyright notice.
