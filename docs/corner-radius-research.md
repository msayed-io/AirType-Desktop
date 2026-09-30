# Corner-radius implementation note

## Official findings

Apple’s Human Interface Guidelines define hierarchy, grouping, adaptable layouts, and a minimum 44 × 44 pt button hit region, but they do **not** publish one universal corner-radius number for every button, field, toolbar, or navigation control:

- Apple HIG — Layout: https://developer.apple.com/design/human-interface-guidelines/layout
- Apple HIG — Buttons: https://developer.apple.com/design/human-interface-guidelines/buttons
- Apple Design Resources (the official platform templates): https://developer.apple.com/design/resources/
- PaintCode’s published `UIBezierPath` extraction and coefficients: https://www.paintcodeapp.com/news/code-for-ios-7-rounded-rectangles

On Apple-native layers, the characteristic smooth result is produced by a continuous corner curve (`CALayer.cornerCurve = .continuous`), not by setting every component to a capsule and not by one radius percentage that applies everywhere. Values circulated for app icons are icon-mask measurements and are not an official general-purpose control rule.

## WPF translation

WPF `Border.CornerRadius` and `DrawingContext.DrawRoundedRectangle` draw ordinary circular-arc corners and expose no equivalent of Apple’s continuous corner-curve selector. Radius changes alone therefore cannot produce the requested curvature.

The targeted interactive surfaces now use `Controls/SmoothBorder.cs`, a WPF `Decorator` that keeps the existing child layout, padding, dependency-property bindings, brushes, template triggers, focus behavior, and hit testing while replacing only the rendered outline. Its `StreamGeometry` follows PaintCode’s published reconstruction of the well-behaved iOS continuous rounded rectangle extracted from `UIBezierPath`: multiple cubic Bézier segments per corner, curve extent `1.52866483 × radius`, and radius clamp `min(width, height) / (2 × 1.52866483)`. Those coefficients are reverse-engineered implementation data—not values Apple documents as a universal HIG rule.

The conversion is deliberately scoped to the requested surfaces: header containers, navigation container/items, buttons, text inputs, language choices, toggle track/thumb, floating indicator, and related compact status badges. Large content cards remain ordinary `Border` surfaces to avoid unrelated visual or layout changes.

| Component | Height | Radius | Radius / height |
|---|---:|---:|---:|
| Standard buttons and text inputs | 44 DIP | 10 DIP | 22.7% |
| Upper header containers | 48 DIP | 12 DIP | 25.0% |
| Bottom navigation container | 56 DIP | 14 DIP | 25.0% |
| Bottom navigation items | 44 DIP | 10 DIP | 22.7% |
| Language choices | 44 DIP minimum | 10 DIP | ≤22.7% |
| Toggle track | 24 DIP | 6 DIP | 25.0% |
| Toggle thumb | 18 DIP | 4 DIP | 22.2% |
| Floating indicator body | 34 DIP | 10 DIP | 29.4% |

All values are centralized in `Themes/DesignTokens.xaml`; obsolete full-pill and capsule-radius tokens were removed so they cannot be reused accidentally.
