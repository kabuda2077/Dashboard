# UI Style Guide

This project embeds a customized zashboard UI inside a Windows desktop shell. UI changes should preserve the existing visual language and reuse shared styles before adding new one-off classes.

## Core Principle

- Reuse existing tokens, utilities, components, and spacing patterns first.
- Add a new visual style only when an existing pattern cannot express the intended UI.
- When adding a new style, document why it is needed and where it should be reused.
- Prefer zashboard's existing component patterns over page-local custom styling.
- Keep desktop shell visuals and frontend visuals in the same DOM/CSS system whenever possible. C# should provide window APIs; frontend should own visual layout.

## Layout

- Main app layout is a left sidebar plus right content area.
- Desktop sidebar widths:
  - Expanded: `w-64`
  - Collapsed: `w-18`
- Sidebar width transitions use `duration-320 ease-[cubic-bezier(0.34,0.1,0.2,1)]`.
- Main content uses page padding of `p-3` and card/grid gaps of `gap-3`.
- Two-column settings/dashboard layouts should use `lg:gap-8` by default. Avoid returning to `lg:gap-12` unless a page has a documented layout reason.
- Wide content containers should usually use `mx-auto w-full max-w-7xl`.
- Avoid nested cards. Use cards for repeated items, modals, and framed tools only.

## Colors

Use DaisyUI theme tokens instead of raw colors.

- Prefer a small fixed set of semantic surface/text tokens.
- Do not introduce a new gray/neutral opacity token unless the current set cannot express the difference.
- Page background: `bg-base-200`
- Sidebar background: `bg-base-150`
- Primary surfaces: `bg-base-100`
- Secondary/control surfaces: `bg-base-200/70`
- Control hover surfaces: `hover:bg-base-200/80`
- Subtle sidebar/card panels: `bg-base-100/70`
- Main text: `text-base-content`
- Secondary/read-only text: `text-base-content/60`
- Very weak helper text or separators: `text-base-content/40`
- Subtle borders/dividers: `border-base-border`
- Top bar borders: `border-base-content/20`
- Brand/action emphasis: `primary`
- Running/status success: `success`
- Stopped/warning status: `warning`

Recommended meaning of each text level:

- `base-content`: primary labels, headings, actionable text.
- `base-content/60`: secondary labels, status text, section hints, and long read-only content.
- `base-content/40`: separators, arrows, or very lightweight metadata.

Avoid adding new text opacities like `/50`, `/70`, or `/80` unless there is a specific visual mismatch that the existing levels cannot solve.

`base-border` is defined as an 8% mix of `base-content` in `dashboard-src/src/assets/styles/override.css`. Keep it for low-emphasis dividers. Use `base-content/20` only where the border intentionally needs more presence, especially top bar controls.

Border policy:

- `base-border`: default divider for cards, lists, sidebar panels, and settings rows.
- `base-content/20`: top bar inputs/selects/tabs and any control that needs a clearer shell.
- Do not mix both border styles inside the same repeated component unless there is a deliberate hierarchy.

## Surfaces

Shared surfaces are defined in `dashboard-src/src/assets/styles/override.css`.

- `base-container`: `bg-base-100 overflow-hidden rounded-xl shadow-xs`
- `card`: `bg-base-100 rounded-xl shadow-xs`
- `collapse`: `bg-base-100 rounded-xl shadow-xs`
- `settings-grid`: `bg-base-100 grid grid-cols-1 overflow-hidden rounded-xl`
- `badge`: `bg-base-200/80`

For most new settings or dashboard panels, prefer `settings-grid`, `base-container`, or existing card components rather than creating custom wrappers.

Use only two broad panel families for custom desktop/Core UI:

- Content/settings panels: `settings-grid`, `setting-item`, and `settings-section-label`.
- Information/log panels: the same outer panel rhythm, with inner read-only content using `bg-base-200/70` and `text-base-content/60`.

Do not introduce a third card-like container with different padding, border, or background just for one Core page area.

