# Upstream Merge Guide

This project embeds a customized copy of zashboard in a Windows desktop shell. Upstream zashboard updates should be merged deliberately so we keep upstream improvements while preserving the desktop launcher behavior and local UI rules.

Use this file as the single entry point for upstream follow-up work. Before merging, also read:

- `STYLE.md` for UI tokens, shared utilities, and visual constraints.

This file owns both the merge workflow and the Dashboard-specific product contract. Do not split the upstream checklist into another document; keep the process on this one path.

## Rule Zero

Every upstream follow-up must happen on a new branch.

- Do not merge upstream zashboard directly on `main`.
- Use branch names like `codex/upstream-zashboard-v3.10.1`.
- Keep one upstream version update per branch when possible.
- Commit local conflict fixes separately from mechanical upstream imports when that makes review clearer.

Recommended start:

```powershell
git switch main
git pull origin main
git switch -c codex/upstream-zashboard-vX.Y.Z
```

## Source Of Truth

Do not rely only on the release changelog.

- Changelog explains upstream intent.
- Commit list explains why smaller changes happened.
- Tag-to-tag diff shows what actually changed.

Use this priority:

1. Tag diff as the factual source.
2. Commit messages and commit diffs for intent.
3. Release changelog as navigation.

## Baseline

Current embedded zashboard baseline:

```text
dashboard-src/package.json: 3.11.0
```

The desktop app is not pure zashboard. It consists of:

- C# desktop host in `src/`
- customized zashboard copy in `dashboard-src/`
- built frontend resources in `resources/dashboard/`
- project UI rules in `STYLE.md`

## Dashboard Product Contract

Preserve these product decisions during every upstream merge.

Desktop host ownership:

- C# owns process management, tray behavior, autostart, WebView lifecycle, native window behavior, local settings, publish layout, and Windows window APIs.
- Frontend owns page layout, Core page presentation, zashboard views, top bars, sidebar, and window-control visuals.
- The app manages one active core at a time: `mihomo` or `sing-box`.
- Core paths, config paths, API URLs, secrets, and core type are separate local settings.
- Local publish layout keeps `Dashboard.exe`, `settings.json`, `resources/`, and separate core folders.
- `resources/EBWebView` must be cleared when replacing a local install so stale WebView assets do not hide changes.
- In lightweight mode, hiding to tray keeps the WebView alive briefly before disposal; reopening from tray during that delay cancels disposal. Do not restore immediate WebView disposal on close-to-tray.

Frontend product shape:

- Default route is the Core page.
- Standalone Settings is folded into Core page.
- Backend settings live in Core page, not as a separate primary route.
- Core page contains active core status, start/stop/switch controls, path/API settings, logs, operation buttons, and current download summary.
- Core page section titles use the same `dashboard-section-title` structure. The embedded Backend title is static text; do not restore upstream version links or click-through title behavior.
- Overview page content should follow the same right-edge padding rhythm as the other scrollable card pages, including `p-3 md:pr-2` where applicable.
- Dashboard uses the Clash-compatible API path for both mihomo and sing-box main pages.
- sing-box native API / Tools is not exposed.
- Window controls are custom frontend controls connected to the C# host.
- `dashboard-src/src/assets/styles/dashboard-desktop.css` is the final desktop override layer and must stay imported last.

Desktop settings defaults:

- The settings title is `Dashboard`, without upstream version, commit id, update indicator, or upstream GitHub link.
- Dashboard self-upgrade UI is hidden; desktop releases are handled by this project, not upstream `/upgrade/ui`.
- `splitOverviewPage` defaults to enabled.
- `displayGlobalByMode` defaults to enabled.

Do not restore without a product decision:

- standalone Settings page as a primary route
- settings visibility dialog
- multi-backend management UI as the main product model
- upstream DNS query panel
- upstream Dashboard self-upgrade controls
- upstream core upgrade/config update modals that bypass the C# host
- sing-box native Tools as a separate native API channel

Keep from upstream when compatible:

