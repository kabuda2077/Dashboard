# Upstream Merge Guide

This project embeds a customized zashboard UI inside a Windows desktop shell. Upstream zashboard updates should be merged deliberately: keep compatible upstream improvements, preserve the Dashboard desktop product contract, and avoid reintroducing removed upstream workflows.

This is the single entry point for upstream follow-up work. Do not split the checklist into another document. Commands and code-formatted repository paths below are relative to the repository root.

Before starting, read:

- [Architecture](architecture.md) for current ownership, startup/session/persistence boundaries, and compatibility decisions.
- [Style guide](style.md) for UI tokens, utilities, and visual constraints.
- [README.md](../README.md) / [README.en.md](../README.en.md) only when release-facing wording changes.

## Rule Zero

Every upstream follow-up must happen on a new branch.

- Do not merge upstream zashboard directly on `main`.
- Use branch names like `codex/upstream-zashboard-vX.Y.Z`.
- Keep one upstream version update per branch when possible.
- Commit mechanical imports, desktop integration fixes, and built resources as separate commits when that helps review.

Recommended start:

```powershell
git switch main
git pull origin main
git switch -c codex/upstream-zashboard-vX.Y.Z
```

## Source Of Truth

Do not rely only on the release changelog.

Use this priority:

1. Tag-to-tag diff: what actually changed.
2. Commit diffs/messages: why smaller changes happened.
3. Release changelog: navigation and highlights.

Useful commands when an upstream git checkout is available:

```powershell
git log vOLD..vNEW --oneline --stat
git diff --name-status vOLD..vNEW
git diff --stat vOLD..vNEW
```

Useful commands when comparing downloaded folders:

```powershell
git diff --no-index --name-status .tmp\zashboard-vX.Y.Z\src dashboard-src\src
git diff --no-index --stat .tmp\zashboard-vX.Y.Z\src dashboard-src\src
```

## Current Baseline

Current embedded zashboard baseline:

```text
dashboard-src/package.json: 3.26.0
```

The version string is not proof that every upstream feature was merged. The actual v3.26.0 follow-up has this reproducible three-way baseline:

| Role | Verified ref | Meaning |
|---|---|---|
| Desktop base | `c6a0741ee76253bbc610424a3bd745cee16ea3a2` (`origin/main` at the follow-up start) | Local product before the v3.26.0 follow-up; embedded frontend baseline was v3.19.0. |
| Upstream base | zashboard `v3.19.0`, `f78fac5e29fd5c15908d7bd613105c699948d1aa` | Common upstream source represented by the previous embedded version. |
| Upstream target | zashboard `v3.26.0`, `b31d05f42702b121e0b17eab1e3697d5f1d6db8d` | Target tag; `v3.19.0` is an ancestor and the range contains 74 commits. |
| Local result | `3042c081718e96fa4b410c3237c1ba918cfa82b0` | Follow-up/refactor commit used by the implementation plan; later uncommitted P1–P10 work must be reviewed separately from this ref. |

These refs were resolved from the local repositories, not inferred from package versions. Reproduce the upstream side with `git -C .tmp/analysis/zashboard-upstream diff v3.19.0..v3.26.0`; compare both upstream endpoints with the desktop base/result rather than treating a two-tree folder diff as a merge base. The compatible source and dependency updates selected for the desktop product are applied on top of the customized frontend. Features excluded by the Product Contract remain omitted. See `docs/upstream/v3.26.0.md` for that follow-up's decision record.

The desktop app is not pure zashboard. It consists of:

- C# desktop host in `src/`
- customized zashboard copy in `dashboard-src/`
- built frontend resources in `resources/dashboard/` (generated, not tracked in git)
- build/release scripts in `tools/`, `build.ps1`, and `create-release.ps1`
- local UI rules in `docs/style.md`

## Product Contract

Preserve these decisions unless the product direction is explicitly changed.

### Desktop Host

