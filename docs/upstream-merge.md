# Following upstream zashboard

This is the **single operational guide** for upstream updates. Build implementation lives under `tools/internal/`; neither scripts nor historical inventories independently decide product scope. Current code and current validation take precedence over dated analysis.

## 1. Baseline and authority

The reviewed upstream baseline is zashboard **v3.26.0**, SHA `b31d05f42702b121e0b17eab1e3697d5f1d6db8d`. Its repository root maps to local `dashboard-src/`. The Dashboard 2.0 restructuring does not advance this upstream baseline. See [the baseline and retained decisions](upstream/v3.26.0.md) and [maintenance boundaries](maintenance.md). Generate comparisons from the exact upstream baseline and current local source; historical reports are not current validation.

For each update identify:

- **U0:** last fully reviewed upstream tag and exact SHA.
- **D0:** reproducible Dashboard starting revision/snapshot.
- **U1:** proposed upstream tag and exact SHA.
- **D1:** local candidate and validation evidence.

Review `U0 → U1` against our existing `U0 → D0` changes and their consumers. A package version alone does not prove adoption. A cherry-picked fix does not mean all changes through that release have been reviewed.

Do not overwrite or silently stash a dirty working tree. Preserve it and establish D0 first. Work on a new branch, not directly on main. No push/release without the corresponding authorization.

## 2. Constraints and decision ownership

| Level | Meaning | Decision rule |
| --- | --- | --- |
| Guarantees | No plaintext persisted credentials; no untrusted native commands; correct session isolation, save acknowledgements and failure recovery; preserve data created by the supported format during normal operation | Must not be weakened for merge convenience. Implementations may change if evidence preserves the guarantee |
| Current product choices | Single active mihomo/sing-box, Clash API, desktop process ownership, Core-first desktop workflow, portable deployment, current page/operation scope | Keep by default; propose changes to the product owner rather than silently importing them |
| Retained visual contract | Compact density, alignment, consistency, control order, focus/scroll context and surfaces in [style.md](style.md); sizes are current references | Preserve the experience by default; equivalent sizes/implementations may follow upstream with consumer evidence. Obvious or uncertain visual changes need comparison and a design decision |
| Pending visual choices | Any unresolved choices listed in the local-change register; the initial six v2 choices are now explicitly accepted in style.md | Keep new proposals pending until decided; current code/tests alone do not establish owner approval |
| Implementation | Specific classes, filenames, refs, caches, layout technique and component internals | May be replaced while preserving the preceding levels; update implementation-bound tests with equivalent evidence |
| Historical decisions | A feature was previously deferred/rejected for cost, dependency, protocol or relevance | Revisit when the reason changes; a product-scope change still requires approval |

Dashboard 2.0 intentionally does not support old Dashboard settings, profile directories, legacy Secret formats, old backend UUIDs or previous bridge protocols. Do not reintroduce those migrations merely because upstream contains them. Current supported core API differences are not the same thing as old Dashboard data compatibility.

## 3. Current desktop contract

- Native process/window/tray/autostart/file selection and executable updates remain trusted host operations. Data pages use Clash-compatible APIs. Do not bypass save coordination or native trust checks through upstream action buttons.
- One core operation path and non-queuing core gate; profile saves and their dependent actions are coordinated. Desktop options and file dialogs are not hidden core-profile saves.
- The host is authoritative for desktop connection identity, epoch, running process and operation result. Local editor drafts remain separate. Same-endpoint restarts/switches invalidate old asynchronous results.
- Restore the bootstrap preference object before importing preference-initializing application modules. Command replies correlate to request IDs; nullable incremental fields can clear old values.
- Settings schema and bridge protocol are version 2. Disk commits become visible only after success. A process started result is not the same as API readiness.
- Close to tray, view suspension, delayed disposal and application exit remain distinct. Same-version restarts and resource replacements preserve browser data. A changed application version or missing valid marker resets only `resources/webview-data-v2/EBWebView`, before browser startup; do not use resource fingerprints or touch 1.x profiles. Preserve host settings, core files, icon-cache and logs. See the reset/failure contract in maintenance.md.
- Current core pages remain Core, Proxies, Connections, Overview, Logs and Rules; settings are embedded in Core. Browser preview has no host privileges and its own connection records.
- Preserve current valid user-facing features: proxy folders, grouping/filtering, labels, histories, backgrounds, theme settings, latency tests and meaningful error feedback. Known bugs and unused interfaces are not features to preserve.
- Core uses the `v1.3.2` user-visible reference and retained historical status/Switch/Start/Stop arrangement, with independent window controls and no profile selector. Keep v2's target-draft editing and explicit save/switch. Cancellation preserves drafts, runtime **and background/scroll context**. Sizes and optical alignment are governed by [style.md](style.md), not by copying the old renderer. Pending v2 layout choices are not implicitly approved.
- First-time setup remains a full dialog with mihomo/sing-box selection and executable/config/API/Secret fields, file selection and core-specific API help. Starting commits only the chosen draft. API readiness plus a durable completeSetup ACK gates automatic dismissal; a banner is not an equivalent replacement. An elevated --start-core relaunch with incomplete setup must resume starting/probing/completing setup before showing the main window; failed startup/API readiness/persistence returns to visible setup, without prematurely persisting completion.
- Core action order currently is reload configuration, restart, DNS flush, Fake IP flush, GEO update where supported, core upgrade, then optional Smart actions. The version and upgrade indicator use host update state.
- Current network/latency default: four targets, ten samples per target, average with min/max. The renderer and its component names are replaceable.
- No standalone native Tools/Terminal/Tailscale/USB-IP/OpenVPN, Earth/Honk pages or upstream UI self-upgrade in the desktop product **without a new product decision**. A common helper from such a change may still be useful independently.
- The production UI currently uses fixed loopback HTTP. Changing this requires validating every supported API URL/WS path and the security policy, not disabling browser security.

