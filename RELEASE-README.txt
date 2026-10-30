Audio Me 1.2.0 beta - Windows x64

Portable Windows audio switching. Requires Windows 10/11 x64 and .NET Framework 4.8.
Developer: Justin Miller. Settings (gear) > About shows the running version.
Extract and run AudioMe.exe. No installer or administrator access is needed.
The build is unsigned: SmartScreen may ask before the first run (More info > Run anyway).
Glass surfaces need Windows 11 22H2+ with Transparency effects on; otherwise a solid
dark surface is used.

FREE FEATURES
Manual playback/microphone switching; profiles with icons; favorites; local labels;
collapsible Other devices; tray quick-switch flyout and tray menu; keep-on-top; accent
color (Audio Me teal or match Windows); remembered size; optional current-user startup;
device status; verification and restoration safeguards.

Select New in the profile row. Name it, pick an icon and explicitly choose one playback
device and one microphone. Save, then click its tile to activate. Right-click a tile or
use Edit to change it. Disconnected profiles stay visible, dimmed with a missing-device
explanation; saved references are retained and never substituted.
The Now card names a profile only when all six defaults match; otherwise it shows
Custom setup. Monitor/music/phone chips show System/Media/Calls for each device.
With more than three profiles the row scrolls sideways (mouse wheel works).

Star favorites. The ... button renames a device's local label or moves it to Other
devices and back. Hardware names remain in tooltips. Esc returns to the main page.

Closing hides the app to the tray. Left-click the tray icon for the quick-switch
flyout; right-click for profiles and Exit. Hidden-window switches show tray feedback.
Start with Windows uses a current-user entry; toggle off/on after moving the EXE.
Settings: %LOCALAPPDATA%\AudioSwitch\settings.xml. Old groups migrate automatically.
Unreadable settings are retained; saving pauses until explicit backup/reset.

Support development: https://buymeacoffee.com/justony5
Source and issues: https://github.com/justony5/audio-me (MIT License)

PLANNED PREMIUM - NOT IMPLEMENTED
Global shortcuts, cycling shortcuts, application-triggered profiles with optional restoration,
and playing through two devices at once. A free calls-on-another-device option is planned.
No payments, accounts, subscriptions or production licensing are implemented.

LIMITATIONS
Unsigned build. PolicyConfig is undocumented and may change with Windows updates.
Per-app selections can override defaults. Some existing audio sessions need restart.
Restoration is attempted, not guaranteed if devices disappear or Windows rejects a call.
System-DPI aware; exit/reopen after changing monitor scale. Long pages scroll.
Bluetooth, hotplug, sleep/resume and real failed-switch restoration need manual beta checks.
No microphone audio is recorded and no telemetry is sent.