- C# owns process management, tray behavior, autostart, WebView lifecycle, native window behavior, local settings, publish layout, and Windows window APIs.
- Frontend owns page layout, Core page presentation, zashboard views, top bars, sidebar, and window-control visuals.
- Window drag/resize depends on `dashboard-src/src/hostBootstrap.ts`; `dashboard-src/src/appEntry.ts` must keep importing it. `main.ts` must remain storage-neutral and load `appEntry.ts` only after `dashboardStartup.ts` restores the latest host settings snapshot.
- Proxy group icons use the desktop icon cache bridge. The C# host builds `iconCacheMap`, `hostBootstrap.ts` exposes it as `window.__mihomoIconCache`, and `ProxyIcon.vue` must prefer cached local URLs before remote icon URLs.
- Existing proxy icon cache files should be loaded on startup and notify the WebView after the map changes, so the Proxies page can render cached strategy group icons immediately.
- The app manages one active core at a time: `mihomo` or `sing-box`.
- Core paths, config paths, API URLs, secrets, and core type are separate local settings.
- Local publish layout keeps `Dashboard.exe`, `settings.json`, `resources/`, and separate core folders.
- Normal replacement must preserve `resources/EBWebView`. The host's content marker must invalidate HTTP cache and service workers when entry content changes while retaining profile data such as IndexedDB. Full profile removal is a destructive troubleshooting/reset action, never a routine merge or release step, and requires explicit user scope for the isolated target.
- In lightweight mode, hiding to tray keeps the WebView alive briefly before disposal; reopening from tray during that delay cancels disposal.

### Core And Backend

- Default route is the Core page.
- Standalone Settings is folded into Core page.
- Backend settings live in Core page, not as a separate primary route.
- Core page contains active core status, start/stop/switch controls, path/API settings, logs, operation buttons, and current download summary.
- Core operation buttons keep this row-major order: reload config, restart core, flush DNS cache, flush Fake IP, update GEO, upgrade core. sing-box omits update GEO; optional smart-group maintenance actions come after this sequence.
- When a core update is available, the same update dot appears beside the Backend version and on the upgrade-core button.
- mihomo and sing-box update availability must use the same host-owned state lifecycle: clear stale availability before a check or upgrade, clear it after a failed check, and recheck after the installed binary version changes.
- Both cores must use the shared release-metadata request/retry helper. Their GitHub release endpoints and upgrade execution can differ where required by their release channels and native capabilities.
- Core page section titles use the same `dashboard-section-title` structure.
- The embedded Backend heading remains static text. Only the adjacent core version is clickable: mihomo opens `MetaCubeX/mihomo`, while sing-box opens `reF1nd/sing-box` in the system browser.
- The sidebar bottom must not show a backend settings button or backend version.
- Dashboard uses the Clash-compatible API path for both mihomo and sing-box main pages.
- sing-box native API / Tools is intentionally removed from the desktop frontend. See the sing-box version contract for API endpoint and version fallback details.

### sing-box Version Display Contract

Do not regress the Backend version display after switching cores.

- The Backend title/version area must show the active core version after switching from mihomo to sing-box.
- In the desktop product, sing-box version should be read from the Clash-compatible API first: `clash_api.external_controller` + `/version`.
- Do not treat sing-box `services.type=api` as the desktop Dashboard main API. That service may exist for sing-box's own dashboard/native API and can use a different port.
- If Clash `/version` is not reachable yet, use the C# host-provided executable version fallback, read from the active `sing-box.exe version` output.
- A failed API probe must not leave the Backend title with only the sing-box icon and no version text.
- The displayed version remains a repository link after switching cores and must update its target with the active core.

### Settings Defaults

- The settings title is `Dashboard`, without upstream version, commit id, update indicator, or upstream GitHub link.
- Dashboard self-upgrade UI is hidden; desktop releases are handled by this project, not upstream `/upgrade/ui`.
- `splitOverviewPage` defaults to enabled.
- `displayGlobalByMode` defaults to enabled.

### Overview Network And Latency Cards

- The Overview network row uses the same three-column rhythm as the speed cards above it.
- In desktop width, Latency spans two columns and Network Info spans one column; mobile remains a single column.
- The Latency card keeps four targets: Baidu, Cloudflare, GitHub, and YouTube.
- Each latency target runs 10 samples per test. Do not increase sample count just to change chart density.
- Latency history is rendered with the same `MiniSparkline` visual language as the Upload/Download cards: smooth line, subtle area fill, and small sample symbols.
- Latency chart color follows the target's live average latency using the low/medium/high latency theme tokens.
- The right-side latency value is the average and should only appear after the target's 10 samples complete.
- The row under each target shows `min` and `max`; keep the average in the chart row instead of duplicating it below.
- Latency hover uses the original compact tooltip style through `useTooltip`, showing a single sample value such as `120ms`; do not use the Upload/Download ECharts tooltip in the latency card.

### Do Not Restore

Do not restore these without a product decision:

- standalone Settings page as a primary route
- settings visibility dialog
- multi-backend management UI as the main product model
- upstream DNS query panel
- upstream Dashboard self-upgrade controls
- upstream core upgrade/config update modals that bypass the C# host
- sing-box native API adapters, generated protobuf code, dependencies, and Tools UI

