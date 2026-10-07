# Dashboard UI contract

**Reference:** Dashboard `v1.3.2` (`3089aca`). It is the UI/UX and behavior reference, not an implementation template. Keep v2's host protocol, persistence and component architecture. The reviewed upstream source is still zashboard `v3.26.0`; these are two different baselines.

Product/update rules live in [upstream-merge.md](upstream-merge.md), system boundaries in [maintenance.md](maintenance.md). The small [local-change register](upstream/local-changes.json) records origin, intent, checks, pending choices and known gaps. Generated inventories/screenshots belong in `.tmp/reports/`, not in this document.

## What may change

| Level | Rule |
| --- | --- |
| Guarantees | Do not weaken trust, persistence ACKs, session isolation, failure feedback, draft safety or accessibility for a merge/refactor. |
| Retained product/visual contract | Preserve the behavior and appearance below by default. A deliberate change needs an explicit product/design decision and before/after evidence. These are not disposable implementation defaults. |
| Pending choices | Preserve the current implementation while a decision is pending, but do **not** label it approved or refresh the reference to bless it. |
| Implementation | Components, filenames, Grid/Flex/container queries, observers, caches and renderer internals are replaceable when the preceding levels remain satisfied. |

Neither `v1.3.2` nor today's v2 is a pixel-perfect specification for every state. Known bugs are not features to preserve. In particular, a green build or a test of v2's current layout does not decide the pending choices below.

## Retained visual contract

Nominal sizes are **CSS pixels at the default text size**, not physical display pixels. Long labels, keyboard focus and supported zoom must remain usable. Deliberate responsive exceptions need a stated condition and evidence; do not silently substitute a new component's defaults.

| Surface/control | Retained result |
| --- | --- |
| Core actions | Switch, Start, Stop in that order; all remain visible, unavailable operations disabled. Nominal buttons **72×34**, **8px** between actions. No profile selector in the runtime row. |
| Runtime status | Status box height **36px**. Lamp **12×12**, body at profile-card-left **+2px**, status box at **+30px**, leaving a **16px** gap. Vertically center lamp on status box, including wrapped actions. Glow **4px**, state color at **30%** opacity. |
| Core fields | Input height **32px**; labels and shrinkable inputs share a row when space permits. File/action buttons must not be clipped. Save stays beside Secret, not in a permanent footer. |
| Other desktop toolbars | Shared control height **36px**, including selects and circular buttons. Preserve controlled popup border, spacing and selected indication; **DropdownSelect is not a required component name**. |
| Sidebar | Expanded **16rem**, collapsed **4.5rem**, restrained **320ms** transition; content and toolbar remain aligned during transition. |
| Window controls | Independent at the viewport's top right with reserved space. Scrolling must not hide or overlap them; ordinary interactive controls must not initiate drag/resize. |
| Logs | A complete card and an inner read-only scrolling area. Appending logs must not grow the card/page. Preserve text wrapping and discernible loading/empty/error states. |
| Settings | Embedded in Core, normally folded. Lazy content finishes before column measurement/deep-link positioning. Cancelling/changing the target cancels pending navigation. |

Use relative geometry (for example lamp versus card), not a global `left=276px`. Viewport, sidebar state, scrollbar, font and language must be recorded for absolute-coordinate comparisons.

### Why these are retained

- `5fdf3b7` (2026-06-15): Core 72×34 actions, 8px spacing, 36px status, compact fields were explicitly introduced; they are not accidental upstream defaults.
- `eb0edc7` (2026-06-23): Core toolbar resize correction protected separation from window controls.
- `e743fb1` (2026-06-27): desktop-owned override layer protected those controls from upstream changes.
- `750c162` (2026-06-27): restored top-bar styling and controlled dropdown appearance because native WebView select popups were not reliably styleable.
- `517d6b3` is a v2 checkpoint, **not proof that every changed dimension was separately approved**. Prefer documented intent over inferring approval from a large commit.

## Behavior that accompanies the visual contract

- Native code owns processes/windows/system actions; frontend code owns visible layout. Do not import upstream actions that bypass the trusted host.
- Normal configuration follows the active core. Switching identifies and edits the target's independent draft; cancellation changes neither runtime nor stored profiles. **Opening/cancelling must retain the background layout and scroll context.**
- Runtime updates do not overwrite user drafts. Dirty/conflict/restart notices remain visible. Save is disabled when unchanged; failure must not look like success.
- First-time setup remains a full dialog with both cores, paths, API, Secret, file selection and core-specific help. API readiness plus a durable completeSetup ACK gates dismissal. v2's native dialog and independent drafts need not match 1.x's modal implementation.
- Reload/discard confirmation invoked from the Core native dialog remains inside it, with cancellation, focus and stale-context protection.
- Unknown TUN/API state is not false. Loading, failure, unknown, disabled and read-only states must be distinguishable; read-only TUN reports its source.
- External/host error messages are ordinary text. Rich content needs a separate sanitized path. Copy/save success reflects an actual successful operation.
- Menus support selection, outside-click/Escape dismissal and visible keyboard focus. Horizontal tables, editing and vertical scrolling must not be hijacked by page gestures.
- Current accepted page capabilities (proxy folders, grouping/filtering, labels, histories, backgrounds and latency tests) remain available. Unused props and unsupported native-API branches are not capabilities to preserve.

