# Beta validation checklist

## Completed for this revision

- Build and connection/preference tests.
- Legacy group migration and preserved endpoint IDs/preferences.
- Missing-device explanations, active-profile matching across all six defaults, and reconnection with the same IDs.
- Invalid/unreadable settings protection, save blocking, explicit backup/reset and previous-file backups.
- Inline offline rename without substitution; accessible editor labels and Tab focus from name to playback.
- Simulated switch/failure with UI message-loop activity; no live default changes.
- Rendered 440×480, 480×660 and 720×900 client sizes plus simulated 150%/200% control scaling. Small windows/long lists scroll; tooltips retain full names and explanations.

## Manual hardware and accessibility checks still required

- With explicit permission, switch real devices/profiles; independently verify all six Windows role assignments.
- Bluetooth hands-free/stereo transitions; USB/HDMI hotplug before and during switching.
- Unplug a profile device, confirm it stays visible/unavailable; reconnect without substitution.
- Sleep/resume, driver restarts and competing Windows/application changes.
- Real write/restoration failures, including disappearance of the prior default.
- Apps with explicit device selections and sessions that do not follow defaults.
- Real 100%, 150% and 200% monitor settings, multiple monitors, screen readers, high contrast and full keyboard navigation. Simulated renders do not verify OS DPI transitions or screen readers.
- Enable/disable startup as current user, move the EXE, and test a clean Windows account.
- Recovery messages/backups on access-denied, disk-full and OneDrive synchronization errors.

Keep the repository private. Do not publish a beta release or purchase signing until the owner explicitly requests it.
