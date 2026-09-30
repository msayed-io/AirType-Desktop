# Corner-radius implementation note

## Official findings

Apple’s Human Interface Guidelines define hierarchy, grouping, adaptable layouts, and a minimum 44 × 44 pt button hit region, but they do **not** publish one universal corner-radius number for every button, field, toolbar, or navigation control:

- Apple HIG — Layout: https://developer.apple.com/design/human-interface-guidelines/layout
- Apple HIG — Buttons: https://developer.apple.com/design/human-interface-guidelines/buttons
- Apple Design Resources (the official platform templates): https://developer.apple.com/design/resources/

On Apple-native layers, the characteristic smooth result is produced by a continuous corner curve (`CALayer.cornerCurve = .continuous`), not by setting every component to a capsule and not by one radius percentage that applies everywhere. Values circulated for app icons are icon-mask measurements and are not an official general-purpose control rule.

## WPF translation

WPF `Border.CornerRadius` draws ordinary circular-arc rounded corners and has no native equivalent of Apple’s continuous corner curve. Replacing every control with a custom clipped Bézier geometry would alter rendering, focus outlines, hit testing, and control templates beyond the requested corner-only scope. The safe translation is therefore a fixed semantic radius scale whose values stay substantially below half of each component’s height; a half-height radius is specifically avoided because it produces an oval/capsule.

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
