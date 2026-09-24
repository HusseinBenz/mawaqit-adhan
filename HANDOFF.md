# Handoff — release verification, 2026-09-20

The app now targets .NET Framework 4.8 AnyCPU with no NuGet/runtime package
additions. Build and portable packaging instructions are in README.md.

## Completed

- Fixed Test adhan dereferencing a null prayer.
- Verified the dedicated STA MCI player opens MP3s, reports playback, produces
  nonzero audio samples and stops on both x64 and x86 on Windows 11.
- Fixed cancellation during fade, the uncancellable resume delay, exit racing
  volume restoration, undisposed audio wait handles, and timeout false success.
- Preserved manually resumed media and volume changes during the adhan.
- Added visible playback failures, bounded/cancellable voice downloads with
  atomic completion and cleanup, and cancellation on dialog/window disposal.
- Fixed timeout reporting, old-year calendar reuse, duplicate keys across
  masjids, next-day highlighting, zero-minute iqama, half-hour DST gaps and
  after-midnight Isha/iqama (including next/current lookup across midnight).
- Added embedded IANA-to-Windows mapping plus aliases, safe unknown-zone failure,
  old settings compatibility and cache validation.
- Reduced repeated schedule work and hidden-window repainting; bounded in-memory
  notification history and cleaned up cancellation sources and icon handles.
- Removed .NET 8 runtime requirement. Portable release contains no dependency DLLs.

## Evidence

- Release build: zero warnings/errors.
- RegressionCheck: offline checks plus native audio checks passed on x64 and x86.
  x64 also passed voice download, decode, import and cancelled-download cleanup.
- CoreCheck: Witten, Paris and Toronto live timetables, complete-year scans,
  cache round trips, time-zone/DST checks and both search endpoints passed.
- UI checked on this Windows 11 machine: first-run picker, pasted-link search,
  selection persistence and main window timetable. User stopped Computer Use
  with Escape during the settings check; no further UI automation was performed.
- A short visible-window idle sample measured about 43.5 MB private memory,
  79.8 MB working set and 0.016 CPU seconds over five wall-clock seconds. These
  are one-machine measurements, not performance guarantees.

Check outputs are under artifacts when run locally. RegressionCheck uses a fresh
system-temp data directory. UI verification used artifacts/test-data/ui and did
not modify the user's normal AppData settings. The test app was stopped afterward.

## Remaining environment verification

Windows 7 SP1/8.1/10 and ARM hardware were not available for direct verification.
Compatibility is based on .NET Framework/API targeting, with x86/x64 execution
verified on Windows 11. Real Spotify/browser/VLC media-key behavior, startup at
logon, tray relaunch, notification delivery, sleep/wake and device removal still
benefit from manual tests on the target machines. Media keys remain best effort.

Keep hand-placed UI coordinates scaled through Dpi. The manifest uses system
DPI awareness and forms disable WinForms automatic scaling. The audio pump must
remain STA. Do not reuse a previous year's calendar without a fresh fetch.
