# Upstream Merge Guide

This project embeds a customized copy of zashboard in a Windows desktop shell. Upstream zashboard updates should be merged deliberately so we keep upstream improvements while preserving the desktop launcher behavior and local UI rules.

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
dashboard-src/package.json: 3.9.0
```

The desktop app is not pure zashboard. It consists of:

- C# desktop host in `src/`
- customized zashboard copy in `dashboard-src/`
- built frontend resources in `resources/dashboard/`
- project UI rules in `STYLE.md`

## Update Workflow

1. Fetch or download the upstream zashboard tag into `.tmp/`.
2. Read the release notes for a quick overview.
3. Generate tag-to-tag commit and diff reports.
4. Classify changed files before editing.
5. Merge low-risk upstream changes first.
6. Hand-merge files that overlap with local desktop/UI changes.
7. Reapply local visual constraints from `STYLE.md`.
8. Run type-check and build.
9. Manually inspect the app pages.
10. Commit and push the upstream branch.

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
- `STYLE.md`
- `UPSTREAM_MERGE.md`
- desktop-specific settings and C# bridge behavior

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

For these files, copy the upstream behavior, not blindly the whole file.

### Replaced Or Removed Upstream Features

Do not restore these unless we explicitly decide to change product direction.

- standalone Settings page
- settings visibility dialog
- multi-backend management UI
- upstream DNS query panel
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
