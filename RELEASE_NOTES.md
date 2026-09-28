# Mawaqit Adhan 1.0.0-beta.3

Released 2026-09-28.

## Changelog

- Replaced the custom setup program with **Inno Setup 6**, using its standard Windows installation wizard and uninstaller.
- Supports fresh installation, repeat upgrades, and migration from the beta 2 custom installer. Upgrading beta 2 replaces its obsolete uninstall registration and helper files.
- Detects registered installations, the current user's startup entry, and running portable copies. The destination page also lets you select a different existing copy.
- Preserves settings, cached timetables, downloaded/imported voices, custom files, and the enabled/disabled Windows startup preference.
- Uses Windows Restart Manager to close applications during upgrades. Uninstall keeps user data.
- Keeps beta 2's media fix: pause only playing media sessions, then resume only sessions the app paused. Already-paused players remain paused.

## Downloads

- **MawaqitAdhan-1.0.0-beta.3-Setup.exe** — standard Inno Setup installer; recommended for installation and upgrades.
- **MawaqitAdhan-1.0.0-beta.3.zip** — portable version.
- **SHA256SUMS.txt** — SHA-256 checksums for both packages.

Run Setup, review the detected destination, and follow the standard wizard. No administrator rights are required for a writable per-user location. If a portable copy cannot be detected, select its folder on the destination page. Other copies elsewhere on the PC are not deleted.

## Requirements and verification

- .NET Framework 4.8 or later is required; Setup checks this before installing.
- App and Inno Setup packages build successfully. Isolated installer integration checks cover fresh install, repeat upgrade, migration from the custom installer, portable discovery, startup preservation, and uninstall without removing custom files.
- The beta 2 app regression suite passed on x64 and x86 Windows 11. Playback behavior is unchanged in beta 3.
- Windows media control requires Windows 10 version 1809 or later and a supported media player. Older Windows/ARM devices and a live mixed Brave/MPC-HC playback scenario have not been verified.
- The release is unsigned. Desktop source and installer scripts are on the `desktop-app` branch; the default branch hosts the website.
