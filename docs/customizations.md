# Dashboard Customizations

This document records how this project differs from upstream zashboard. Use it during upstream merges to decide what to keep, what to skip, and what must be hand-merged.

For merge workflow, read `../UPSTREAM_MERGE.md`. For visual rules, read `../STYLE.md`.

## Product Shape

- The app is a Windows desktop dashboard based on zashboard, not a pure web deployment.
- C# owns process management, tray behavior, autostart, WebView lifecycle, native window behavior, local settings, and publish layout.
- The embedded frontend owns page layout, Core page presentation, zashboard views, top bars, sidebar, and window-control visuals.
- The default product entry is the Core page.
- The app manages one active core at a time: `mihomo` or `sing-box`.
- zashboard remains the source for general dashboard pages such as overview, proxies, rules, connections, and logs.

## Desktop Shell Changes

Preserve these local desktop behaviors when following upstream:

- Start, stop, restart, and switch the active core through the C# host.
- Keep core paths, config paths, API URLs, secrets, and core type in local settings.
- Keep tray icon behavior, close-to-tray behavior, WebView lifecycle, and Windows window controls under desktop host ownership.
- Keep local publish layout with `Dashboard.exe`, `settings.json`, `resources/`, and separate core folders.
- Keep generated zashboard assets embedded under `resources/dashboard/`.
- Clear `resources/EBWebView` when replacing a local install so stale WebView assets do not hide changes.

## zashboard UI Changes

Preserve these frontend product decisions:

- The standalone Settings page is folded into the Core page.
- Backend settings live inside Core page instead of being a separate primary route.
- Core page contains active core status, start/stop/switch controls, path/API settings, logs, operation buttons, and current download summary.
- The Core page supports both mihomo and sing-box through Clash-compatible API assumptions.
- Top bars are desktop-aware and must not collide with custom window buttons.
- Window control buttons are custom frontend controls connected to C# host APIs.

## Keep From Upstream

Prefer accepting upstream changes in these areas when they do not conflict with local product decisions:

- API models, request handling, stores, and general data parsing.
- Overview, proxies, rules, connections, and logs page behavior.
- Performance fixes, virtual list/table fixes, responsive fixes, and accessibility improvements.
- Sidebar polish, toggle improvements, and visual refinements that can be expressed with existing `STYLE.md` tokens.
- sing-box compatible dashboard improvements when they work through the existing Clash-compatible API path.

## Do Not Restore Without A Product Decision

Do not reintroduce these upstream behaviors accidentally:

- Standalone Settings page as a primary route.
- Settings visibility dialog as a user-facing workflow.
- Multi-backend management UI as the main product model.
- Upstream DNS query panel.
- Upstream core upgrade/config update modals that bypass the C# host.
- sing-box native Tools as a separate native API channel; this project currently uses the Clash-compatible API path.

## Manual-Merge Areas

These areas often contain both upstream value and local behavior. Review them line by line:

- Core page and embedded backend settings.
- Top bar, sidebar, and window control components.
- Shared style files under `dashboard-src/src/assets/styles/`.
- Host bootstrap and desktop bridge code.
- Any upstream change that touches settings, backend selection, DNS tools, or native desktop assumptions.

## UI Customization Rules

- `STYLE.md` is the source of truth for visual decisions.
- Prefer zashboard components and DaisyUI classes first, then project utilities, then new utilities only when there are at least two real reuse sites.
- Keep top bar controls on shared `CtrlsBar` rules.
- Keep settings panels on `settings-grid`, `setting-item`, and `settings-section-label`.
- Keep neutral operation buttons on `btn btn-sm dashboard-action-btn`.
- Keep Core top buttons as the documented semantic exception: switch `primary`, start `success`, stop `warning`.
- Keep MiSans variable font fix in the build pipeline; do not compensate with page-local font-weight hacks.

## Publish And Resource Rules

- Frontend source changes must be followed by regenerated `resources/dashboard/` assets before release.
- Do not manually edit generated files in `resources/dashboard/`; regenerate them through the build.
- Local replacement should preserve user settings and core folders.
- Do not move cache/runtime files again without a separate product decision.