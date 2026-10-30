# Beta validation checklist

## Completed for 1.2 (glass redesign)

- WPF rebuild with Windows 11 acrylic backdrop, custom title bar and solid fallback; native caption buttons suppressed under the custom bar.
- Fake-device UI checks: profile tiles, active/custom states, unavailable tiles, role chips, accent default/Match Windows/restore with persistence, horizontal profile scrolling past three profiles, tray flyout, Settings/About identity, offline editing without substitution, editor focus, responsive background switching and failure feedback, exit/reopen persistence.
- Real app launch and second-instance hand-off verified on Windows 11.

## Completed for 1.1

- Build and connection/preference tests.
- Legacy group migration and preserved endpoint IDs/preferences.
- Missing-device explanations, active-profile matching across all six defaults, and reconnection with the same IDs.
- Invalid/unreadable settings protection, save blocking, explicit backup/reset and previous-file backups.
- Inline offline rename without substitution; accessible editor labels and Tab focus from name to playback.
- Simulated switch/failure with UI message-loop activity; no live default changes.
- Profile-to-custom regression verifies that the Active profile field clears while saved profiles remain. Split-role computer/disc/phone indicators and the current Windows title-bar preference are checked.
- With explicit permission, activated Gaming on live hardware, then returned playback to the original fifine speakers with the fifine microphone. Confirmed active-name clearing and restored role indicators. This does not cover failed-switch restoration.
- Rendered 440×480, 480×660 and 720×900 client sizes (1.1 WinForms UI) plus simulated 150%/200% control scaling. Small windows/long lists scroll; tooltips retain full names and explanations.

## Manual hardware and accessibility checks still required

- With explicit permission, switch real devices/profiles; independently verify all six Windows role assignments.
- Bluetooth hands-free/stereo transitions; USB/HDMI hotplug before and during switching.
- Unplug a profile device, confirm it stays visible/unavailable; reconnect without substitution.
- Sleep/resume, driver restarts and competing Windows/application changes.
- Real write/restoration failures, including disappearance of the prior default.
- Apps with explicit device selections and sessions that do not follow defaults.
- Glass on a Windows 10 machine and with Transparency effects off (solid fallback); Match Windows with several accent colors, including very light and very dark ones.
- Tray flyout placement with the taskbar on each screen edge and on secondary monitors.
- Real 100%, 150% and 200% monitor settings, multiple monitors, screen readers, high contrast and full keyboard navigation. Simulated renders do not verify OS DPI transitions or screen readers.
- Enable/disable startup as current user, move the EXE, and test a clean Windows account.
- Recovery messages/backups on access-denied, disk-full and OneDrive synchronization errors.

The owner approved a public beta. Builds remain unsigned; do not purchase signing until the owner explicitly requests it.
