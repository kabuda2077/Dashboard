# Dashboard UI rules

The objective is a coherent Windows dashboard using zashboard's visual language, not permanent preservation of today's component names or layout algorithm. Product decisions and upstream workflow live in [upstream-merge.md](upstream-merge.md); maintenance and remaining acceptance boundaries live in [maintenance.md](maintenance.md).

## Stable principles

- Reuse existing theme tokens, controls and surface patterns before inventing a new local family.
- The frontend owns visible layout and control appearance. Native code provides window/process/system operations.
- UI must remain usable with keyboard focus, long labels, light/dark themes, normal/maximized windows and supported DPI.
- Text truncates inside its allocated area, never over action buttons. Loading, disabled and failure states must be distinguishable.
- User drafts are not overwritten by unrelated runtime updates. Destructive actions and abandoning unsaved data require clear feedback.
- Use ordinary text for external/host error messages. Rich text needs an explicit sanitized path.
- Prefer natural Grid/Flex/sticky layout to repeated measuring loops. Use ResizeObserver only where content requires measurement.
- Better upstream controls, rendering and accessibility can replace local implementations when these principles and current product behavior remain satisfied.

## Current defaults, not immutable implementation

- Sidebar expanded 16rem and collapsed 4.5rem, a restrained 320ms transition. A shared `--sidebar-width` expresses the content/top-bar relationship.
- Typical page padding/gaps are `p-3` / `gap-3`. Wide settings containers use max-w-7xl.
- Core keeps profile controls/output together, followed by backend controls and folded general settings. The runtime toolbar spans the profile column at wide widths, using the same centered content geometry and scrollbar space. The status lamp follows v1.2.2's optical alignment: its 12px body is inset 2px from the profile card's left edge, the status box is inset 30px (16px clear gap), and its 4px glow uses the state color at 30% opacity. Center the lamp vertically on the status box even when action buttons wrap. It has an external status lamp, a shrinkable status box, then three fixed buttons in Switch, Start, Stop order. Both Start and Stop remain visible with the unavailable action disabled; no profile editor selector belongs in this row. Native window controls remain at the viewport's top right with reserved space; scrolling must not hide or overlap either control group.
- Core profile/output and embedded backend/downloads share column widths, outer padding and column gap. Embedded children must not add another outer inset. At sufficient actual content width, both sections use two columns; otherwise both stack.
- Core profile rows use compact vertical padding (6px per side with 32px inputs), keeping single-line rows approximately the same 44px height as desktop options, allowing the divider's 1px difference. The profile card to desktop-options heading gap is explicitly 16px, not conflicting utility margins. Wrapped rows grow naturally.
- Profile field labels, inputs and file/action buttons share a row when space permits, with a common label column and shrinkable inputs. Narrow layouts put labels above the fields. Long paths/translations must not clip buttons or create horizontal page overflow.
- Output has a complete card surface and an inner read-only log area. In two columns its outer edges align with the profile/options column. Appending logs scrolls inside the area without growing the page or changing the card height. Express alignment through layout rather than fixed-duration RAF loops.
- Settings use one column below 1000px of actual available content and two columns above it. Changing the measuring/rendering technique is allowed; do not restore historical arbitrary-hidden-item metadata as an invisible compatibility requirement.
- General top controls are approximately 36px; Core compact actions currently use 34px height/72px width. Long translations must remain usable; these sizes can evolve with demonstrated need.
- The Core configuration heading does not repeat the active core name. Save belongs beside Secret, is disabled when unchanged, and does not occupy a permanent footer row. Dirty/conflict/restart notices remain visible when applicable.
- Runtime text displays the core name and PID only, without repeating running/readiness labels. The lamp is green only while the core is running and its API is ready; otherwise it is yellow. Its accessible label and the lamp/status-box native tooltip expose detailed running/connection status. The log card is titled “内核日志” / “Core logs”. Compact runtime toolbars hide PID below 560px of available toolbar content; the tooltip preserves it. Do not add a JavaScript measurement loop for this.
- Reload/discard confirmation invoked from a native Core modal stays inside that same modal, with cancellation and stale-context protection; an external div dialog is not an equivalent replacement.
- Lazy settings must finish initial async rendering before measuring columns or honoring a settings deep link. Collapsing settings or choosing another target cancels the pending navigation; do not repeatedly reposition on a timer.
- Normal Core configuration follows the active core. Switching opens a dialog that identifies and edits the target's independent draft; only confirmation saves/changes runtime and cancellation preserves both drafts. Keep selection and path/API editing out of the runtime toolbar.
- Fresh current-format profiles show a full first-time setup dialog with mihomo/sing-box choices, executable/config/API/Secret fields, file selection and core-specific API help. Start runs the selected draft through the same command path. API readiness and a successful completeSetup ACK are required before dismissing the guide; failures keep it visible and explain the result. Do not replace this flow with a banner.
- Current Core maintenance order and network latency presentation are product defaults documented in the upstream guide, not checks against literal source strings.
- The initial Core page must not eagerly load all charts/settings previews through a hidden static import. Render optional previews only when needed.

## Shared tokens and surfaces

Use DaisyUI semantic tokens: base-200 page/sidebar; base-100 main surfaces; base-200/70 secondary read-only controls; base-content text; base-content/60 secondary text; base-content/40 weak metadata; primary actions; success running state; warning attention state.

`base-border` is the low-emphasis divider token; base-content/20 is reserved for clearer control outlines. Reuse existing opacity levels rather than inventing new nearly-identical grays.

`base-container`, `settings-grid`, `setting-item`, `settings-section-label` and DaisyUI controls are the shared vocabulary. These names can change during an intentional cleanup; preserve the resulting consistency and update consumers together.

Avoid unnecessary nested cards. Respect virtual-row measurement rather than wrapping every row in a new surface. A cached/paused chart must not repeatedly redraw just because it remains mounted.

Current shared CSS is in `assets/styles/components/`; `dashboard-desktop.css` is the last override import for real desktop exceptions. This is today's ownership arrangement, not permission to put every new rule in overrides. If replaced, verify the resulting cascade rather than keeping an unused file to satisfy a string check.

## Controls and focus

- Choose one suitable shared select/menu implementation when its interactions are equivalent. Do not preserve duplicate controls merely because one is local.
- Menus close appropriately on selection, outside click and Escape; focus remains visible and keyboard access works.
- Read-only TUN follows the disabled control appearance and reports its source. Do not make unknown state look like a confirmed false value.
- Keep native window buttons accessible, reserve their space and exclude ordinary interactive controls from draggable regions.
- Existing mobile/browser interactions remain deliberate: horizontal tables, text inputs and vertical scrolling should not be hijacked by page swipes.

## Verification

Use real component interaction tests and a small real WebView visual matrix. At minimum inspect Core/Settings, a proxy list, connection/log/rule tables, overview charts, expanded/collapsed sidebar, light/dark and high DPI for changes affecting those areas.

CoreLayoutTests uses the production WebView to check actual toolbar/card edges, column widths, field placement, output surfaces, internal scroll and window-control separation. It exercises Chinese/light and English/dark at several widths and sidebar states, including a test-only window below the production desktop minimum. Screenshots in `.tmp/reports/core-layout/` support visual inspection; they do not prove physical DPI, touch or multiple-monitor acceptance.

A selector existing in CSS is not a visual pass. A renamed renderer is not a failure if behavior and appearance are preserved. Record intentional default/product changes with a reason and the relevant owner approval rather than freezing implementation details indefinitely.
