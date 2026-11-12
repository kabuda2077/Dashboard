# Dashboard

[简体中文](README.md) | [English](README.en.md)

**A Windows desktop proxy dashboard for mihomo and sing-box**, built on [zashboard](https://github.com/Zephyruso/zashboard). Manage two core profiles, select and benchmark nodes, inspect live connections, traffic, rules and logs, and use desktop tray and network diagnostics in one place.

- **Latest release**: [Releases](https://github.com/kabuda2077/Dashboard/releases)
- **Supported cores**: mihomo, sing-box (with Clash API enabled)
- **Platform**: Windows x64

---

## Quick Start

### Requirements

Make sure the following are installed before running Dashboard:

- **Windows x64**
- **[.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)** (required even if .NET 9 is already installed)
- **[Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)**

### Installation & Setup

1. Download the latest archive from [Releases](https://github.com/kabuda2077/Dashboard/releases), extract all files, and run `Dashboard.exe`.
2. Prepare your own `mihomo` or Clash-API-enabled `sing-box` executable and configuration file (the release package does not bundle proxy cores or user configs).
3. Open the **Core** page, select the core type, fill in the executable path, configuration file path, API address, and Secret, then save and start the core.

> **Core API Configuration**
> - **mihomo**: enable `external-controller` (for example `127.0.0.1:9090`) and match the Secret with your `secret` field.
> - **sing-box**: enable `experimental.clash_api.external_controller` and match the Secret with your configuration.
> - If the core starts but stays disconnected, check the API address and Secret. If the target API does not belong to the currently managed core, Dashboard warns you and disables mihomo upgrades through that API.
> - UAC is requested when administrator privileges are required. Elevate with the **current Windows account**; elevating with another account cancels the handoff and keeps the original window.

---

## Features

### Core & Desktop Management

- **Dual core profiles**: store two independent core configurations and start, stop, restart, or switch the active core at any time.
- **TUN controls**: the TUN mode and allow-LAN switches stay in the main backend section; the TUN stack selector is located under the expanded **Settings → Connection settings → TUN settings** when supported (switching stacks may briefly interrupt connections).
- **System tray & lightweight mode**: supports tray residency, autostart, and close-to-tray. The tray icon follows Windows light or dark mode and updates when the mode changes while running. With lightweight mode enabled, hiding the window for one minute releases the WebView interface and restores it when reopened.

### Proxies, Connections & Preferences

- **Daily proxy workflow**: switch proxy groups and nodes, run latency tests, and inspect live connections, traffic, rules, and logs. All settings are consolidated on the Core page.
- **Group icon caching**: automatically cache remote proxy group icons, including newly added groups and custom icons set in the interface. Reopening the interface prefers the local cache.
- **Connection views & history**: switch between **Active** and **All** connections, and click **All** again to filter for **Closed only**. Connection history is a capacity-bounded aggregate trimmed and saved periodically even in the background; lower-traffic entries may be evicted and accumulate again if they reappear. Aggregation mode and table sorting are saved and exported with interface preferences (statistical data itself is not exported).
- **Customization & source labels**: customize themes, background images, and source IP labels, and import/export interface preferences. Optional source-IP hostname resolution is available (disabled by default; manual labels take priority and unsupported lookups keep the original IP).

### Overview & Network Diagnostics

Use the left-hand tabs to switch between **Latency** and **Routing**, while the right-hand side shows **Domestic / International exit information**:

- **Latency checks**: tests 8 targets with up to 4 concurrent targets and 5 sequential samples each, displaying the median of successful samples. Sample bars grow as results arrive, queued bars stay still, and failed samples appear as short red bars (animations respect reduced-motion preferences).
- **Routing checks**: covers 8 targets by opening independent host HTTPS connections through an existing local proxy inbound of the active core, matching the final node or direct route. Confirmed results show a green dot and green bottom track, while unconfirmed or failed results stay gray; an active check shows a moving segment (indicating activity only, not latency or percentage progress). Checks require a usable local inbound and never modify core configuration.
- **On-demand & automatic refresh**: click any target tile to retest it, or use the lightning button to retest the current tab. Previous results remain visible during retests, persist across page navigation and lightweight-mode disposal within the same app session, and clear on full exit. Automatic checks run only while the card is visible: latency follows its auto-check toggle and refreshes when returning after 5 minutes; routing runs on first view or after path changes without periodic polling. Node, mode, rule, or network changes coalesce into a refresh of the visible tab.
- **Exit IP & attribution**: queries domestic and international exits in parallel. The domestic card queries `ipip.net` once for IP, location, and ISP (without ASN); the international card queries the provider selected under **IP info API** once for the observed exit IP, location, ASN, and ISP/organization, showing the provider name in the title. Details are ordered as **Location | ASN number | ISP** with single-line truncation and full hover tooltips; privacy masking applies consistently to both visible text and hover titles. The same API setting is used for online attribution in connection details, while the connection list uses separate GeoIP country and ASN databases.

> **Diagnostic notes**: HTTP request time is not ICMP Ping. Host routing probes may hit different process or inbound rules than WebView, TUN, or other applications; results do not represent every application and do not guarantee website unblocking or full site functionality.

---

## Updates & Upgrades

### Updating 2.x

- **Fully exit** Dashboard from the system tray, extract the new archive over the existing application files, and keep your `settings.json`, core executables, and configuration files.
- Application version changes reset WebView browser data (clearing connection history and local background images, so local backgrounds need to be selected again), while saved settings and interface preferences are restored automatically.

### Upgrading from 1.x to 2.x

- **Do not overwrite an existing 1.x folder.** Back up the old directory, extract 2.x into a new empty directory, and configure it again. Settings, history, backgrounds, and autostart entries from 1.x are not migrated automatically.

### Application & Core Updates

- **Dashboard**: check for updates in the About section to open the release page and replace files manually.
- **Original mihomo**: recognized original builds upgrade through the managed core API and report completion only after the restarted process and API are confirmed ready.
- **Core project page**: click the core version at the top to open the recognized core’s GitHub project page. For example, TanakaLun opens `TanakaLun/mihomo`, and reF1nd sing-box opens `reF1nd/sing-box`. Unknown sources do not fall back to a fixed repository.
- **Manual core updates**: for recognized forks (such as TanakaLun and Smart) and sing-box, **Upgrade core** opens the corresponding releases page, with manual update instructions on hover. The same button opens the releases page when an official mihomo core cannot currently upgrade directly. reF1nd sing-box points to its dedicated releases repository. Download the archive, stop the core, and replace the executable manually. The button is disabled for unknown sources and never falls back to an upstream update URL.

---

## Notes & Troubleshooting

- **sing-box scope**: managed through the Clash API with a read-only desktop TUN stack; native API, Tools, and Terminal are not supported. Automatic TUN recovery after system sleep/wake has not been verified.
- **Secret protection**: Secrets are encrypted for the current Windows user. If decryption fails after switching Windows accounts, re-enter and save the Secret on the Core page.
- **Port & autostart**: the local UI uses port `33291` and cannot start if that port is occupied. Autostart scheduled tasks with conflicting ownership are not overwritten; check existing tasks in Windows Task Scheduler.
- **Logs**: runtime logs are stored under `resources/logs`. After lightweight mode releases the UI, tray restore timings are logged to `performance.log` by default, with the same `trayRestore id` for each stage of a restore. `elapsedMs` starts at the restore request, `durationMs` measures a stage or event, `documentMs` is time since frontend navigation, and `sincePreviousMs` is the interval between frontend markers. `frontendReady` means the initial route is ready and the browser has had a paint opportunity; it does not include core API readiness or physical screen presentation. Launch with `Dashboard.exe --diagnostic-log` for other verbose diagnostic logging.

---

## Development & License

- See [docs/development.md](docs/development.md) for development and verification workflows, and [docs/upstream.md](docs/upstream.md) for upstream synchronization (both in Chinese).
- Released under the [MIT License](LICENSE); upstream frontend copyright is retained in [dashboard-src/LICENSE](dashboard-src/LICENSE).