## Typography

Keep type hierarchy restrained.

- Page/section title: `text-lg font-semibold`
- Settings section label: `settings-section-label`
- Setting row label: `setting-item-label`
- Normal control text: `text-sm`
- Logs, helper metadata, and compact secondary rows: `text-xs`
- Important inline labels may use `font-medium` or `font-semibold`.

Existing settings definitions:

- `settings-section-label`: `text-xs font-semibold tracking-wider uppercase`, color equivalent to `base-content/60`.
- `setting-item-label`: `text-sm font-medium`.
- Small inputs/buttons/selects/tabs are normalized to `text-sm`.
- Toggles use the shared `.toggle` utility: `bg-base-100`, `border-base-border`, and a `base-content` thumb.

Use `text-base-content/60` for read-only or low-priority content that must remain readable, such as core paths and log text. Use `text-base-content/40` sparingly for decorative separators like arrows.

## Borders And Radius

- Keep radius vocabulary small and stable.
- Standard cards and settings panels use `rounded-xl`.
- Compact top bar controls use `rounded-lg`.
- Inner pills and small repeated items may use `rounded-box` or `rounded-full`.
- Settings rows use `border-base-border border-b`; the last row removes the bottom border.
- Top bar inputs, selects, and tabs use `border-base-content/20`.
- Avoid adding heavy shadows. Current default is `shadow-xs`; many custom controls use `shadow-none`.

## Canonical Reusable Patterns

When adding or changing UI, prefer these existing patterns first:

- `settings-grid` + `setting-item` + `setting-item-label`
- `base-container` / `card`
- `CtrlsBar` + shared top bar select/input styles
- `bg-base-200/70` secondary control surfaces
- `text-base-content/60` for muted/read-only text
- `border-base-border` for regular separators

If a new pattern is unavoidable, document why it is different from the canonical set above.

## Top Bar

Top bars are rendered through `CtrlsBar`.

- Use `CtrlsBar` for page-level controls.
- The visual bar itself is transparent; individual controls provide the visible surface.
- Top bar control height is `2.25rem` / 36px.
- `ctrls-search` defines the standard desktop search width:
  - `width: 20rem`
  - `min-width: 12rem`
  - `max-width: min(20rem, 32vw)`
- Top bar inputs and selects should reuse the shared rule:
  - `border-base-content/20 bg-base-100 rounded-lg border shadow-none`
- Top bar circular buttons use:
  - `bg-base-100 shadow-xs hover:bg-base-200`
  - width/height `2.25rem`
- Window controls are separate from zashboard controls but aligned in the same top row.
- Window controls are independent circular buttons using the same top-control surface family; the close button may use `error` on hover.
- Core page status controls are a top bar variant: the status box follows top bar border/radius rules, while its action buttons keep the documented 34px semantic-button exception.

Do not add page-specific top bar borders, shadows, or heights unless the shared top bar rules cannot handle the case.

## Sidebar

- Sidebar uses `sidebar border-base-border bg-base-150 text-base-content`.
- Menu item gap is `gap-1`.
- Menu icons use `h-5 w-5`.
- Expanded sidebar content width is `w-60`; collapsed content width is `w-18`.
- Expanded lower sidebar panels use `border-base-border bg-base-100/70 rounded-xl border shadow-none`.
- Keep collapsed sidebar styling close to the upstream zashboard behavior. Avoid custom collapsed-only visual rewrites unless fixing alignment.

## Settings Pages

Settings should follow the shared settings system.

- Group title outside cards: `settings-section-label`
- Card wrapper: `settings-grid`
- Row: `setting-item`
- Row label: `setting-item-label`
- Row minimum height: `min-h-11`
- Row horizontal padding: `px-4`
- Row gap: `gap-3`

Backend/Core settings are embedded into the Core page, but they should still use these shared settings classes.

## Core Page

The Core page combines desktop app controls and zashboard settings.

