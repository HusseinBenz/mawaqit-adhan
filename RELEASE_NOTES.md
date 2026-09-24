# Mawaqit Adhan 1.0.0-beta.1

First beta of the lightweight Windows prayer-time tray application.

- Uses the selected masjid's own Mawaqit timetable, with an offline calendar cache.
- Includes five recordings, separate Fajr audio, downloads, MP3 import and preview.
- Supports per-prayer controls, iqama notifications, tray operation and Windows startup.
- Provides configurable audio fading and media pause/resume.
- Handles time zones, DST transitions and prayer times after midnight.
- Portable AnyCPU executable targeting .NET Framework 4.8; no third-party runtime DLLs.

## Install

Download and extract `MawaqitAdhan-1.0.0-beta.1.zip`, keep the audio folder beside
the executable, and run `MawaqitAdhan.exe`. Older systems may need Microsoft's
.NET Framework 4.8 installer. Check `SHA256SUMS.txt` to verify the archive.

## Beta status

Build and regression checks passed; native audio was previously verified on x86
and x64 Windows 11. The compatibility target includes Windows 7 SP1, Windows 8.1,
Windows 10 version 1607 or later, and Windows 11. Older Windows and ARM devices
have not been tested directly. Media-key handling depends on the other player.
See README.md and HANDOFF.md for detailed verification and remaining manual checks.
