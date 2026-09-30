# Pill and rounded-corner implementation note

## Final requirement

Interactive elongated controls use the native-WPF equivalent of CSS `border-radius: 9999px`: one circular radius clamped to half the element’s actual shorter side. For the horizontal controls in this application, that is exactly half the rendered height, so both ends are true semicircles and the center remains straight at every supported width.

## Why a custom WPF renderer is required

AirType Desktop is a native **WPF** application, not HTML/CSS, Electron, Tauri, Chromium, or WebView. It therefore has no CSS cascade, browser DevTools, or CSS computed-style panel.

A raw oversized `Border.CornerRadius` is not used for pills. WPF can normalize oversized corner values independently against the horizontal and vertical dimensions, which risks elliptical radii on a non-square rectangle. `Controls/PillBorder.cs` prevents that failure explicitly:

```csharp
var effectiveRadius = Math.Min(requestedRadius, Math.Min(rect.Width, rect.Height) / 2);
drawingContext.DrawRoundedRectangle(
    Background, pen, rect, effectiveRadius, effectiveRadius);
```

Using the same `effectiveRadius` for X and Y guarantees circular arcs. The centralized requested value is:

```xml
<CornerRadius x:Key="Radius.Pill">9999</CornerRadius>
```

`PillBorder` preserves the previous WPF child layout, padding, dependency-property bindings, brushes, borders, focus triggers, and hit testing while changing only the outline renderer.

## Scope

| Surface | Rule |
|---|---|
| Primary and secondary buttons | `Radius.Pill` → half actual height |
| Header capsules | `Radius.Pill` → half actual height |
| Individual navigation items | `Radius.Pill` → half actual height |
| Pill text fields | `Radius.Pill` → half actual height |
| Language choices | `Radius.Pill` → half actual height |
| Toggle track and thumb | `Radius.Pill` → half shorter side |
| Compact status badges | `Radius.Pill` → half actual height |
| Floating typing indicator and bars | `Radius.Pill` → half shorter side |
| Large cards | `Radius.L` = 20 DIP |
| Outer navigation container | `Radius.Navigation` = 24 DIP |
| Large diagnostics output field | `Radius.Input` = 12 DIP |
| Main window / dialog shells | `Radius.XL` = 28 DIP |

The previous continuous Bézier corner renderer was intentionally removed because a continuous Apple-style corner is not a circular pill end and therefore does not satisfy this final pill specification.