### Recorded Product Exceptions

For the v3.26.0 follow-up, the decision is concrete:

- Accepted: compatible Clash API/data parsing, request/stream lifecycle, persistence, responsive/accessibility, table/list, logs/rules, and performance fixes.
- Adapted: App entry, host bridge/version behavior, settings, proxy/connection UI, and shared styles around the desktop host; sing-box stays on the Clash-compatible API with host executable-version fallback.
- Deferred: reverse DNS that depends on an upstream DNS-query endpoint and broader browser-only/performance harness work.
- Rejected: multi-backend and standalone Settings workflows; native sing-box/protobuf/Tools/Terminal/Tailscale/USB-IP/OpenVPN; DNS Query, Dashboard self-upgrade, Earth/Honk cards, and workflows that bypass the C# host for core/config upgrades.

A future follow-up must append or update its own decision record; it must not silently reinterpret these exceptions as missing imports.

### Keep From Upstream When Compatible

- API models, request handling, stores, and general data parsing.
- Overview, Proxies, Rules, Connections, and Logs behavior.
- Performance fixes, virtual list/table fixes, responsive fixes, and accessibility improvements.
- Sidebar polish, toggle improvements, and visual refinements that can be expressed with existing `docs/style.md` tokens.
- sing-box compatible dashboard improvements when they work through the Clash-compatible API path.

## File Classes

Classify every upstream-changed file before applying it.

### Upstream-First

Prefer accepting upstream changes here unless they conflict with the desktop shell:

- `dashboard-src/src/api/`
- `dashboard-src/src/store/`
- generic data composables
- proxy/rule/connection/log core behavior
- mobile, table, virtual-list, API parsing, and performance fixes

Keep general upstream bug fixes and data/API improvements where compatible.

### Local-First

Do not overwrite these with upstream code:

- `src/*.cs`
- `dashboard-src/src/hostBootstrap.ts`
- `dashboard-src/src/views/CorePage.vue`
- `dashboard-src/src/components/common/WindowControls.vue`
- `dashboard-src/src/assets/styles/dashboard-desktop.css`
- `docs/style.md`
- `docs/upstream-merge.md`
- `tools/build-zashboard.ps1`
- desktop-specific settings and C# bridge behavior

These files define the launcher product, not upstream zashboard.

### Manual-Merge

These often contain both upstream value and local layout changes. Inspect carefully and copy behavior, not blindly the whole file:

- `dashboard-src/src/App.vue`
- `dashboard-src/src/main.ts`
- `dashboard-src/src/router/index.ts`
- `dashboard-src/src/views/HomePage.vue`
- `dashboard-src/src/components/common/CtrlsBar.vue`
- `dashboard-src/src/components/overview/ConnectionStatus.vue`
- `dashboard-src/src/components/overview/LatencyChart.vue`
- `dashboard-src/src/components/overview/MiniSparkline.vue`
- `dashboard-src/src/components/overview/NetworkCard.vue`
- `dashboard-src/src/components/proxies/ProxyIcon.vue`
- `dashboard-src/src/components/common/DashboardSettings.vue`
- `dashboard-src/src/components/sidebar/SideBar.vue`
- `dashboard-src/src/components/sidebar/SidebarButtons.vue`
- `dashboard-src/src/components/settings/backend/BackendSettings.vue`
- `dashboard-src/src/components/common/BackendVersion.vue`
- `dashboard-src/src/components/controls/ProxiesCtrl.tsx`
- `dashboard-src/src/components/controls/ConnectionCtrl.tsx`
- `dashboard-src/src/components/controls/LogsCtrl.tsx`
- `dashboard-src/src/components/common/TextInput.vue`
- `dashboard-src/src/components/common/VirtualScroller.vue`
- `dashboard-src/src/views/LogsPage.vue`
- `dashboard-src/src/views/RulesPage.vue`
- `dashboard-src/src/assembly/backend.ts`
- `dashboard-src/src/assembly/version.ts`
- `dashboard-src/src/assets/main.css`
- `dashboard-src/src/assets/styles/theme/`
- `dashboard-src/src/assets/styles/base/`
- `dashboard-src/src/assets/styles/components/`
- `dashboard-src/src/assets/styles/utilities/`

## Integration Closure Rule

Review each accepted change as a dependency closure, not as an isolated file:

