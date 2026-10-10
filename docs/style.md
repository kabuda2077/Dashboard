# Dashboard UI contract

**Reference:** Dashboard `v1.3.2` (`3089aca`). It is the UI/UX and behavior reference, not an implementation template. Keep v2's host protocol, persistence and component architecture. The reviewed upstream source is zashboard `v3.27.0`; these are two different baselines. New brand icons and mobile overlay synchronization remain pending comparisons in the change register.

Product/update rules live in [upstream-merge.md](upstream-merge.md), system boundaries in [maintenance.md](maintenance.md). The small [local-change register](upstream/local-changes.json) records origin, intent, checks, pending choices and known gaps. Generated inventories/screenshots belong in `.tmp/reports/`, not in this document.

## What may change

| Level | Rule |
| --- | --- |
| Guarantees | Do not weaken trust, persistence ACKs, session isolation, failure feedback, draft safety or accessibility for a merge/refactor. |
| Retained product/visual contract | Preserve product behavior and a compact, aligned, consistent appearance by default. Equivalent sizes or implementations may follow upstream with evidence. Obvious or uncertain visual changes need before/after comparison and a product/design decision. |
| Pending choices | Preserve the current implementation while a decision is pending, but do **not** label it approved or refresh the reference to bless it. |
| Implementation | Components, filenames, Grid/Flex/container queries, observers, caches and renderer internals are replaceable when the preceding levels remain satisfied. |

Neither `v1.3.2` nor today's v2 is a pixel-perfect specification for every state. Known bugs are not features to preserve. The six initial v2 choices were explicitly accepted in the UI follow-up scope below; future pending choices still require a decision, not just a green build.

## Retained visual contract

Nominal sizes below are the **current reference in CSS pixels at the default text size**, not permanent pixel locks or physical display pixels. The retained result is compact density, alignment, consistent controls, usable text/focus/zoom and safe interaction. Equivalent upstream sizes or layout techniques may replace them when consumer checks establish that result. For obvious or uncertain changes, compare matched before/after views and obtain the design decision before changing the reference or assertions; a new component's defaults alone are not evidence.

| Surface/control | Retained result |
| --- | --- |
| Core actions | Switch, Start, Stop in that order; all remain visible, unavailable operations disabled. Nominal buttons **72×34**, **8px** between actions. No profile selector in the runtime row. |
| Runtime status | Status box height **36px**. Lamp **12×12**, body at profile-card-left **+2px**, status box at **+30px**, leaving a **16px** gap. Vertically center lamp on status box, including wrapped actions. Glow **4px**, state color at **30%** opacity. |
| Core fields | Input height **32px**; rows share an intrinsic label column capped at **7.5rem**, rather than always reserving that width. Labels and shrinkable inputs share a row when space permits; narrow fields stack. File/action buttons must not be clipped. Save stays beside Secret, not in a permanent footer. |
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
- Unknown TUN/API state is not false. Loading, failure, unknown, disabled and read-only states must be distinguishable; read-only TUN reports its source. Session authentication/connection errors remain visible after data reset. Retry re-probes the host when its session is not ready; ordinary configuration read failures retry the API. Failed writes restore the last confirmed switch value.
- Secret starts masked, with an explicit accessible show/hide button inside the input area. Visibility is local, never persisted, and resets on leaving the field or changing the profile. Unreadable credentials cannot be revealed without explicit replacement.
- External/host error messages are ordinary text. Rich content needs a separate sanitized path. Copy/save success reflects an actual successful operation.
- Menus support selection, outside-click/Escape dismissal and visible keyboard focus. Horizontal tables, editing and vertical scrolling must not be hijacked by page gestures.
- Current accepted page capabilities (proxy folders, grouping/filtering, labels, histories, backgrounds and latency tests) remain available. Unused props and unsupported native-API branches are not capabilities to preserve.

## Accepted v2 follow-up choices

The user approved the following six-item scope before implementation. Their original pending entries remain in Git history; [local-changes.json](upstream/local-changes.json) records the decision evidence and current check ownership. New undecided changes belong in `pending`, not in an automatically refreshed reference.

| Decision | Current contract |
| --- | --- |
| `core-density` | Retain separate profile/options cards, compact rows (about 45px including divider) and the 16px profile-to-options heading gap. Do not re-merge the cards. |
| `core-breakpoint` | Retain stacking below 1000px of **actual content**; 1120px with expanded sidebar may be single-column. |
| `field-space` | Share natural label width so Chinese does not reserve 120px. Preserve usable English labels, long paths and fixed action buttons. Use native layout, not a measurement loop. |
| `runtime-text` | Retain simplified runtime text and PID's 560px content threshold. Full running/readiness details remain in tooltips/accessibility labels; failures and checking states must be meaningful. |
| `dialog-density` | Retain 768px setup/target-edit dialogs clamped to narrow windows, independent target drafts, and native modal behavior. Fix clipping/focus/scroll issues without reverting the structure. |
| `secret-reveal` | Retain default masking and add explicit show/hide; visibility is not a stored preference. |

These are deliberate v2 adaptations, not accidental divergence from the 1.3.2 reference. Further changes can still be proposed with their reason and before/after evidence.

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
2. Run behavior tests for the affected interaction and actual DOM geometry checks for the current reference and retained relationships. Equivalent upstream dimensions may update size assertions when comparison and consumer checks preserve compactness, alignment, consistency and usability. Keep screenshot review for density, surfaces and interaction states that coordinates alone miss.
3. CoreLayoutTests checks action sizes/gaps, status/input heights, optical alignment, compact Chinese labels, retained background/scroll/focus on cancel, and 36px/8px-radius selects on Proxies/Connections/Logs. It waits for the target page rather than measuring a fading previous page.
4. CI runs CoreLayoutTests and CoreSetupLayoutTests against the built UI and isolated host/profile/API ports, and uploads JSON/screenshots/TRX. All API data comes from SyntheticClashApi, never the default 9090 endpoint. Offscreen focus is explicitly emulated for the Secret interaction test without activating a desktop window.
5. The initial `switch-scroll`, `toolbar-select-size` and `unknown-config-state` gaps are fixed and guarded. Configuration error/retry, unknown, host read-only and native authentication/unreachable states are rendered against the synthetic API; component tests also cover malformed data and failed-write rollback. CoreProfileFields tests cover masking, focus, profile changes and unreadable credentials. Removing a gap does not imply that physical multi-monitor/DPI, every dialog or actual core/TUN/UAC scenarios have been accepted.
6. Before accepting an upstream common-control/style/dependency change, check its desktop consumers even if their files did not change. The audit's explicit watch patterns are a minimum review map, not a complete static dependency graph.
7. For an equivalent implementation/size adjustment, record its reason and evidence and update the affected reference/checks together. For an obvious or uncertain change to the retained experience, show before/after comparison and get the product/design decision first. Do not refresh screenshots or remove assertions merely because an implementation changed.

Keep the review matrix small but real: Core/settings, proxies, connections/logs/rules, overview; expanded/collapsed sidebar, light/dark, Chinese/English, constrained and wide layouts. Select extra error/busy/conflict, focus, background and zoom cases according to impact. System acceptance boundaries remain in maintenance.md.
