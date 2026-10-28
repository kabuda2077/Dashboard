# Dashboard

[简体中文](README.md) | [English](README.en.md)

A Windows desktop proxy dashboard based on [zashboard](https://github.com/Zephyruso/zashboard), supporting mihomo and sing-box with the Clash API enabled.

## Features

- Manage two independent core configurations; start, stop, restart and switch the active core.
- Select proxies, run latency tests, and view connections, traffic, rules and logs.
- Customize themes, backgrounds and connection labels; import and export interface preferences.
- System tray, autostart, close to tray and lightweight mode. Lightweight mode releases the interface after one minute hidden and restores it when reopened.
- Optional source-IP hostname resolution, disabled by default. Manual labels take priority; unsupported lookups keep the original IP.
- Overview measures HTTP request times for eight targets, with five samples each. The number is the median of successful samples; dots retain failures. Scroll between the left-hand latency/routing cards; a thin scrollbar appears on hover. Routing checks cover eight targets and create independent host HTTPS connections through an existing local proxy inbound of the current core and show the matched final node or direct route. Checks are unavailable without a usable inbound and never change configuration. Process and inbound rules may differ from WebView, TUN or other apps; results do not represent every application. Unmatched connections remain unconfirmed.
- Click an individual latency or routing target to retest it; the lightning button tests all targets on the current page. Existing results remain visible during checks and survive page changes and lightweight WebView disposal within the same application session; exiting the application clears them. Automatic checks run only while the diagnostics card is visible: latency follows its auto-check setting and refreshes on a return after five minutes; routing runs on first view or after a path change, without periodic polling. Node, mode, rule or network changes coalesce into a refresh of the visible page without pending-update labels.
- Network information shows the single exit IP actually observed by each of ipip.net and Cloudflare, with no separate IPv6 probe or columns. The foreign exit shows Location, ISP/organization and ASN as independent fields; missing fields are not inferred. Addresses appear before country, region, city and organization enrichment and remain visible if it fails. By default, the latter half of each IP and location details beyond the country/territory are masked. The ISP keeps its first word and the ASN keeps its number, with each following word replaced by `**`; reveal them explicitly to see full information. The domestic exit adds no attribution lookup. These observations follow current network and routing rules and do not represent every proxy node. Request time is not Ping; routing checks do not guarantee unblocking or full website functionality.

## Install and use

Requires Windows x64, [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) and [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/).

1. Download a ZIP from [Releases](https://github.com/kabuda2077/Dashboard/releases), extract it and run `Dashboard.exe`.
2. Supply mihomo or sing-box with the Clash API enabled, plus its configuration file. The package includes neither cores nor configurations.
3. On the Core page, choose the core and enter its executable, configuration file, API address and Secret. Save and start it. UAC is requested when administrator privileges are needed.

For mihomo, configure `external-controller`; for sing-box, use `experimental.clash_api.external_controller`. An example API address is `http://127.0.0.1:9090`. The Secret must match the core configuration. If the core starts but remains disconnected, check the API address and Secret.

Settings are on the Core page. Connections offers Active and All views; select All again to show only closed connections. Connection statistics aggregation and table sorting are saved as interface preferences and included in preference imports and exports; statistical data is not included.

## Updates and limitations

- **When upgrading from 1.x to 2.x, extract to a new directory and configure it again.** Old settings, history, backgrounds and autostart entries are not migrated automatically. Keep the old directory as a backup.
- To update 2.x, fully exit the application and replace its files. Keep `settings.json`, cores and configuration files.
- An application version change clears browser data, including connection history and local background images. Saved interface preferences are restored. The first switch from the old cache policy also resets browser data once.
- Secrets are protected for the current Windows user. If another user cannot decrypt them, enter and save them again on the Core page.
- Dashboard updates open the download page for manual replacement. mihomo can upgrade through its core API. Built-in sing-box upgrades use the **reF1nd Windows amd64v3** build; other editions can be upgraded manually.
- sing-box is managed through the Clash API, with its desktop TUN stack read-only. Native API, Tools and Terminal are unavailable. Automatic TUN recovery after sleep has not been verified.
- The local interface uses port `33291` and cannot start if it is occupied. Autostart tasks with conflicting ownership are not overwritten; check existing tasks.

For troubleshooting, inspect `resources/logs`. Start with `Dashboard.exe --diagnostic-log` for detailed logging.

For development and verification, see the [development guide](docs/development.md). For upstream updates, see the [upstream guide](docs/upstream.md) (both in Chinese).

MIT License. Upstream frontend copyright is retained in [dashboard-src/LICENSE](dashboard-src/LICENSE).
