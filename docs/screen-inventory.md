# AirType Screen and State Inventory

Inventory derived from `App.xaml`, window/page XAML, view models, localization dictionaries, and window lifecycle code. AirType is a Windows WPF desktop utility; there are no mobile routes, bottom sheets, onboarding, or splash screen in the codebase.

## Application shell (`MainWindow`)

- Floating top brand capsule: app name, privacy tagline, indicator toggle, live Arabic/English switch, minimize-to-indicator, close.
- Current-page host.
- Floating bottom navigation: Home, Settings, Diagnostics.
- Hotkey-registration warning banner: hidden / visible.
- Update banner states: hidden; update available; downloading and install disabled; install failed and retry enabled.
- Direction states: Arabic RTL and English LTR, changed live.
- Window states: visible; hidden to floating indicator; restored; minimized path redirected to indicator; explicit close.

## Home / status (`StatusPage`, `MainViewModel`)

Connection hero states:

1. Not connected.
2. Ready to scan.
3. Connected and streaming.
4. Paused manually.
5. Paused by protection.
6. Resynchronizing.
7. Emergency stopped.
8. Error.

Supporting states and controls:

- Phone: none / paired device name.
- Metrics: no samples / last-received age / measured one-way lag / optional heartbeat RTT / populated session statistics.
- Pair phone command: idle / busy-disabled while pairing starts.
- Stream button: disabled without active pump; stop while active; resume while paused/stopped.
- Disconnect: disabled / enabled.
- Elevated-target warning card: hidden / visible.
- Error routes for invalid port binding and no local IPv4 address.

## Pairing window (`QrWindow`)

- Active QR session: title, instructions, QR bitmap, PIN, countdown.
- Countdown active / expired (`00:00`, window dismissed by main flow).
- Manual-entry expander: collapsed / expanded with address, session ID, PIN.
- Firewall/local-network notice.
- Close action.
- QR image must remain high-contrast black on pure white.

## Settings (`SettingsPage`, `SettingsViewModel`)

- Connection port input and explanatory state.
- Three global-hotkey capture inputs: listening, accepted combination, cleared with Escape, invalid format, duplicate, OS registration warning surfaced in shell.
- Language choice: Arabic selected / English selected, updates live.
- Start with Windows: off / on.
- Logging: off / on.
- Log streamed text content: off / on warning state.
- Open log folder action.
- Save statuses: idle; saved; invalid port; invalid hotkey; duplicate hotkeys; server restarted successfully; restart/bind failure.

## Diagnostics (`DiagnosticsPage`, `DiagnosticsViewModel`)

- Server: running / stopped.
- Uptime: value / unavailable dash.
- Network candidates: none / one / multiple IPv4 addresses.
- Port: configured only / actual plus configured fallback.
- Firewall: allowed / no rule / unknown.
- Last error: none / recorded protocol or UI error.
- Refresh action.
- Self-test: idle/empty; running and button disabled; passed output; failed output; crashed output.

## Floating indicator (`FloatingIndicatorWindow`)

- Hidden / shown.
- States mapped from all eight connection states.
- Activity pulse when session statistics change.
- Click to restore main window.
- Drag/reposition and bottom-screen placement.
- Per-pixel transparent surroundings; capsule only.

## System-owned prompts and external UI

- Single-instance `MessageBox`: already running.
- Unhandled-exception `MessageBox`: warning.
- Windows Firewall permission prompt on first server bind.
- Explorer window for logs.
- Installer/restart UI owned by Velopack/Windows.

These system-owned surfaces cannot be fully restyled from WPF without replacing operating-system behavior; application-owned surrounding copy and surfaces remain consistent. No custom toast, snackbar, permission sheet, onboarding, or splash implementation exists.

## Coverage target

Application-owned shell, Home, Settings, Diagnostics, QR window, floating indicator, warnings, update states, error/success/disabled/focus/hover states, and both RTL/LTR directions are included. Backend, networking, pairing protocol, injection, state semantics, persistence, and update behavior are out of visual scope and must remain unchanged.
