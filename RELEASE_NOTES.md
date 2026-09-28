# Mawaqit Adhan 1.0.0-beta.2

Released 2026-09-28.

## Changelog

- Fixed the global play/pause bug that could start an already-paused player (for example MPC-HC) while pausing YouTube in Brave for the adhan.
- Pause only Windows media sessions that report Playing. Resume only sessions successfully paused by the app and still paused afterward. Already-paused, stopped, and manually resumed players are left alone.
- Removed the global media-key fallback. Unsupported players are left untouched instead of risking starting other media.
- Added a standalone per-user Windows installer, existing-installation detection, in-place upgrades, graceful app shutdown, rollback on installation errors, a Start menu shortcut, and a Windows Installed Apps uninstall entry.
- Preserve settings, cached timetables, imported/downloaded voices, custom files, and the user's enabled/disabled startup preference during upgrades. Uninstall keeps user data.
- Added multi-player and installer regression coverage; excluded sync-conflict source copies from builds.

## Downloads

- **MawaqitAdhan-1.0.0-beta.2-Setup.exe** — recommended installation and upgrade program.
- **MawaqitAdhan-1.0.0-beta.2.zip** — portable version; extract and run MawaqitAdhan.exe with its audio folder beside it.
- **SHA256SUMS.txt** — SHA-256 checksums for both downloads.

## Upgrade behavior

Run Setup.exe. It first detects an installation registered by this installer, then an existing portable copy through the current user's Windows startup entry, a running app, or known install locations. The detected folder is shown before updating. Use Browse for a portable copy in another location. It does not scan every disk or delete other copies.

The installer closes the running app through Windows Restart Manager before replacing files. If Windows cannot close it gracefully, exit it through the tray menu and retry. Files are backed up during installation and restored if installation fails. Startup is redirected to the selected installation only when it was already enabled.

No administrator rights are needed for a writable per-user folder. .NET Framework 4.8 or later must already be installed. Settings and voices remain in `%APPDATA%\MawaqitAdhan`.

## Verification and limitations

- Release app and installer built with no warnings/errors.
- 43 application regression checks passed on each of x64 and x86 Windows 11, including native media-session enumeration.
- Installer checks cover clean install, repeat install, legacy portable upgrade, startup preservation, copy/registration failure rollback, safe extraction, uninstall, and graceful shutdown of the previous tray app.
- Media session control requires Windows 10 version 1809 or later and a player that exposes Windows media controls. The mixed-player regression uses simulated sessions; a live Brave/MPC-HC combination has not been verified.
- Older Windows and ARM hardware have not been tested directly. The app and installer are not Authenticode-signed.

Desktop app source is on the `desktop-app` branch. The default branch remains the download website.