## 4. Decide by change, not directory

For each meaningful upstream change ask:

1. What user problem or defect does it address?
2. Does it preserve guarantees and fit current product scope?
3. What permissions, background work, dependencies, external data access and testing cost does it add?
4. Can it replace/remove a local patch rather than add a parallel implementation?
5. How will the real consumer and unsupported-core fallback be validated?

Choose **adopt**, **adapt**, **replace local implementation**, **request product decision**, **defer**, or **not included this time**. Record the reason and a re-review trigger for deferred items. Unreviewed is not the same as rejected. Group trivial changes under one explained decision where appropriate.

File ownership labels help prioritize review; they are not automatic ours/theirs rules. API/store changes can affect native credentials, while a local-first component may have a better upstream replacement. No textual merge conflict does not imply semantic compatibility.

Follow the accepted change through its production entry, dependencies, every consumer and tests. Do not copy a helper without its required wiring, or reject an entire commit just because one UI feature is outside scope.

## 5. Practical workflow

### Current local differences and incoming impact

The curated [local-change register](upstream/local-changes.json) is the single machine-readable source of U0 and the stable UI/behavior reference R (`v1.3.2`). It groups intent, historical evidence, local paths, explicit dependency watch patterns, relevant checks, pending decisions and known gaps. It is **not** a hand-maintained all-file inventory or an ours/theirs merge policy. Wildcards use PowerShell semantics (`*` spans directories); root configuration filenames are explicit so new source features remain visible as unclassified.

Keep an isolated upstream checkout at U0. Fetch proposed refs there, without changing the application or advancing the reviewed baseline. Run the existing audit:

```powershell
# One-time checkout for the current baseline; use the register when the baseline advances.
git clone --depth 1 --branch v3.26.0 https://github.com/Zephyruso/zashboard.git .tmp/experiments/upstream-audit-v3.26.0
pwsh -NoProfile -File tools/internal/audit-upstream.ps1
# After fetching a proposed tag/commit into that checkout (leave HEAD at U0):
pwsh -NoProfile -File tools/internal/audit-upstream.ps1 -TargetRef <proposed-upstream-ref>
```

The audit reads R from a temporary archive and D from the current candidate including untracked source, never switching/stashing it. It compares **U0→R, R→D, U0→D** for frontend sources; native files are explicitly local-only relative to U0. Outputs under `.tmp/reports/upstream-current/`:

- `inventory.json`: normalized source hashes, the three relations and intent groups; byte changes are not automatically semantic changes.
- `audit.json` / `report.md`: exact refs, dirty status, fingerprints, unclassified paths, pending choices, known gaps and incoming U0→U1 impact. A shared upstream component may trigger consumer checks even when it has no local patch. New unclassified files always need review.
- `modified.diff`: U0→D modified frontend text files; `reference-to-candidate.diff`: Git's R→D tracked source diff. Added/deleted/untracked files are also recorded in the inventory, not magically represented by those partial text diffs.

Three declared mirrors are checked offline by Check/Release; the audit additionally verifies their recorded hashes against real U0 files. These are narrow ownership guards, not a frozen hash list for the whole codebase. Do not refresh hashes from candidate files. If upstream replaces a local fix, remove the explained patch only after checking consumers. The audit never advances U0, updates a screenshot/hash baseline, merges, pushes or approves a release.

Data page entries are explicitly owned by proxy-experience, tables-history or overview-charts so an upstream page-only change triggers the relevant consumer checks. Excluded page patterns name the excluded scope; they do not classify every view as excluded. Browser Setup belongs to host-session and remains separate from desktop Core setup.

A new group needs an intent/evidence/check link; a new change in an existing group still needs semantic review. Watches are explicit dependency triggers, not a complete dependency graph. Formatting and comments remain visible in source diffs and may be grouped after inspection, rather than automatically ignored.

### Integration steps