- API models, request handling, stores, and general data parsing
- Overview, Proxies, Rules, Connections, and Logs page behavior
- performance fixes, virtual list/table fixes, responsive fixes, and accessibility improvements
- sidebar polish, toggle improvements, and visual refinements that can be expressed with existing `STYLE.md` tokens
- sing-box compatible dashboard improvements when they work through the Clash-compatible API path

## Update Workflow

1. Start from current `main`, pull latest changes, and create a fresh upstream branch.
2. Fetch or download the upstream zashboard tag into `.tmp/`.
3. Read the release notes for a quick overview.
4. Generate tag-to-tag commit and diff reports.
5. Classify changed files before editing.
6. Merge low-risk upstream changes first.
7. Hand-merge files that overlap with local desktop/UI changes.
8. Reapply the Dashboard product contract from this file.
9. Reapply local visual constraints from `STYLE.md`.
10. Run type-check and build.
11. Manually inspect the app pages and desktop shell behavior.
12. Record accepted, skipped, and manually merged changes before merging back.

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

## File Classes

Classify each upstream change before applying it.

### Upstream-First

Prefer accepting upstream changes here unless they conflict with the desktop shell.

- `dashboard-src/src/api/`
- `dashboard-src/src/store/`
- generic data composables
- proxy/rule/connection/log core behavior
- bug fixes for mobile, tables, virtual lists, API parsing, and performance

When upstream adds a general improvement here, keep it.

### Local-First

Do not overwrite these with upstream code.

- `src/*.cs`
- `dashboard-src/src/hostBootstrap.ts`
- `dashboard-src/src/views/CorePage.vue`
- `dashboard-src/src/components/common/WindowControls.vue`
- `dashboard-src/src/assets/styles/dashboard-desktop.css`
- `STYLE.md`
- `UPSTREAM_MERGE.md`
- desktop-specific settings and C# bridge behavior
- `tools/build-zashboard.ps1`

These files define the launcher product, not zashboard upstream.

### Manual-Merge

These often contain both upstream value and local layout changes. Always inspect carefully.

- `dashboard-src/src/views/HomePage.vue`
- `dashboard-src/src/components/common/CtrlsBar.vue`
- `dashboard-src/src/components/sidebar/SideBar.vue`
- `dashboard-src/src/components/sidebar/SidebarButtons.vue`
- `dashboard-src/src/components/common/DashboardSettings.vue`
- `dashboard-src/src/components/settings/backend/BackendSettings.vue`
- `dashboard-src/src/components/controls/ProxiesCtrl.tsx`
- `dashboard-src/src/components/controls/ConnectionCtrl.tsx`
- `dashboard-src/src/components/controls/LogsCtrl.tsx`
- `dashboard-src/src/assets/styles/override.css`
- `dashboard-src/src/assets/styles/components.css`
- `dashboard-src/src/assets/main.css`

For these files, copy the upstream behavior, not blindly the whole file.

### Replaced Or Removed Upstream Features

Do not restore these unless we explicitly decide to change product direction.

- standalone Settings page
- settings visibility dialog
- multi-backend management UI
- upstream DNS query panel
- upstream Dashboard self-upgrade controls
- upstream core upgrade/config update modals that conflict with the desktop package

If upstream improves a useful subcomponent in this area, extract the improvement and adapt it to the embedded Core/settings layout.

## Product Boundaries

Keep these decisions stable unless discussed first.

- The app is a mihomo desktop launcher first.
- C# owns process management, tray behavior, autostart, WebView lifecycle, and Windows window APIs.
- Frontend owns visual layout, top bars, sidebar, settings presentation, and window-control buttons.
- The default route is the Core page.
- The independent Settings page has been folded into the Core page.
- Multi-backend support should not reappear as a visible primary workflow unless intentionally restored.

## UI Merge Rules

All upstream UI changes must pass `STYLE.md`.

Keep upstream improvements when they fit our style system:

- sidebar item animations
- improved toggles
- better responsive behavior
- bug fixes for visibility in dim/dark themes
- accessibility or focus fixes that do not reintroduce heavy black outlines

Reconcile these with local rules:

