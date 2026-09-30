# AirType Reference Design DNA

## Method and source

The sole visual source is `Finance Dashboard.jpg` (736 × 552 px). The image was inspected at native resolution, sampled in flat regions, quantized in 8-value RGB buckets to reduce JPEG noise, and compared across adjacent pixels. Measurements below are image-space measurements; implementation values are translated into WPF device-independent pixels rather than copied blindly.

## 1. Philosophy, mood, and personality

The reference is calm, editorial, and quietly premium rather than technological or neon. It creates trust with a warm near-monochrome shell, large areas of breathing room, charcoal anchors, and one restrained cornflower-blue feature surface. Hierarchy comes from scale and weight first, then from tone; saturated color is used sparingly. The rounded outer shell and nested rounded cards make the product feel approachable, while the dark navigation rail and promo panel prevent the pale composition from becoming weak. Density is medium-high, but every cluster is bounded and generously padded, so it reads as organized rather than busy. Shadows are soft, broad, and neutral, suggesting diffuse light from above. The overall strategy is “soft structure”: strong grouping without heavy dividers, confident typography without decorative excess, and accents that occupy a small minority of the screen.

## 2. Color system

### Sampled palette

| Token role | Sample / implementation HEX | Evidence and role |
|---|---:|---|
| `canvas` | `#C3C3C3` | Exact samples at (5,5) and (20,275); neutral outer field. |
| `shell` / `surface1` | `#EBEAE8` | Sample around (300,150), dominant shell quantization near `#E8E8E8`. |
| `surface2` | `#F7F6F4` | Exact sample at (110,180); raised card interior. |
| `surface3` | `#E1E0DE` | Repeated quantized card separation tone around `#E0E0E0`. |
| `raised` | `#F2F0F1` | Exact sample near header at (90,70). |
| `overlayDark` | `#2D2D2D` | Exact dark promo sample at (500,450); sidebar clusters at `#303030`. |
| `overlayDarkPressed` | `#232323` | Dark inner/edge clusters in sidebar and promo. |
| `stroke` | `#F7F6F4` | Pale one-pixel card outlines visible against `#EBEAE8`. |
| `strokeStrong` | `#A9AAA8` | Neutral control outlines and subdued icon strokes. |
| `accent` | `#82A0CF` | Hero card sample at (360,180); quantized family `#80A0D8`–`#98B0E0`. |
| `accentHover` | `#7595C7` | Darker observed blue family, used for interactive hover. |
| `accentPressed` | `#6888BB` | Pressed derivative, restrained and same hue family. |
| `secondary` | `#E4C75F` | Card chip/chart yellow sampled visually from reference. |
| `tertiary` | `#E66B54` | Small red-orange card/chart accent. |
| `textPrimary` | `#292929` | Main headings/numbers and dark navigation family. |
| `textSecondary` | `#666866` | Accessibility-adjusted from observed gray near `#8F908E`. |
| `textTertiary` | `#747674` | Muted labels; kept at AA only when size/weight permits. |
| `textDisabled` | `#8A8C89` | Disabled controls on pale surfaces. |
| `onDark` | `#F7F6F4` | Text/icons on charcoal. |
| `onAccent` | `#202733` | Accessibility adjustment for the blue feature surface. |
| `success` | `#587D68` | Reference has no clear green semantic; a desaturated green matching its restraint. |
| `danger` | `#B64F43` | Darkened from reference red-orange so normal text passes AA. |
| `warning` | `#8A6818` | Dark ochre derived from yellow for readable warning text. |
| `info` | `#49698F` | Dark blue semantic text derived from the feature card. |

### Color distribution

Approximate image area: 26% outer gray canvas, 52% warm shell and pale surfaces, 13% charcoal anchors, 6% blue accent, and 3% yellow/red/highlight details. Within the application shell, pale neutral surfaces should stay near 72%, charcoal near 17%, blue near 8%, and all secondary accents below 3%. Blue must never flood every button or card.

### Gradients

The blue hero uses a subtle layered field rather than a high-contrast linear rainbow: base `#82A0CF`, a broad radial highlight around the upper-right/center (`#9BB4DF` at roughly 0–35%, fading transparent by 75%), and a lower-left/mid overlay near `#7595C7` at low opacity. The source also contains faint abstract arcs at approximately 8–14% alpha. AirType may reproduce this with a restrained two-stop diagonal/radial WPF brush, never with the retired purple/olive palette. The outer canvas and ordinary cards are flat tones; the apparent variation is JPEG texture and shadow, not a gradient.

## 3. Depth, light, and material

There are four elevation levels: canvas (0), shell (1), cards/controls (2), and floating/dark feature panels (3). Separation is primarily tonal plus a one-pixel light stroke; shadows appear only on the outer shell and selected raised elements. The shell shadow is approximately x 0, y 14–18 px, blur 28–38 px, black at 18–22% alpha. Cards use x 0, y 2 px, blur 8–12 px, black at 5–8%, often nearly invisible. Top-edge highlights are white at roughly 45–70% alpha. There is no convincing glass blur or neon glow. Light is diffuse from upper-left/above. AirType should use opaque warm surfaces and soft shadows; transparency is reserved for the existing floating indicator where per-pixel transparency is functionally required.

## 4. Shape and geometry