1. Preserve the working tree and establish D0; create a dedicated branch for this upstream update.
2. Fetch U0/U1 into a separate upstream checkout, retaining exact refs. Release notes are navigation; tag-to-tag diff and commit intent are evidence.
3. Generate the upstream delta and identify overlap with current local changes plus dependency reach. Avoid rebuilding the entire historical audit on every patch release.
4. Decide feature/implementation treatment before mechanically resolving overlaps. Escalate changes to scope, permissions, data destination or major workflow; ordinary compatible fixes and proven equivalent replacements need no per-line approval.
5. Integrate coherent dependency groups. Prefer small reviewable commits; avoid unrelated reformatting, whole-lockfile ours/theirs and simultaneous wholesale local restructuring.
6. When upstream replaces a local fix, remove the obsolete local mechanism after verifying the replacement. Do not keep both implementations indefinitely behind another switch.
7. Resolve package changes first, then update the lockfile using the pinned package manager. Build outputs remain generated, not source commits.
8. Run targeted tests while developing, then the common Check entry for the candidate. Tooling/document changes also run check-maintenance.ps1 (including offline three-way/impact regression fixtures). Check/Release validate the fork register and mirrors before building. Update tests that freeze implementation only after replacing their protected behavior with equivalent evidence.
9. Run the affected real WebView/core/system checks. Mark pass/fail/unrun/blocked/not applicable accurately; old logs and mock-only tests do not prove current integration.
10. Write a short version record with U0/U1/D0/D1, grouped decisions, local patches retired, commands/environment, actual outcomes and remaining acceptance.
11. Advance the reviewed upstream baseline only after the whole delta has a decision. A code merge and a release-ready candidate are different states.

A three-way tool must have access to the actual base blobs and correct path mapping. An upstream checkout in `.tmp/` does not automatically make `git apply --3way` valid in the Dashboard repository. Do not introduce a subtree/submodule migration solely to avoid understanding the three inputs.

## 6. One check and one release entry

Build/test tooling requires PowerShell 7 (`pwsh`). The packaged application has no PowerShell dependency.

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .\tools\check.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File .\tools\create-release.ps1
```

Both use the same automated verification stages. Release captures the source revision and tracked/untracked source manifest before verification, checks it again after verification/publish and immediately before replacing the ZIP, and saves that starting manifest. Changed inputs abort and preserve the previous ZIP. The two tracked generated icons (resources/app.ico and resources/tray.ico) are outputs, excluded from source comparison; their tracked favicon and generator remain inputs, and ZIP content hashes cover the produced files. Internal front-end build, icon generation and resource assertions are not a separate user workflow.

Optional real WebView validation:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .\tools\check.ps1 -IncludeWebViewIntegration
```

Optional isolated verified-core validation (never user profiles):

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .\tests\scripts\PrepareValidationCores.ps1
$env:DASHBOARD_TEST_CORES_DIR = (Resolve-Path .tmp/tests/fixtures/validation-cores).Path
pwsh -NoProfile -ExecutionPolicy Bypass -File .\tools\check.ps1 -IncludeWebViewIntegration -IncludeRealCoreIntegration
```

At release-candidate stage inspect the published directory and ZIP, not only the source or an old `dist`. The release sidecar records source inputs and validation scope. Do not treat a ZIP as evidence that manual UAC, tray, sleep and DPI acceptance occurred.

## 7. What checks should protect

- Behavior tests for request/reply schema, fresh bootstrap, explicit PID clearing, drafts/ACKs, same-endpoint sessions, config write/read order and core operations.
- Actual UI interactions for operation order, selection, menus, visibility and meaningful feedback; [visual contract](style.md) for retained appearance. CI on main/v2 and PRs runs CoreLayoutTests/CoreSetupLayoutTests in the real isolated WebView after the common build and uploads evidence. The fixture uses its own synthetic API, including configuration failures and read-only states. This remains scoped coverage; any newly registered gaps must stay visible until fixed.
- Build checks for packaged dependencies, resource completeness and startup eager dependency graph. The frontend product-routes test imports the production router/navigation and renders the actual sidebar: only Core/Proxies/Connections/Overview/Logs/Rules are exposed, browser setup stays separate, and desktop setup redirects to Core. Page bodies/sidebar extras are stubbed; router and navigation are not. The offline audit fixture verifies page-only incoming changes reach their actual feature checks.
- Focused manual/real integration checks for window controls, tray/suspension, both cores, permissions, sleep recovery, themes and DPI according to impact.

Do not require a variable called `lastKeys`, a particular `shallowRef` spelling or a component called MiniSparkline. If an implementation changes, identify the invariant the previous test protected, replace that evidence, then remove the obsolete assertion. Do not simply delete failed guards to pass the build.

## 8. Minimal record

Use `docs/upstream/vX.Y.Z.md`, not an additional competing merge guide:

```text
Upstream old/new tag and SHA; Dashboard starting/candidate revision
Change/source → decision → reason/cost → local adaptation or retired patch
Register: update affected intent/watch/check entries; resolve pending items only with an explicit decision
Validation: current commands, environment, results, unrun/blocked items
Deferred: condition that would trigger another review
```

Keep current baseline and product decisions accurate. Store generated inventories, diffs and run output under `.tmp/reports/` or release verification artifacts, not `docs/`; disposable upstream checkouts belong under `.tmp/experiments/`. Revisit old rejections when their premises change, but do not silently approve new product scope.
