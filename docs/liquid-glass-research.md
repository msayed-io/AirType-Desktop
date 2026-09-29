# AirType visual research

Sources:
- Apple Liquid Glass overview: https://developer.apple.com/documentation/technologyoverviews/liquid-glass
- Apple HIG Materials: https://developer.apple.com/design/human-interface-guidelines/materials

Applied principles:
- Liquid Glass is a functional layer for controls/navigation, not the content layer.
- Use it sparingly for floating navigation capsules; keep content cards as solid standard surfaces.
- Regular material prioritizes legibility by blurring/adjusting luminosity; clear is for visually rich backgrounds.
- Use scroll-edge/dimming support when translucency could reduce contrast.
- Use vivid foreground colors and preserve hierarchy, predictable action placement, adaptive layout, and accessibility.
- AirType adapts these principles visually in WPF using tinted translucent capsules, highlight border, shadow, and solid dark content cards; it is not a literal Apple API on Windows.
