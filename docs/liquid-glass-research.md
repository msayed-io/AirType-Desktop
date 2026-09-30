# AirType visual research

Sources:
- Apple Liquid Glass overview: https://developer.apple.com/documentation/technologyoverviews/liquid-glass
- Apple HIG Materials: https://developer.apple.com/design/human-interface-guidelines/materials
- LiquidGlass2 taskbar theme reference: https://github.com/ramensoftware/windows-11-taskbar-styling-guide/tree/main/Themes/LiquidGlass2

Applied principles:
- Liquid Glass is a functional layer for controls/navigation, not the content layer.
- Use it sparingly for floating navigation capsules; keep content cards as solid standard surfaces.
- Regular material prioritizes legibility by blurring/adjusting luminosity; clear is for visually rich backgrounds.
- Use scroll-edge/dimming support when translucency could reduce contrast.
- Use vivid foreground colors and preserve hierarchy, predictable action placement, adaptive layout, and accessibility.
- LiquidGlass2 uses Windhawk-only `WindhawkBlur`, dark translucent tints, a lightly saturated blur, vertical highlight borders, and a corner radius equal to half the control height.
- AirType maps that treatment to standalone WPF with per-pixel transparent layered windows, a dark translucent material, a vertical gradient border, half-height pill radii, and restrained shadow. This keeps the floating capsule shape exact; native HWND-wide acrylic is intentionally avoided because it paints a rectangular composition surface outside WPF's rounded content.
- Content cards remain solid and legible; glass is limited to floating controls and navigation.
