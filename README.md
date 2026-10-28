# Audio Me

A small native Windows tray widget for selecting default playback and recording devices.

![Audio Me logo](Assets/AudioMe.png)

## Requirements

Windows 10/11 x64 with .NET Framework 4.8. Windows 11 includes the runtime. The EXE runs as the current user and does not require installation or administrator access.

## Run

For a packaged release, extract the ZIP and open `AudioMe.exe`. For a source checkout, run `build.ps1`, then open `bin\AudioMe.exe`. The legacy `AudioSwitch.exe` filename is also rebuilt for existing shortcuts.

The AM logo is embedded in the executable and used by the window, taskbar and tray. Artwork lives in `Assets\AudioMe.png`; `Assets\AudioMe.ico` contains Windows icon sizes from 16 to 256 pixels. Existing settings remain in the AudioSwitch folder to preserve saved groups and preferences.

The UI uses 10.5-point Segoe UI and system-DPI-aware rendering rather than Windows bitmap stretching. After changing screen scaling or moving to a display with a different scale, exit and reopen the app for the sharpest result. Per-monitor DPI transitions are not currently supported.

- Click a speaker or microphone button to assign that endpoint to **System, Media, and Calls** (Console, Multimedia, Communications).
- Current defaults have a checkmark and a subtle teal highlight. Lists scroll when needed.
- Click ☆ on the right of a device to favorite it (★). Favorites sort alphabetically at the top of each device list, followed by regular devices alphabetically.
- Click the red × to move a device to the bottom and render its name at 50% opacity against its background. Click × again to restore it. Dimmed devices also sort alphabetically and remain usable. Favoriting a dimmed device restores it; dimming a favorite removes its star. Preferences are saved by endpoint ID across restarts and reconnections.
- **Manage groups → New group** lets you choose a name, speakers, and microphone. Use names such as FPS Gaming, Work, or Casual. Edit / rename and Delete apply to the selected saved group.
- The **Group** dropdown contains only groups whose two endpoints are active and connected. Selecting a name immediately applies both devices, including all three audio roles. Disconnected groups remain saved in the manager and return automatically when both endpoints reconnect. Editing an offline group retains its saved device unless you choose a replacement.
- **Keep on top** pins the widget above other windows.
- **Hide**, closing the window, or clicking the tray icon hides the widget. Click the tray icon to reopen it. Right-click the tray icon and choose **Exit** to quit.
- The last normal window size is saved when resizing, hiding, or exiting and restored on launch (clamped to the current display). Launching the EXE again, including from a pinned taskbar shortcut, brings the existing widget forward instead of silently exiting.
- Run with `--tray` to start hidden. Automatic Windows startup is not enabled by this app; you can place a shortcut with that argument in your Windows Startup folder if desired.

Profiles and the pin preference are saved in `%LOCALAPPDATA%\AudioSwitch\settings.xml`. Existing saved pairs remain compatible. Profiles use endpoint IDs so identically named devices remain distinct; new group names must be unique. Reinstalling a driver or changing USB ports can change endpoint IDs; edit affected groups to select their replacement devices.

## What “verified” means

The app calls the Windows PolicyConfig COM interface for every role and then reads each default back through `IMMDeviceEnumerator.GetDefaultAudioEndpoint`. It reports success only when all requested defaults match. If any call or verification fails, it attempts to restore all prior role assignments and reports restoration failures. Pair changes are sequential Windows operations, not an atomic OS transaction.

This changes Windows defaults. Apps with explicit per-app routing or their own selected device can continue using those choices. An app that does not respond to default-device changes may need its device selection changed or its audio session restarted. This widget does not override per-app routing, restart applications, or force existing streams onto another endpoint.

PolicyConfig is undocumented and may change with a Windows update. The public Core Audio APIs are used for discovery and verification. The widget polls every two seconds for device/default changes; it does not continuously force a preferred default or record microphone audio.

## Build

From PowerShell in this directory:

```powershell
.\build.ps1
```

The script uses the Windows .NET Framework C# compiler, so no .NET SDK or NuGet restore is required. Exit a running copy before rebuilding.

`AudioMe.csproj` is also available for Visual Studio/MSBuild with the .NET Framework 4.8 developer pack. The PowerShell build is the canonical release build.

```powershell
.\test.ps1     # Build first; verifies connection filtering and preference persistence
.\release.ps1  # Builds, tests, creates a portable ZIP and SHA-256 checksum in dist/
```

The Windows GitHub Actions workflow builds, tests, and packages the same release on pushes and pull requests. Generated binaries, ZIPs, local settings, and diagnostic reports are excluded from Git.

## GitHub setup

Create an empty **private** repository named `audio-me` in your GitHub account. Do not initialize it with another README. From this source directory:

```powershell
git remote add origin https://github.com/YOUR-ACCOUNT/audio-me.git
git push -u origin main
```

Upload the ZIP and its `.sha256` file from `dist/` as release assets when you want to distribute a build. The release ZIP contains only the EXE and usage instructions; source files belong in the repository. Builds are currently unsigned. Keep the repository private until you choose its sharing and licensing terms; no open-source license is granted by this repository.

Read-only device report (use an absolute output path):

```powershell
Start-Process .\bin\AudioSwitch.exe -ArgumentList '--diagnose "C:\path\devices.txt"' -Wait
```

## Validation on the development machine

- Compiled successfully using the installed x64 .NET Framework compiler.
- Enumerated eight active playback endpoints and four active recording endpoints and read their role assignments.
- Opened and visually inspected the widget.
- Rebuilt the compact charcoal interface and inspected the main widget and group manager.
- ConnectionTests passed for both connected endpoints, either endpoint missing, both missing, incorrect direction, reconnection, and rejection of a disconnected endpoint before switching.
- Reapplied the existing fifine microphone default via the UI and verified all three roles successfully.
- Switching to a different endpoint, pair persistence, unplug/replug behavior, and failure rollback have not yet been exercised end to end.

API references: [Windows device roles](https://learn.microsoft.com/en-us/windows/win32/coreaudio/device-roles), [GetDefaultAudioEndpoint](https://learn.microsoft.com/en-us/windows/win32/api/mmdeviceapi/nf-mmdeviceapi-immdeviceenumerator-getdefaultaudioendpoint), [PolicyConfig interface declaration](https://github.com/amate/SetDefaultAudioDevice/blob/master/PolicyConfig.h).
