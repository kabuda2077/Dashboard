# Upstream Merge Guide

This project embeds a customized zashboard UI inside a Windows desktop shell. Upstream zashboard updates should be merged deliberately: keep compatible upstream improvements, preserve the Dashboard desktop product contract, and avoid reintroducing removed upstream workflows.

This is the single entry point for upstream follow-up work. Do not split the checklist into another document.

Before starting, read:

- `STYLE.md` for UI tokens, utilities, and visual constraints.
- `README.md` / `README.zh-CN.md` only when release-facing wording changes.

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
dashboard-src/package.json: 3.11.0
```

The desktop app is not pure zashboard. It consists of:

- C# desktop host in `src/`
- customized zashboard copy in `dashboard-src/`
- built frontend resources in `resources/dashboard/`
- build/release scripts in `tools/`, `build.ps1`, and `create-release.ps1`
- local UI rules in `STYLE.md`

## Product Contract

Preserve these decisions unless the product direction is explicitly changed.

### Desktop Host

- C# owns process management, tray behavior, autostart, WebView lifecycle, native window behavior, local settings, publish layout, and Windows window APIs.
- Frontend owns page layout, Core page presentation, zashboard views, top bars, sidebar, and window-control visuals.
- Window drag/resize depends on `dashboard-src/src/hostBootstrap.ts`; `dashboard-src/src/main.ts` must keep importing it.
- The app manages one active core at a time: `mihomo` or `sing-box`.
- Core paths, config paths, API URLs, secrets, and core type are separate local settings.
- Local publish layout keeps `Dashboard.exe`, `settings.json`, `resources/`, and separate core folders.
- `resources/EBWebView` must be cleared when replacing a local install so stale WebView assets do not hide changes.
- In lightweight mode, hiding to tray keeps the WebView alive briefly before disposal; reopening from tray during that delay cancels disposal.

### Core And Backend

- Default route is the Core page.
- Standalone Settings is folded into Core page.
- Backend settings live in Core page, not as a separate primary route.
- Core page contains active core status, start/stop/switch controls, path/API settings, logs, operation buttons, and current download summary.
- Core page section titles use the same `dashboard-section-title` structure.
- The embedded Backend heading is static text; do not restore upstream version links or click-through heading behavior.
- The sidebar bottom must not show a backend settings button or backend version.
- Dashboard uses the Clash-compatible API path for both mihomo and sing-box main pages.
- sing-box native API / Tools is not exposed in the desktop product.
- A sing-box config may expose both `clash_api.external_controller` and `services.type=api`; this Dashboard uses the Clash-compatible endpoint, not the native/dashboard service endpoint.
- Runtime version display should prefer Clash `/version`, but may fall back to the desktop host's executable version when the API is not ready.

### Settings Defaults

- The settings title is `Dashboard`, without upstream version, commit id, update indicator, or upstream GitHub link.
- Dashboard self-upgrade UI is hidden; desktop releases are handled by this project, not upstream `/upgrade/ui`.
- `splitOverviewPage` defaults to enabled.
- `displayGlobalByMode` defaults to enabled.

### Do Not Restore

Do not restore these without a product decision:

- standalone Settings page as a primary route
- settings visibility dialog
- multi-backend management UI as the main product model
- upstream DNS query panel
- upstream Dashboard self-upgrade controls
- upstream core upgrade/config update modals that bypass the C# host
- sing-box native Tools as a separate native API channel

### Keep From Upstream When Compatible

- API models, request handling, stores, and general data parsing.
- Overview, Proxies, Rules, Connections, and Logs behavior.
- Performance fixes, virtual list/table fixes, responsive fixes, and accessibility improvements.
- Sidebar polish, toggle improvements, and visual refinements that can be expressed with existing `STYLE.md` tokens.
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
- `STYLE.md`
- `UPSTREAM_MERGE.md`
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
- `dashboard-src/src/components/common/DashboardSettings.vue`
- `dashboard-src/src/components/sidebar/SideBar.vue`
- `dashboard-src/src/components/sidebar/SidebarButtons.vue`
- `dashboard-src/src/components/settings/backend/BackendSettings.vue`
- `dashboard-src/src/components/common/BackendVersion.vue`
- `dashboard-src/src/components/controls/ProxiesCtrl.tsx`
- `dashboard-src/src/components/controls/ConnectionCtrl.tsx`
- `dashboard-src/src/components/controls/LogsCtrl.tsx`
- `dashboard-src/src/assembly/backend.ts`
- `dashboard-src/src/assembly/version.ts`
- `dashboard-src/src/assets/main.css`
- `dashboard-src/src/assets/styles/override.css`
- `dashboard-src/src/assets/styles/components.css`

## Merge Workflow

1. Start from current `main`, pull latest changes, and create a fresh upstream branch.
2. Fetch or download the upstream zashboard tag into `.tmp/`.
3. Read release notes for navigation.
4. Generate tag-to-tag commit and diff reports.
5. Classify changed files as Upstream-First, Local-First, Manual-Merge, or Removed/Replaced.
6. Merge low-risk upstream changes first.
7. Hand-merge files that overlap with local desktop/UI changes.
8. Reapply the Product Contract from this file.
9. Reapply local visual constraints from `STYLE.md`.
10. Run the contract check, type-check, and full build.
11. Manually inspect the app pages and desktop shell behavior.
12. Record accepted, skipped, and manually merged changes in the final merge summary.

## UI Merge Rules

All upstream UI changes must pass `STYLE.md`.

Required rules:

- `dashboard-src/src/assets/styles/dashboard-desktop.css` is the final desktop override layer and must remain imported last.
- Top bars use shared `CtrlsBar` behavior.
- Top bar controls are 36px high.
- Top bar borders use `border-base-content/20`.
- Regular card/settings dividers use `border-base-border`.
- Settings use `settings-grid`, `setting-item`, and `settings-section-label`.
- Core page headings use `dashboard-section-title`.
- Secondary/read-only text uses `text-base-content/60`.
- Very weak separators use `text-base-content/40`.
- Avoid one-off gray opacity tokens and page-local custom button families.
- Top bar dropdowns that need controlled popup styling should use the project dropdown component, not native WebView `<select>` popups.
- Core top status text must truncate inside its own box and not run into buttons.
- Sidebar route items keep `gap-1`.
- Overview page card alignment must match other scrollable card pages, including the shared right-edge padding rhythm.

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
- sing-box native feature: preserve reusable code where helpful, but do not expose a native API workflow without a product decision.
- Visual polish: keep when it can be expressed with existing style tokens.

## Verification

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

When replacing a local install, also clear WebView cache:

```powershell
Remove-Item E:\APP\Dashboard\resources\EBWebView -Recurse -Force
```

Manual inspection checklist:

- Core page: top status row, section titles, backend card, current downloads, logs, start/stop/switch/restart/upgrade actions.
- Overview page: no local-core label in the top bar, settings button placement, split overview behavior, card alignment.
- Proxies page: card secondary text, search/dropdown widths, sidebar behavior.
- Rules page: tab visible on first app load, top bar dropdown border and popup behavior.
- Connections page: top bar search/dropdown spacing and controlled dropdown close behavior.
- Logs page: level dropdown and search spacing.
- Sidebar expanded/collapsed: route spacing, bottom panels, no backend settings/version.
- Window shell: buttons, dragging, resizing, maximize, minimize, close-to-tray, tray reopen.
- Core behavior: mihomo start/stop/restart/upgrade; sing-box start/stop/restart/upgrade through desktop host.
- API behavior: mihomo and sing-box pages use Clash-compatible API; sing-box native Tools stay hidden.
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
- [ ] `src/main.ts` still imports `./hostBootstrap`.
- [ ] `dashboard-desktop.css` still exists and remains imported last in `dashboard-src/src/assets/main.css`.
- [ ] Built resources in `resources/dashboard/` were regenerated when frontend code changed.

UI:

- [ ] Upstream visual changes pass `STYLE.md`.
- [ ] Top bars do not overlap page content.
- [ ] Top bars use shared `CtrlsBar` rules.
- [ ] Settings still use `settings-grid`, `setting-item`, and `settings-section-label`.
- [ ] New buttons, inputs, panels, and text colors reuse existing tokens/utilities.
- [ ] No new page-local class family was added without documenting the reusable purpose in `STYLE.md`.
- [ ] Overview page alignment matches the other scrollable card pages.
- [ ] Sidebar route items keep `gap-1`.
- [ ] Core top status text truncates inside its own box.
- [ ] Core page section titles share the same `dashboard-section-title` structure.

Product:

- [ ] Settings header says `Dashboard` only.
- [ ] Sidebar bottom does not show backend settings button or backend version.
- [ ] Backend title is not a link.
- [ ] Dashboard self-upgrade controls are not visible.
- [ ] DNS query UI is not restored.
- [ ] sing-box native Tools UI is not restored.
- [ ] Rules tab is visible on first app load when split overview is enabled.
- [ ] Lightweight close-to-tray delays WebView disposal and tray reopen cancels pending disposal.
- [ ] sing-box uses the Clash-compatible API path for main dashboard pages.
- [ ] Runtime version display works after switching between mihomo and sing-box.

Build and review:

- [ ] `pnpm --dir dashboard-src type-check` passed.
- [ ] `tools/build-zashboard.ps1 -SkipBuild` passed.
- [ ] Full `build.ps1` passed.
- [ ] Core, Overview, Proxies, Rules, Connections, and Logs were manually inspected.
- [ ] Sidebar expanded/collapsed, window buttons, tray behavior, and core actions were manually inspected.
- [ ] Local replacement, if performed, cleared `resources/EBWebView`.

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
4. Did any `STYLE.md` rule need to change?
5. Were built resources in `resources/dashboard/` regenerated?
6. Did type-check, contract check, and full build pass?
7. Was local replacement tested with `resources/EBWebView` cleared?