| Check | Required evidence |
|---|---|
| Entry | The real production importer, route, lifecycle callback, or C# command path reaches the behavior. |
| Dependencies | Helpers, stores, styles, API models, bridge protocol, package changes, and generated resources required by that entry are present. |
| Consumers | Every page/component/host consumer is found by reference search and remains compatible with desktop ownership and session identity. |
| Tests | At least one test exercises the production entry where practical; helper tests alone are insufficient. Record the manual Windows case when WebView/core/system behavior cannot be automated safely. |

For the desktop seams, preserve the chain `main.ts` → `dashboardStartup.ts` → `appEntry.ts` → `hostBootstrap.ts`/Vue, the WebView message path through `HostMessageRouter` and `hostBridge.ts`, and backend-session invalidation even when two cores share an endpoint. Historical investigation inventories are not auto-accept, auto-delete, or overwrite whitelists.

The manual acceptance entry is `docs/manual-acceptance.md`. It maps shortcuts, startup import, host messages, reload settings, and same-endpoint switching to production entries, dependencies, consumers, tests, and explicitly unrun manual cases. The companion `docs/validation.md` records the clean automated gate, release package, and artifact audit; it does not convert unrun manual cases into passes.

## Merge Workflow

1. Start from current `main`, pull latest changes, and create a fresh upstream branch.
2. Fetch or download the upstream zashboard tag into `.tmp/`.
3. Read release notes for navigation.
4. Generate tag-to-tag commit and diff reports.
5. Classify changed files as Upstream-First, Local-First, Manual-Merge, or Removed/Replaced.
6. Merge low-risk upstream changes first.
7. Hand-merge files that overlap with local desktop/UI changes.
8. Reapply the Product Contract from this file.
9. Reapply local visual constraints from `docs/style.md`.
10. Run the contract check, type-check, and full build.
11. Manually inspect the app pages and desktop shell behavior.
12. Record accepted, skipped, and manually merged changes in the final merge summary.

## UI Merge Rules

All upstream UI changes must pass `docs/style.md`.

Required rules:

- `dashboard-src/src/assets/styles/dashboard-desktop.css` is the final desktop override layer and must remain imported last.
- Top bars, settings, colors, borders, text hierarchy, and reusable components must follow `docs/style.md`.
- Core page headings use `dashboard-section-title`.
- Top bar dropdowns that need controlled popup styling should use the project dropdown component, not native WebView `<select>` popups.
- Connections, Logs, and Rules card views use independent `base-container` rows with the measured `gap-3` spacing defined in `docs/style.md`.
- Core top status text must truncate inside its own box and not run into buttons.
- Sidebar route items keep `gap-1`.
- Overview Network and Latency cards must keep the Product Contract above.
- Proxies page strategy group icons must keep the desktop icon cache bridge from the Product Contract above.

Keep upstream visual improvements when they fit this system:

- sidebar item animations
- improved toggles
- better responsive behavior
- dim/dark theme fixes
- accessibility or focus fixes that do not reintroduce heavy black outlines

## Feature Decisions

When upstream adds a feature, classify it before exposing UI:

- General zashboard feature: usually keep.
- Desktop-host feature: keep only if C# can support it cleanly.
- Backend API feature: keep API/model changes, then decide whether to expose UI.
- sing-box native feature: do not import it into the desktop frontend without a new product decision; users can run an upstream browser dashboard against a separate native API port.
- Visual polish: keep when it can be expressed with existing style tokens.

## Verification

Current artifact evidence and relevant historical checkpoints are recorded in `docs/validation.md`; measurements and rejected speculative optimizations are in `docs/performance.md`. Actual Windows/WebView/core acceptance remains unrun. Results apply only to their recorded source snapshots; future upstream branches must run and record their own checks.

Run frontend checks:

```powershell
pnpm --dir dashboard-src type-check
```