- Top status row height: `h-9` / 36px.
- Core top action buttons height: 34px.
- Core top action button colors are a deliberate semantic exception:
  - Switch: `btn-primary`
  - Start: `btn-success`
  - Stop: `btn-warning`
- Core status box border: `border-base-content/20`.
- Core status title: `font-semibold`.
- Runtime status text: `text-xs text-base-content/60`.
- Core path/config/API/Secret inputs: `input input-sm dashboard-input`.
- Core log text: `text-xs leading-5 text-base-content/60`.
- Core operation buttons: `btn btn-sm dashboard-action-btn`.
- Core and embedded backend two-column layouts should use `lg:gap-8`.

Avoid introducing a second settings style in Core page. Prefer the same `settings-grid`, `setting-item`, and `settings-section-label` used elsewhere.

## Buttons And Controls

- Prefer three button families:
  - soft action buttons
  - top bar icon buttons
  - primary/status buttons
- Reuse one of these families before creating a new button shape.

- Standard small buttons: `btn btn-sm`.
- Neutral utility buttons in panels: `btn btn-sm dashboard-action-btn`; this is the only soft-button supplement allowed to set `font-weight: 600`, keeping Chinese labels clear without changing size or spacing.
- Primary action: `btn-primary`.
- Start/status action: `btn-success` only where the action truly means start/running.
- Warning/destructive stop action: `btn-warning` when it represents warning/stop rather than deletion.
- Icon-only top bar buttons should be circular: `btn btn-circle btn-sm`.
- Use existing icon libraries already used by the project, especially Heroicons in current components.
- Avoid visible focus black outlines. Reuse the current zashboard-style subtle purple focus ring instead of removing focus feedback.

Potential utility extraction candidates:

- `surface-soft`: reusable `bg-base-200/70` inner data/control surface.
- `text-muted`: reusable `text-base-content/60` for secondary and read-only values.

Do not create more button utilities unless at least two or three call sites clearly benefit from the extraction. `dashboard-action-btn` is already the canonical soft action supplement.

When extracting DaisyUI control styles, keep DaisyUI base classes on the element and use project utilities only as supplements:

- Good: `class="input input-sm dashboard-input"`
- Good: `class="btn btn-sm dashboard-action-btn"`
- Avoid: `class="dashboard-input"` or `class="dashboard-action-btn"` when the element depends on DaisyUI sizing, font, or state rules.

MiSans must be loaded with a valid variable font-weight range. The Vite build fixes the upstream `subsetted-fonts/MiSans-VF` CSS from invalid `font-weight: undefined` to `font-weight: 100 900`; do not work around that font issue by adding page-specific button weights.

## Data Cards

- Use `bg-base-200/70` for inner read-only result rows or compact data strips.
- Use `text-sm` for primary row content.
- Use `text-xs text-base-content/60` for metadata.
- Use `text-base-content/40` for separators.
- Use stable heights and grid rows when card height must remain aligned across columns.

## Responsive Behavior

- Desktop uses sidebar plus fixed top controls.
- Middle/mobile screens use bottom dock navigation.
- Avoid viewport-scaled font sizes.
- Preserve text fit with `truncate`, `min-w-0`, stable widths, or wrapping as appropriate.
- Do not let top controls overlap the window controls. Use existing `CtrlsBar` sizing and content max-width behavior.
- Core page top controls should be measured against the available right content area and reserved window-control width, so they do not drift during sidebar expand/collapse.
- Two-column custom sections must collapse naturally to one column on narrow windows.
- Fixed-width action buttons must keep text on one line.

## When Adding New Styles

Before adding a new class or token, check:

1. Can this use `settings-grid`, `setting-item`, `base-container`, `card`, or `collapse`?
2. Can this use existing `base-*`, `primary`, `success`, or `warning` tokens?
3. Can this use `text-base-content/60` or `/40` instead of a new color?
4. Can this use existing top bar, sidebar, or settings patterns?

If a new style is still needed, include a short comment or document the reason in this file. The reason should explain the reusable purpose, not only the current page.