## Pending visual choices — do not silently finalize

The authoritative pending IDs and questions are in [local-changes.json](upstream/local-changes.json). In particular:

| ID | Current v2 characterization, not final approval |
| --- | --- |
| `core-density` | Separate profile/options cards; approximately 45px profile rows including divider; 16px profile-to-options heading gap; different outer padding/column gap. |
| `core-breakpoint` | Core stacks below 1000px of **actual content**, so 1120px with expanded sidebar differs from the reference. |
| `field-space` | 120px labels and added gaps reduce path input width despite a wider card; API retry also consumes width. |
| `runtime-text` | Running/readiness text is moved to tooltip/accessibility labels; PID hides below 560px of toolbar content. |
| `dialog-density` | 768px setup/target-edit dialogs versus the reference's smaller setup/confirmation surfaces. |
| `secret-reveal` | Password input is retained; whether to add explicit reveal remains undecided. |

Do not merge cards or restore old measurement loops just to match the reference. Likewise do not treat a current container-query threshold as immutable. Choose the user-visible goal first, then retain the simplest v2 implementation that achieves it.

## Style ownership and shared vocabulary

- Reuse DaisyUI semantic colors and existing tokens: base-200 page/sidebar, base-100 surfaces, base-200/70 secondary/read-only surfaces, base-content text, /60 secondary text, /40 weak metadata; primary/success/warning actions.
- `base-border` is the low-emphasis divider; base-content/20 is the clearer control outline. Reuse the established opacity/radius family instead of inventing near-duplicates.
- `base-container`, `settings-grid`, `setting-item` and `settings-section-label` are today's shared vocabulary, not permanent API names. Avoid nested cards around every virtual row.
- `dashboard-desktop.css` remains the last override import for desktop exceptions. Local presentation belongs in the consumer/desktop layer when an upstream component can stay unchanged. Do not turn every fix into a broad global `!important` override.
- Only the **three explicitly declared mirrors** in local-changes.json are currently required to match U0 after LF normalization. This is not a promise that every stylesheet or common component is untouched upstream code. Other shared files may contain explained local patches.
- For a mirror update, compare the real upstream blob at the new reviewed SHA; never copy the local hash into the manifest just to pass. If a local patch becomes necessary, explicitly reclassify ownership and record why.
- Prefer natural Grid/Flex/sticky layout to repeated measurement loops. A cached/paused chart must not redraw just because it remains mounted. Removed dead flexibility does not require visual compatibility work.

## Verification and changing the contract

1. Use identical fixtures, fonts, themes, locale and viewport when comparing. Record source SHAs and dirty input; volatile log/time content is not a reliable screenshot oracle.
2. Run behavior tests for the affected interaction, and actual DOM geometry checks for the retained sizes/relationships. Keep screenshot review for information density, surfaces and interaction states that coordinates alone miss.
3. CoreLayoutTests now explicitly checks retained action sizes/gaps, status/input heights and optical alignment. Its other density/breakpoint assertions characterize current v2; they are **not approval of pending designs**.
4. CI runs these Core checks against the built UI in an isolated production WebView host, and uploads JSON/screenshots/TRX. This does not cover every toolbar, all dialogs, physical multi-monitor/DPI, TUN or UAC.
5. The register explicitly retains known gaps (`switch-scroll`, `toolbar-select-size`, `unknown-config-state`). Source/mirror checks may pass while those remain unresolved; report them, do not call the whole visual contract green.
6. Before accepting an upstream common-control/style/dependency change, check its desktop consumers even if their files did not change. The audit's explicit watch patterns are a minimum review map, not a complete static dependency graph.
7. To intentionally change a retained result: state the reason, show before/after evidence, get the product/design decision, then update this contract and its checks together. Do not refresh screenshots or remove assertions merely because an implementation changed.

Keep the review matrix small but real: Core/settings, proxies, connections/logs/rules, overview; expanded/collapsed sidebar, light/dark, Chinese/English, constrained and wide layouts. Select extra error/busy/conflict, focus, background and zoom cases according to impact. System acceptance boundaries remain in maintenance.md.