- `dashboard-desktop.css` is the final desktop override layer and must remain imported last
- top bar controls use shared `CtrlsBar` behavior
- top bar controls are 36px high
- top bar borders use `border-base-content/20`
- regular card/settings dividers use `border-base-border`
- settings use `settings-grid`, `setting-item`, and `settings-section-label`
- secondary/read-only text uses `text-base-content/60`
- very weak separators use `text-base-content/40`
- avoid one-off gray opacity tokens and page-local custom button families

## Feature Decisions

When upstream adds a feature, classify it before merging.

- General zashboard feature: usually keep.
- Desktop-host feature: only keep if C# can support it cleanly.
- Backend API feature: keep the API/model changes, then decide whether to expose UI.
- sing-box native feature: preserve reusable upstream code where possible, but do not turn the launcher into a dual mihomo/sing-box process manager without an explicit product decision.
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

Manual inspection checklist:

- Core page
- Overview page
- Proxies page
- Rules page
- Connections page
- Logs page
- Sidebar expanded and collapsed
- Window buttons, dragging, resizing, minimize, close-to-tray
- Tray icon open behavior
- Start/stop/restart core actions
- Light mode and hidden-to-tray behavior

## Merge Checklist

Use this checklist for every upstream branch.

Preparation:

- [ ] Branch was created from current `main`.
- [ ] Target upstream version and current local baseline are written down.
- [ ] Release notes were read for navigation.
- [ ] Commit list was reviewed for intent.
- [ ] Tag-to-tag or folder diff was generated as the factual source.

Merge:

- [ ] Each changed file was classified as Upstream-First, Local-First, Manual-Merge, or Replaced/Removed.
- [ ] Local desktop shell files were not blindly overwritten.
- [ ] Removed upstream features were not accidentally restored.
- [ ] `tools/build-zashboard.ps1 -SkipBuild` passed before the full build.
- [ ] General upstream bug fixes and data/API improvements were kept where compatible.
- [ ] Manual-merge files were reviewed for both upstream behavior and local layout constraints.

UI:

- [ ] Upstream visual changes pass `STYLE.md`.
- [ ] `dashboard-src/src/assets/styles/dashboard-desktop.css` still exists and remains the last import in `dashboard-src/src/assets/main.css`.
- [ ] Dashboard frontend contract check passed.
- [ ] Top bars still use shared `CtrlsBar` rules.
- [ ] Settings still use `settings-grid`, `setting-item`, and `settings-section-label`.
- [ ] New buttons, inputs, panels, and text colors reuse existing tokens/utilities.
- [ ] No new page-local class family was added without documenting the reusable purpose in `STYLE.md`.

Build and review:

- [ ] Frontend type-check passed.
- [ ] Full build passed.
- [ ] Built resources in `resources/dashboard/` were regenerated when frontend code changed.
- [ ] Core, Overview, Proxies, Rules, Connections, and Logs were manually inspected.
- [ ] Sidebar expanded/collapsed, window buttons, tray behavior, and core start/stop/restart were manually inspected.

Regression checks from the v3.11.0 follow-up:

- [ ] `src/main.ts` still imports `./hostBootstrap`; window drag and resize work.
- [ ] Top bars do not overlap page content.
- [ ] Overview page card alignment matches the other scrollable card pages and does not appear shifted left/right.
- [ ] Rules tab is visible on first app load when split overview is enabled.
- [ ] Sidebar bottom does not show backend settings button or backend version.
- [ ] Sidebar route items keep `gap-1`.
- [ ] Core top status text truncates inside its own box and does not run into buttons.
- [ ] Core page section titles share the same `dashboard-section-title` structure; Backend title is not a link.
- [ ] Settings header says `Dashboard` only.
- [ ] Dashboard self-upgrade controls are not visible.
- [ ] DNS query UI and sing-box native UI are not restored.
- [ ] Lightweight close-to-tray delays WebView disposal and reopening from tray cancels the pending disposal.
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
4. Did any STYLE.md rule need to change?
5. Were built resources in `resources/dashboard/` regenerated?
6. Did type-check and full build pass?
