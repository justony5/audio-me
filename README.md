# Audio Me

<img src="Assets/AudioMe.png" alt="Audio Me AM logo" width="96">

**A compact Windows tray utility for switching speakers, microphones, and saved audio profiles.**

Move between gaming, work, and casual listening without digging through Windows Sound settings. Choose a device or activate a profile that selects **one playback device and one recording device**. Audio Me sets and verifies the Windows **System, Media, and Calls** defaults; a profile does not combine several speakers.

## Implemented features — free

- **Manual playback and microphone switching**, with availability checks, verification of all requested roles, and attempts to restore previous defaults on failure.
- **Profiles inside the main window:** create, rename, edit, delete, and manually activate named setups. No separate management window.
- **Clear profile status:** active profiles are highlighted; custom or split-role setups are identified. Disconnected profiles remain visible and explain which device is missing. Saved references stay intact until you choose a replacement.
- **Favorites:** gold stars sort favorites alphabetically at the top. Local display aliases keep long hardware names manageable; full hardware names remain in tooltips.
- **Other devices:** move connected but less-used hardware into a collapsible section with the down arrow; use the up arrow to restore it. This is separate from disconnected profile status.
- **Visible role assignments:** see System, Media, and Calls under each device, including when Windows assigns them to different devices.
- **Tray access and taskbar reopening**, keep-on-top, remembered window size, and optional startup with Windows for the current user.
- **Background switching and verification** keep the window responsive; status messages report success, failure, and restoration problems.
- **Settings recovery:** unreadable or invalid settings are left untouched, saving is paused, and an explicit recovery action backs up the original before allowing a reset. Normal saves retain the previous file as a backup.
- **Keyboard navigation and accessible control names** are included in the free app.

No accounts, payments, subscriptions, production licensing, microphone recording, or telemetry are implemented.

## Planned premium features — not implemented

Everyday manual switching and the features above remain free. Future premium work is reserved for:

- Global shortcuts that select devices or profiles.
- Shortcuts to cycle through selected devices or profiles.
- Application-based profile activation when a selected app launches or becomes active.
- Optional restoration of the previous setup when an application trigger ends.

These are roadmap items, not capabilities in this build. Basic Tab/Shift+Tab navigation and accessibility will remain free.

## Using profiles

1. Select **Create profile** near the top. The editor expands inside the main window.
2. Enter a name such as **FPS Gaming**, **Work**, or **Casual** and explicitly select its playback device and microphone.
3. Select **Save profile**, then click the profile's name to activate it.
4. Use its **Edit** button to rename it, change either device, or delete it. **Edit profiles** also opens the editor.

An unavailable profile cannot be activated. Its missing-device explanation remains visible, and editing it retains the saved device reference. Reconnecting the same endpoint makes it available again. Audio Me never silently substitutes another microphone or speaker.

Old saved groups migrate automatically to profiles using the same stored endpoint IDs. Favorites, Other devices preferences, pin state, and window size remain compatible.

## Run

Requires **Windows 10/11 x64 and .NET Framework 4.8**. Windows 11 includes the runtime. Extract a portable ZIP and open `AudioMe.exe`; no installer or administrator access is required. The icon is embedded, so the EXE can be moved by itself.

- Closing the window hides it to the tray. Launch it again or use its tray icon to reopen it. Right-click the tray icon and select **Exit** to quit; exit is deferred while switching is in progress.
- `--tray` starts it hidden. **Start with Windows** writes a current-user startup entry pointing to this EXE. If you move it, turn that option off and on from its new location.
- Settings live at `%LOCALAPPDATA%\AudioSwitch\settings.xml`; the old folder name is retained for compatibility. The app is portable to run, but preferences are stored per user outside the EXE folder.
- On a settings-load failure, preserve or repair the original file before restarting, or choose **Back up original and reset**. Its recovery backup path is reported after reset. Normal saves keep `settings.xml.bak`.

## Compatibility and limitations

- Apps with explicitly selected audio devices can ignore Windows defaults. Some existing sessions require reselection or restart. Audio Me does not override per-app routing or restart apps.
- Switching uses the undocumented Windows PolicyConfig interface. Discovery and verification use public Core Audio APIs. Windows updates may require compatibility fixes.
- Pair changes are sequential OS operations, not an atomic transaction. Restoration is attempted and verified, but cannot be guaranteed if a device disappears or Windows rejects a call.
- Device status is polled every two seconds; availability is checked again by the switching service before changing defaults.
- Aliases are local labels, not driver or Windows device renames. Driver reinstalls or USB-port changes can produce new endpoint IDs; edit affected profiles explicitly.
- The UI is system-DPI aware. Exit and reopen after moving between monitors with different scaling or changing display scaling. True per-monitor DPI transitions are not implemented. Small windows scroll; full names and long availability reasons are in tooltips.
- Builds are **unsigned**. No signing service has been purchased and no public release has been published as part of this update.

## Build and validate

From PowerShell in the repository directory:

```powershell
.\build.ps1       # Windows Framework compiler; no NuGet or .NET SDK required
.\test.ps1        # Connection, preference, migration and settings-recovery tests
.\test-ui.ps1     # Fake-device UI tests and rendered layouts; no live default changes
.\release.ps1     # Local portable ZIP and SHA-256 checksum in dist/; does not publish
```

Exit a running copy before rebuilding. `AudioMe.csproj` supports Visual Studio/MSBuild with the .NET Framework 4.8 developer pack. `build.ps1` is the canonical release build and rebuilds `AudioSwitch.exe` for legacy shortcuts.

The GitHub Windows workflow builds, runs source-level tests, and packages downloadable artifacts on pushes and pull requests. UI rendering checks run separately on an interactive Windows desktop. Binaries, diagnostic reports, local settings and test screenshots are excluded from Git.

Tests cover settings migration/recovery, six-role profile matching, missing endpoints and reconnection, offline edits without substitution, preferences/window dimensions, keyboard focus, and responsiveness/failure feedback with a fake service. Layouts were rendered at 440×480, 480×660, and 720×900 client sizes and with simulated 150%/200% control scaling. Simulated scaling does not verify real monitor DPI transitions.

**Hardware validation still required:** Bluetooth transitions, USB/HDMI disconnects during switching, sleep/resume, app-specific routing, real failed-switch restoration, and assistive-technology testing. No live defaults were changed during this revision. See the [beta checklist](docs/BETA-CHECKLIST.md).

## Development and distribution

See [architecture and premium boundaries](docs/ARCHITECTURE.md). The next recommended step is a hardware beta pass and real multi-monitor/accessibility testing before adding premium automation.

The repository remains private. No open-source license is granted; choose licensing and distribution terms before a public beta. Source and portable packages retain only assets used by the app plus the logo source and notes.

API references: [Windows device roles](https://learn.microsoft.com/en-us/windows/win32/coreaudio/device-roles), [GetDefaultAudioEndpoint](https://learn.microsoft.com/en-us/windows/win32/api/mmdeviceapi/nf-mmdeviceapi-immdeviceenumerator-getdefaultaudioendpoint), [PolicyConfig interface declaration](https://github.com/amate/SetDefaultAudioDevice/blob/master/PolicyConfig.h).
