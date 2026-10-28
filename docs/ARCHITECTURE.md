# Architecture and feature boundaries

`Program.cs` owns process startup and single-instance activation. `Widget.cs` owns the manual interface, inline editor, tray interaction, current-user startup preference and UI-thread notifications.

`Models.cs` contains persisted models, settings saves/recovery, profile availability and six-role matching, and `IAudioService`. `WindowsAudioService` creates and disposes COM objects on the worker thread that uses them; the UI never shares a COM object between threads. Read/switch tasks explicitly use the default background scheduler. Completion callbacks use the form's BeginInvoke dispatcher rather than an ambient synchronization context, keeping redraws on the owning UI thread even during nested/test message loops. Only one manual switch is accepted at a time, exit is held during a switch, and a possibly stale polling result is ignored while switching.

`UiElements.cs` draws the compact role indicators and list outlines. Indicators are noninteractive and expose role/state accessible names plus tooltips. WindowTheme reads the current-user app theme preference and requests the DWM immersive title bar, with a native fallback on unsupported systems. It never modifies Windows theme settings. The active-profile field is derived from each snapshot; it is not a persisted last-used name.

`Audio.cs` retains availability prechecks for the entire requested set, per-role writes, read-back verification, prior-default snapshots and verified restoration attempts. This reliability layer is not a paid capability.

Legacy XML fields `Profiles`, `Favorites`, `Deemphasized`, `Pinned`, `WindowWidth`, and `WindowHeight` are retained. Deemphasized devices now appear under Other devices. Endpoint references are never replaced based on similar hardware names. Optional saved hardware names improve offline explanations; aliases affect presentation only. Unknown XML elements are preserved. Invalid or unreadable settings block writes until explicit backup/reset; normal saves retain the previous file.

## Free boundary

Manual device/profile selection, profile editing, favorites, aliases, organization, startup preference, tray access, remembered size, status/error feedback, verification/restoration, keyboard navigation and accessibility remain free. No activation check gates these paths.

## Planned premium boundary

There are no global hotkey listeners, process watchers, application triggers, entitlement checks, accounts or payment code in this version. Future premium trigger adapters should submit explicit requests through a shared operation coordinator backed by `IAudioService`, rather than writing Windows defaults directly. That coordinator should serialize operations, retain full prior role assignments, handle cancellation/disconnection, and marshal UI changes safely. An entitlement policy should gate trigger registration only, leaving free manual commands and accessibility intact.

Trigger precedence, overlapping apps, restoring previous defaults only when appropriate, and sleep/resume rules need a separate design before automation is enabled. Do not silently substitute unavailable devices. Keep licensing/payment implementation separate from audio service and profile storage.

## Tests

`ConnectionTests.cs` exercises sorting, preferences, window dimensions, backward compatibility and rejecting an invalid endpoint before any write. `StateTests.cs` validates migration, availability/reconnection, split roles, malformed settings, explicit recovery and exact backups in temporary directories. `UiChecks.cs` injects a fake service and isolated settings into the real form, renders layouts, tests offline editing, checks Tab focus and holds a simulated switch to verify UI responsiveness. It never writes live defaults or enables startup.