Run the Dashboard frontend contract check:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\build-zashboard.ps1 -SkipBuild
```

Run full build:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

For a normal local replacement, preserve `resources/EBWebView`. Start the replacement build and verify that the host content marker applies targeted HTTP-cache/service-worker invalidation and that profile-backed data remains available. If a full profile reset is genuinely required for diagnosis, stop and obtain explicit user approval for the exact isolated directory; do not put a broad `Remove-Item` command in this workflow.

Manual inspection checklist:

- Core page: top status row, section titles, backend card, current downloads, logs, start/stop/switch/restart/upgrade actions.
- Overview page: no local-core label in the top bar, settings button placement, split overview behavior, card alignment.
- Overview Network and Latency cards: Product Contract still holds.
- Proxies page: card secondary text, search/dropdown widths, sidebar behavior, and strategy group icon cache behavior.
- Rules page: tab visible on first app load, top bar dropdown border and popup behavior.
- Connections page: top bar search/dropdown spacing and controlled dropdown close behavior.
- Logs page: level dropdown and search spacing.
- Sidebar expanded/collapsed: route spacing, bottom panels, no backend settings/version.
- Window shell: buttons, dragging, resizing, maximize, minimize, close-to-tray, tray reopen.
- Core behavior: mihomo start/stop/restart/upgrade; sing-box start/stop/restart/upgrade through desktop host.
- API behavior: mihomo and sing-box pages use Clash-compatible API; no sing-box native API or Tools code is bundled.
- Themes/layout: light mode, dark mode, small window, normal window, maximized window, high DPI if possible.

## Merge Checklist

Preparation:

- [ ] Branch was created from current `main`.
- [ ] Target upstream version and current local baseline are written down.
- [ ] Release notes were read for navigation.
- [ ] Commit list was reviewed for intent.
- [ ] Tag-to-tag or folder diff was generated as the factual source.

Merge:

- [ ] Each changed file was classified before editing.
- [ ] Local-First files were not blindly overwritten.
- [ ] Removed upstream features were not accidentally restored.
- [ ] General upstream bug fixes and data/API improvements were kept where compatible.
- [ ] Manual-Merge files were reviewed for both upstream behavior and local product constraints.
- [ ] `src/main.ts` remains storage-neutral and starts `dashboardStartup`; `src/appEntry.ts` still imports `./hostBootstrap` after fresh settings restoration.
- [ ] `dashboard-desktop.css` still exists and remains imported last in `dashboard-src/src/assets/main.css`.
- [ ] `resources/dashboard/` was rebuilt after frontend changes. It is generated and untracked, so there is nothing to commit; `check.ps1` and `build.ps1` regenerate it before the .NET build.

UI:

- [ ] Upstream visual changes pass `docs/style.md`.
- [ ] Top bars do not overlap page content.
- [ ] Top bars use shared `CtrlsBar` rules.
- [ ] Settings still use `settings-grid`, `setting-item`, and `settings-section-label`.
- [ ] New buttons, inputs, panels, and text colors reuse existing tokens/utilities.
- [ ] No new page-local class family was added without documenting the reusable purpose in `docs/style.md`.
- [ ] Overview Network and Latency cards still match the Product Contract.
- [ ] Sidebar route items keep `gap-1`.
- [ ] Core top status text truncates inside its own box.
- [ ] Core page section titles share the same `dashboard-section-title` structure.
- [ ] Core operation buttons keep the documented order, and core updates mark both the version and upgrade action.
- [ ] Backend version opens the active core's GitHub repository, and read-only TUN follows DaisyUI's disabled-toggle appearance.

Product:

- [ ] Settings header says `Dashboard` only.
- [ ] Sidebar bottom does not show backend settings button or backend version.
- [ ] Backend title is not a link.
- [ ] The Do Not Restore list was checked.
- [ ] Rules tab is visible on first app load when split overview is enabled.
- [ ] Proxies page strategy group icon cache behavior still matches the Product Contract.
- [ ] Lightweight close-to-tray delays WebView disposal and tray reopen cancels pending disposal.
- [ ] sing-box API selection and Backend version display still match the sing-box Version Display Contract.

Build and review:

- [ ] `pnpm --dir dashboard-src type-check` passed.
- [ ] `tools/build-zashboard.ps1 -SkipBuild` passed.
- [ ] Full `build.ps1` passed.
- [ ] Manual inspection checklist completed.
- [ ] Local replacement, if performed, preserved `resources/EBWebView`, invalidated stale HTTP cache/service workers through the host content marker, and retained profile data.

## Commit Discipline

Prefer small commits with clear intent.

Suggested sequence:

1. `Import zashboard vX.Y.Z changes`
2. `Reapply desktop shell integration`
3. `Reconcile upstream UI with local style guide`
4. `Build dashboard resources`

Do not squash away important conflict decisions if they would help future upstream merges.

## Final Review Questions

Before merging the branch back to `main`, answer:

1. Which upstream features were accepted?
2. Which upstream features were intentionally skipped?
3. Which local files required manual conflict resolution?
4. Did any `docs/style.md` rule need to change?
5. Was `resources/dashboard/` rebuilt and verified? (Generated and untracked, so it never appears in the diff.)
6. Did type-check, contract check, and full build pass?
7. Was normal local replacement tested with targeted cache/service-worker invalidation while `resources/EBWebView` and profile-backed data were preserved?
