# Storava Web — Design Language

## Intent

Storava should feel like a precise storage instrument: calm, local, trustworthy, and deep.
It is not an admin dashboard. The visual metaphor is an **atlas**—nested orbits, boundaries,
depth lines, and permission gates—rather than generic cards filled with invented metrics.

## Color system

| Token | Light | Dark | Use |
| --- | --- | --- | --- |
| Paper | `#F3F4EC` | `#071718` | Canvas |
| Surface | `#FBFCF6` | `#0D2526` | Primary working surfaces |
| Ink | `#071A1C` | `#EDF4E8` | Text and strong structure |
| Pine | `#0D3B39` | `#BAF36B` | Authority, primary actions |
| Lime | `#BAF36B` | `#BAF36B` | Permission, safe/local emphasis |
| Aqua | `#68D5CB` | `#68D5CB` | Processing, activity, browser capability |
| Amber | `#F3B860` | `#F3B860` | Caution |
| Coral | `#E16C60` | `#E16C60` | Blocked capability and risk |

Risk colors communicate severity only. Category colors will be expanded in Phase 3 and will
remain distinct from risk.

## Surface hierarchy

1. Paper canvas: calm background and large whitespace.
2. Flat surface: content blocks, tables, and dialogs.
3. Raised surface: capability console and modal, using one restrained shadow.
4. Instrument surface: deep teal for the atlas, progress, and local-processing views.

Borders carry most hierarchy. Shadows are reserved for real elevation.

## Typography

- English: self-hosted variable Manrope.
- Persian: self-hosted variable Vazirmatn.
- Large display text uses compact tracking and moderate weight instead of extreme bold.
- Technical status labels use small uppercase Latin text; Persian removes artificial
  tracking and uppercase behavior.
- Body copy targets a 65–75 character measure and generous line height.

## Scale and geometry

- Spacing follows a 4/8/12/16/24/32/48/72/112 px rhythm.
- Radii: 10 px controls, 18 px groups, 28 px cards/dialogs, 42 px instrument frames.
- Buttons use pill geometry only for direct actions and compact preferences.
- Cards are used only when content has a distinct boundary and purpose.

## Iconography

- Custom 1.8 px outline icons with round line caps.
- Lime-filled emblems indicate permission or local safety.
- No mixed icon libraries and no decorative emoji.
- Directional icons mirror under RTL; semantic icons do not.

## Motion

- 160–180 ms for direct interaction.
- Slow atlas scan motion is decorative and never represents progress.
- Real scan progress will use worker metrics and indeterminate state when total size is
  unknown.
- `prefers-reduced-motion` collapses transitions and animation.

## Interaction states

- Focus: three-pixel high-contrast ring with offset.
- Hover: one- to two-pixel lift only on actionable surfaces.
- Disabled: reduced contrast with unchanged layout.
- Success: aqua/lime and explicit text.
- Error/blocked: coral and explicit recovery text.
- Loading: structural skeletons; no spinner-only page.

## Localization and direction

- Language and theme update the root document live; no page reload.
- All visible Vue text comes from the locale resource map.
- Razor fallback, error, metadata, and no-script text use `.resx`.
- Layout uses logical CSS properties and selectively mirrors directional arrows.
- Persian typography and line height are tuned independently without changing the component
  hierarchy.

## Phase 1 honest states

- The landing atlas is explicitly labelled as waiting for a folder and contains no size,
  percentage, or file-count claims.
- Folder selection verifies permission only and explicitly says no scan starts in Phase 1.
- Capability indicators are derived from runtime browser APIs.
- Unsupported and fallback modes explain the limitation before permission is requested.

## Legibility floor

The landing page had drifted below what it could defend. Measured with composited alpha against
each element's real backdrop, ten pieces of text failed WCAG AA — tracked numerals at 2.39:1,
workflow indices at 2.86:1, the footer line at 3.69:1 — and thirty-three elements were set below
12px, the smallest at 9.3px.

None of that was visible as a mistake. Faint, tiny, tracked labels are a deliberate idiom here and
they look intentional right up until someone has to read one.

Two tokens now hold the line:

- `--label-xs` (0.72rem) and `--label-sm` (0.78rem) — the floor for micro-labels. Twenty
  declarations scattered between 0.58rem and 0.68rem now reference them. The idiom is unchanged;
  only the floor moved.
- `--muted-on-dark` and `--muted-on-deep` — faint text that is still legible, measured to clear
  4.5:1 on the surfaces they appear on. The failures all came from one-off hex values that bypassed
  the palette, which is exactly how a design system stops being one.

The result is zero contrast failures in both themes, and a smallest size of 11.2px.

The workspace at `/scan` was audited the same way and had no failures to begin with — worth
recording, because it means the drift was confined to the marketing surface rather than the tool.

### What measurement cannot settle

Contrast and type size come from computed styles and are reliable. Touch-target size depends on
layout, and the audit environment reports a zero-width viewport, so the enlarged header hit areas
are reasoned rather than verified. They are built so the visual design is untouched: the nav links
grow an invisible 44px band around themselves rather than being padded apart. That still wants a
look on a real device.
