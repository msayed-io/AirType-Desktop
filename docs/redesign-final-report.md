# AirType Reference-Driven Redesign — Final Engineering Report

## Outcome

AirType’s application-owned WPF presentation layer was rebuilt around the visual DNA sampled from `Finance Dashboard.jpg`: warm pale shell and cards, charcoal navigation/console anchors, a restrained cornflower-blue hero and primary action, broad soft elevation, nested rounded geometry, and stronger editorial typography. No networking, pairing, protocol, injection, state, persistence, update, or service implementation was modified.

## Final token table

### Color

| Token | Value |
|---|---:|
| canvas | `#C3C3C3` |
| shell / background | `#EBEAE8` |
| raised surface | `#F2F0F1` |
| card | `#F7F6F4` |
| muted surface | `#E1E0DE` |
| overlay | `#2D2D2D` |
| overlay pressed | `#232323` |
| stroke | `#A9AAA8` |
| subtle stroke | `#FDFCF9` |
| accent | `#82A0CF` |
| accent hover | `#7595C7` |
| accent pressed | `#6888BB` |
| accent container | `#DDE6F3` |
| secondary | `#E4C75F` |
| tertiary | `#E66B54` |
| text primary | `#292929` |
| text secondary | `#666866` |
| text tertiary | `#747674` |
| text disabled | `#8A8C89` |
| on dark | `#F7F6F4` |
| on accent | `#202733` |
| success | `#587D68` |
| warning | `#745710` |
| danger | `#9E4037` |
| info | `#49698F` |

### Geometry, typography, and motion

- Spacing rhythm: 4, 8, 12, 16, 20, 24, 32, 40 DIP.
- Radius scale: 8, 12, 16, 20, 28, full pill.
- Type scale: 10, 11, 12, 13, 16, 24, 30 DIP; PIN 38; watermark 64.
- Fonts: Segoe UI Variable Text / Segoe UI; Cascadia Mono / Consolas for technical values.
- Control height: 44 DIP; primary interactive sizing targets 44–48 DIP.
- Elevation: 10/2/7%, 18/5/12%, 34/12/20% blur/depth/opacity recipes.
- Motion tokens: 140 ms, 220 ms, 300 ms with cubic ease-out. Existing indicator animation behavior remains functionally unchanged.

## Coverage against inventory

| Surface/state family | Redesigned |
|---|---|
| Main shell, warm canvas/shell, top controls | Yes |
| Bottom Home/Settings/Diagnostics navigation | Yes — charcoal anchor treatment |
| Update available/downloading/failure banner | Yes via shared components |
| Hotkey warning banner | Yes via shared warning component |
| Home: all eight connection states | Yes via state-driven shared hero |
| Home: empty/populated phone and metrics | Yes |
| Home: pairing, stream, disconnect, disabled states | Yes |
| Elevated-target warning | Yes |
| QR pairing window | Yes; QR remains pure black/white for scanning |
| QR manual-entry expanded/collapsed content | Yes |
| Settings: connection, hotkeys, language, startup, logging | Yes |
| Settings success/error status text and input focus | Yes |
| Diagnostics server/firewall/error variants | Yes |
| Diagnostics self-test idle/running/pass/fail console | Yes |
| Floating indicator states and activity bars | Yes |
| Arabic RTL / English LTR resource-driven presentation | Yes structurally; live switch behavior unchanged |
| System-owned MessageBox / Firewall / Explorer / installer UI | Not application-themeable without replacing OS behavior |

## Shared component system

`Themes/DesignTokens.xaml` now holds typography, spacing, radii, stroke, sizing, elevation, opacity, and motion values. `Themes/Colors.xaml` is the single color source. `Themes/Styles.xaml` defines reusable typography, card, hero, warning, danger, chrome capsule, charcoal navigation, button, title control, input, mono input, navigation item, choice pill, and toggle recipes. Screen XAML uses those resources instead of containing color literals.

## Accessibility adjustments

- Reference secondary gray was darkened to `#666866`; contrast on cards is approximately 5.2:1.
- Hero content uses `#202733` on `#82A0CF`, approximately 5.63:1, rather than the reference’s low-contrast white-on-blue treatment.
- `#F7F6F4` on charcoal is approximately 12.75:1.
- Warning and danger text were darkened to `#745710` and `#9E4037` so they exceed 4.5:1 on their containers.
- QR black/white contrast was retained as a functional scanning requirement.
- Primary controls use 44–48 DIP targets; compact desktop window controls remain 40 DIP to avoid breaking window chrome.

## Files changed

Presentation and documentation only:

- `src/LiveTypeBridge.App/App.xaml`
- `src/LiveTypeBridge.App/MainWindow.xaml`
- `src/LiveTypeBridge.App/Themes/Colors.xaml`
- `src/LiveTypeBridge.App/Themes/DesignTokens.xaml`
- `src/LiveTypeBridge.App/Themes/Styles.xaml`
- `src/LiveTypeBridge.App/Views/StatusPage.xaml`
- `src/LiveTypeBridge.App/Views/SettingsPage.xaml`
- `src/LiveTypeBridge.App/Views/DiagnosticsPage.xaml`
- `src/LiveTypeBridge.App/Views/QrWindow.xaml`
- `src/LiveTypeBridge.App/Views/FloatingIndicatorWindow.xaml`
- `docs/design-dna.md`
- `docs/screen-inventory.md`
- `docs/redesign-final-report.md`

No `.cs`, Core, service, networking, pairing, protocol, state, settings, localization-copy, build, signing, or workflow file changed.

## Verification

- Release build: passed, zero errors.
- Existing automated tests: 56/56 passed.
- XAML/XML parsing: passed.
- `git diff --check`: passed.
- Retired olive/khaki and former purple/cyan palette scan: no matches in application source.
- Color-literal scan outside token dictionaries: no matches.
- Scope scan: changes limited to presentation XAML and documentation.

## Screenshot and runtime limitation

The execution environment is Linux and cannot start or render a Windows WPF application; Wine/Xvfb are unavailable. Therefore genuine post-redesign Windows screenshots, interactive focus/hover inspection, 100–200% Windows text-scaling inspection, and manual QR/network flow walkthrough cannot be honestly produced here. The Windows CI build validates compilation, tests, packaging, and release, but it does not provide an interactive desktop. Fabricated mockups were intentionally not substituted for real screenshots. Final pixel-level screenshot acceptance must be captured from the published Windows build using the repository’s `tools/capture-ui.ps1` / `tools/capture-window.ps1` scripts.