The outer shell runs approximately x 36–700 and y 40–511: 664 × 471 px with a radius near 25–28 px. Standard cards use 17–20 px radii, nested panels 13–16 px, buttons/chips 18–24 px or fully pill-shaped, and avatars/icon containers are circular. The nested-radius rule is outer radius minus inset padding, usually a 6–10 px decrement. Borders are one pixel and low contrast. The image follows an approximately 8 px base rhythm: common gaps are 8, 12, 16, and 20 px; major regions use 24–32 px. The shell grid uses a narrow 36 px rail, a flexible main area, and cards aligned to shared horizontal/vertical guides. AirType retains its existing page structure but adopts this spacing rhythm and nested geometry.

## 5. Typography

The family character is a modern geometric/humanist sans with double-storey forms and compact metrics, close to Inter/Manrope. The implementation uses locally available `Segoe UI Variable Text` for Latin and `Segoe UI Variable Text`/`Segoe UI` for Arabic to avoid network loading and preserve excellent Windows shaping; Arabic receives no tracking. Reference scale at 736 px width: hero greeting about 31 px bold, display figures 24–27 px bold, section titles 12–14 px semibold, body 10–12 px regular, captions 8–10 px. WPF translation: display 30, title 24, heading 16, body 13, label 12, caption 11, micro 10, with line heights around 1.35 Latin and 1.5 Arabic. Hierarchy relies on weight and size, not many colors. Numeric metrics use tabular-friendly `Cascadia Mono` only where fixed-width technical data improves scanning.

## 6. Component anatomy

- **Navigation:** charcoal floating rail/pill; circular selected marker; simple 1.5 px outline icons; pale labels/icons.
- **Top controls:** circular or pill outlines on the warm shell, 40–44 px targets, minimal fill, dark text.
- **Primary button:** blue fill, dark on-accent label, rounded 14–18 px, subtle hover darkening and press compression through opacity/elevation only.
- **Secondary button:** raised pale surface, gray outline, charcoal label.
- **Cards:** warm raised fill, one-pixel pale stroke, 18–20 px radius, 16–20 px padding, very soft shadow.
- **Hero card:** cornflower-blue layered surface; large state title, small supporting text, dark readable content.
- **Chips/segmented controls:** compact pills, pale or charcoal based on hierarchy; selected state uses blue or dark fill.
- **Inputs:** pale raised fill, clear gray stroke, blue focus ring, 42–48 px minimum height.
- **Lists/diagnostics:** aligned label/value rows, subtle dividers, no zebra striping.
- **Toggles:** charcoal/gray track when off, blue track when on, circular warm thumb.
- **Dialogs:** warm shell with 22–24 px radius and level-3 shadow; QR itself remains pure white/black for scan reliability.
- **Warnings/info:** tinted pale containers with dark semantic text; never saturated full-card fills.
- **Floating indicator:** compact charcoal capsule with blue/secondary activity bars; no rectangular native backdrop.

## 7. Layout and composition

The eye enters at the large upper-left greeting, moves to the blue hero card at center, then scans the balanced card grid, and lands on the dark lower-right promo anchor. AirType translates this into a clear page heading, a dominant connection-state hero, supporting metric cards, and restrained action grouping. Grouping uses proximity and enclosing surfaces rather than many rules. Existing floating top and bottom capsules remain accessible, but their material becomes warm/charcoal and visually related to the reference’s rail and outlined controls.

## 8. Iconography and imagery

Icons are minimal outline glyphs, approximately 1.5–1.75 px stroke, rounded joins, generally on a 20–24 px grid inside 40–44 px circular containers. Selected navigation may use a filled circle with a dark or pale glyph. No reference avatars, finance symbols, fake imagery, or chart content will be copied. AirType’s current text glyphs remain where replacing them would require external icon dependencies; shared icon containers normalize their presentation.

## 9. Motion inference

The static design implies soft, restrained motion: 140 ms hover/press, 220 ms state transitions, and 300 ms entrance changes using a cubic ease-out. Only opacity, tone, and tiny scale/elevation changes should animate. The existing activity pulse can remain but should use the new colors. No animation may alter networking timing or block input. WPF’s system animation preference should be respected; no mandatory decorative timeline is introduced where reduce-motion detection is unavailable.

## 10. Accessibility audit and adjustments

Observed secondary gray around `#8F908E` on `#EBEAE8` is approximately 2.7:1 and fails normal-text AA, so AirType uses `#666866` (about 4.7:1). White text on sampled blue `#82A0CF` is approximately 2.6:1, so hero/card text becomes `#202733` (over 5:1). Yellow `#E4C75F` is not used for small text on pale surfaces; `#745710` is used instead. Red-orange is darkened to `#9E4037` for text. `#292929` on warm shell exceeds 11:1, and `#F7F6F4` on charcoal exceeds 12:1. QR modules retain black on pure white because scanner contrast takes precedence over decorative theming. Existing compact title controls cannot all reach a 48 px touch target without materially changing the desktop window chrome; desktop pointer targets are kept at or above 38–40 px, while primary controls are 44–48 px.

## Design Constitution

1. Warm pale neutrals dominate; blue is a controlled feature accent, never a wash.
2. Charcoal anchors navigation and high-priority overlays.
3. Hierarchy comes from type scale and weight before color.
4. Every surface follows the nested-radius rule and an 8 px spacing rhythm.
5. Borders are one-pixel and quiet; depth is soft, neutral, and sparse.
6. Body text must meet WCAG AA even where the reference does not.
7. Primary actions use cornflower blue with dark readable labels.
8. Components are token-driven; screens do not invent colors, radii, shadows, or typography.
9. Arabic and English share hierarchy but receive correct shaping, line height, and directionality.
10. No finance content, fake data, or decorative feature is imported—only the visual grammar.
