# Dashboard

[简体中文](README.md) | [English](README.en.md)

A Windows desktop proxy dashboard based on [zashboard](https://github.com/Zephyruso/zashboard), supporting mihomo and sing-box with the Clash API enabled.

## Features

- Manage two independent core configurations; start, stop, restart and switch the active core. The TUN mode switch stays in the main backend section. The stack selector appears when supported under the expanded Settings → Connection settings → TUN settings; changing it may interrupt connections.
- Select proxies, run latency tests, and view connections, traffic, rules and logs. Connection history is a bounded aggregate that is trimmed and periodically saved while the interface runs in the background. Entries with less downloaded traffic may be removed and start accumulating again if they reappear.
- Customize themes, backgrounds and connection labels; import and export interface preferences.
- System tray, autostart, close to tray and lightweight mode. Lightweight mode releases the interface after one minute hidden and restores it when reopened.
- Optional source-IP hostname resolution, disabled by default. Manual labels take priority; unsupported lookups keep the original IP.
- Overview measures HTTP request times for eight targets, with up to four targets running concurrently and five sequential samples per target. Each completed target frees a slot for the next one. The number is the median of successful samples. Sample bars grow from the bottom as results arrive; waiting and queued bars stay still, and failures appear as short red bars. Animations are disabled when reduced motion is preferred. Tabs switch between the left-hand latency and routing cards. Routing checks cover eight targets and create independent host HTTPS connections through an existing local proxy inbound of the current core and show the matched final node or direct route. Both use a green dot for a confirmed result; unconfirmed results use gray. The track is green after a confirmed connection and gray for unconfirmed or failed results, with a moving segment during a check. It does not indicate latency or percentage progress. Checks are unavailable without a usable inbound and never change configuration. Process and inbound rules may differ from WebView, TUN or other apps; results do not represent every application. Unmatched connections remain unconfirmed.
- Click an individual latency or routing target to retest it; the lightning button tests all targets on the current page. Latency retests keep the previous number while bars show current progress, then update the final median after all five samples; routing checks keep their previous results. Results survive page changes and lightweight WebView disposal within the same application session; exiting the application clears them. Automatic checks run only while the diagnostics card is visible: latency follows its auto-check setting and refreshes on a return after five minutes; routing runs on first view or after a path change, without periodic polling. Node, mode, rule or network changes coalesce into a refresh of the visible page without pending-update labels.
- Network information queries domestic and international exits in parallel. The domestic card uses ipip.net to obtain the IP, location and ISP in one request, without an ASN lookup. The international card uses the service selected under “IP info API” to obtain the exit IP observed by that service, location, ISP/organization and ASN in one request; its title shows the selected service. Valid addresses remain visible when attribution fields are incomplete, and missing fields are not inferred. Details are ordered as location, ASN number and ISP; the domestic ISP is also aligned to the right. Long text stays on one line, with full location, ISP and ASN organization available on hover. By default, the latter half of each IP and location details beyond the country/territory are masked. The ISP keeps its first word and the ASN keeps its number, with each following word replaced by `**`; hover text follows the same privacy setting. Reveal them explicitly to see full information. Addresses naturally use the IPv4 or IPv6 returned by each service, without additional probes. These observations follow current network and routing rules and do not represent every proxy node. The API setting also controls online attribution in connection details; the connection list uses the separate GeoIP country and ASN databases. Request time is not Ping; routing checks do not guarantee unblocking or full website functionality.

## Install and use

Requires Windows x64, [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) and [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/).

1. Download a ZIP from [Releases](https://github.com/kabuda2077/Dashboard/releases), extract it and run `Dashboard.exe`.
2. Supply mihomo or sing-box with the Clash API enabled, plus its configuration file. The package includes neither cores nor configurations.
3. On the Core page, choose the core and enter its executable, configuration file, API address and Secret. Save and start it. UAC is requested when administrator privileges are needed. Elevate with the current Windows account; using another account cancels the handoff and keeps the original window.

For mihomo, configure `external-controller`; for sing-box, use `experimental.clash_api.external_controller`. An example API address is `http://127.0.0.1:9090`. The Secret must match the core configuration. If the core starts but remains disconnected, check the API address and Secret. An API that does not belong to the managed core is marked, and mihomo upgrades through that API are disabled.

Settings are on the Core page. Connections offers Active and All views; select All again to show only closed connections. Connection statistics aggregation and table sorting are saved as interface preferences and included in preference imports and exports; statistical data is not included.

## Updates and limitations

- **When upgrading from 1.x to 2.x, extract to a new directory and configure it again.** Old settings, history, backgrounds and autostart entries are not migrated automatically. Keep the old directory as a backup.
- To update 2.x, fully exit the application and replace its files. Keep `settings.json`, cores and configuration files.
- An application version change clears browser data, including connection history and local background images. Saved interface preferences are restored. The first switch from the old cache policy also resets browser data once.
- Secrets are protected for the current Windows user. If another user cannot decrypt them, enter and save them again on the Core page.
- Dashboard updates open the download page for manual replacement. Recognized original mihomo builds upgrade through the managed core API; completion is reported after the replacement process and API are confirmed ready. Recognized mihomo forks, including TanakaLun and Smart, and sing-box use a Releases action with a hover hint: download the core, stop it, then replace its executable. reF1nd sing-box opens its separate releases repository. Unknown sources never fall back to an original core’s update address; confirm the download source yourself. Version markers identify known formats but cannot prove the repository of a custom build that retains an original version string.
- sing-box is managed through the Clash API, with its desktop TUN stack read-only. Native API, Tools and Terminal are unavailable. Automatic TUN recovery after sleep has not been verified.
- The local interface uses port `33291` and cannot start if it is occupied. Autostart tasks with conflicting ownership are not overwritten; check existing tasks.

For troubleshooting, inspect `resources/logs`. Start with `Dashboard.exe --diagnostic-log` for detailed logging.

For development and verification, see the [development guide](docs/development.md). For upstream updates, see the [upstream guide](docs/upstream.md) (both in Chinese).

MIT License. Upstream frontend copyright is retained in [dashboard-src/LICENSE](dashboard-src/LICENSE).
