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

## 2. Four levels of constraints

| Level | Meaning | Decision rule |
| --- | --- | --- |
| Guarantees | No plaintext persisted credentials; no untrusted native commands; correct session isolation, save acknowledgements and failure recovery; preserve data created by the supported format during normal operation | Must not be weakened for merge convenience. Implementations may change if evidence preserves the guarantee |
| Current product choices | Single active mihomo/sing-box, Clash API, desktop process ownership, Core-first desktop workflow, portable deployment, current page/operation scope | Keep by default; propose changes to the product owner rather than silently importing them |
| Implementation/defaults | Specific classes, filenames, refs, caches, layout technique, component internals, local visual defaults | May be replaced with a better solution while satisfying the preceding levels; update relevant tests/rules together |
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
- Core runtime toolbar follows the v1.2.2 status/Switch/Start/Stop arrangement, with independent window controls and no profile editor selector. Normal configuration follows the active core; the switch dialog identifies/edits the target draft and makes save/switch explicit. Cancellation preserves drafts and changes no runtime. Preserve the status lamp's v1.2.2 optical alignment (12px body at card-left +2px, status box at +30px, 4px/30% glow, vertically centered on the status box); omit the redundant core-name suffix in the profile heading and keep Save beside Secret.
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

1. Preserve the working tree and establish D0; create a dedicated branch for this upstream update.
2. Fetch U0/U1 into a separate upstream checkout, retaining exact refs. Release notes are navigation; tag-to-tag diff and commit intent are evidence.
3. Generate the upstream delta and identify overlap with current local changes plus dependency reach. Avoid rebuilding the entire historical audit on every patch release.
4. Decide feature/implementation treatment before mechanically resolving overlaps. Escalate changes to scope, permissions, data destination or major workflow; ordinary compatible fixes and proven equivalent replacements need no per-line approval.
5. Integrate coherent dependency groups. Prefer small reviewable commits; avoid unrelated reformatting, whole-lockfile ours/theirs and simultaneous wholesale local restructuring.
6. When upstream replaces a local fix, remove the obsolete local mechanism after verifying the replacement. Do not keep both implementations indefinitely behind another switch.
7. Resolve package changes first, then update the lockfile using the pinned package manager. Build outputs remain generated, not source commits.
8. Run targeted tests while developing, then the common Check entry for the candidate. Update tests that freeze implementation only after replacing their protected behavior with equivalent evidence.
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

Both use the same automated verification stages. Release rechecks its own inputs before publishing and packaging. Internal front-end build, icon generation and resource assertions are not a separate user workflow.

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
- Actual UI interactions for operation order, selection, menus, visibility and meaningful feedback; [style rules](style.md) for appearance.
- Build checks for packaged dependencies, resource completeness, startup eager dependency graph and excluded product entry points.
- Focused manual/real integration checks for window controls, tray/suspension, both cores, permissions, sleep recovery, themes and DPI according to impact.

Do not require a variable called `lastKeys`, a particular `shallowRef` spelling or a component called MiniSparkline. If an implementation changes, identify the invariant the previous test protected, replace that evidence, then remove the obsolete assertion. Do not simply delete failed guards to pass the build.

## 8. Minimal record

Use `docs/upstream/vX.Y.Z.md`, not an additional competing merge guide:

```text
Upstream old/new tag and SHA; Dashboard starting/candidate revision
Change/source → decision → reason/cost → local adaptation or retired patch
Validation: current commands, environment, results, unrun/blocked items
Deferred: condition that would trigger another review
```

Keep current baseline and product decisions accurate. Store generated inventories, diffs and run output under `.tmp/reports/` or release verification artifacts, not `docs/`; disposable upstream checkouts belong under `.tmp/experiments/`. Revisit old rejections when their premises change, but do not silently approve new product scope.
