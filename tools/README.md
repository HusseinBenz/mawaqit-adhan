# Verification tools

These tools are not shipped in the app. Build on Windows with a modern .NET SDK
and the .NET Framework 4.8 targeting pack. No test-framework packages are needed.

## RegressionCheck

`dotnet run --project tools/RegressionCheck -c Release`

Uses a unique temporary data directory. Tests settings migration/defaults,
atomic writes, cache validation, parsing, time-zone mapping, DST, iqama,
window construction, the Test adhan regression, and playback sequencing with
fake audio. Covers cancellation during fade, delayed resume, manual resume,
shutdown, decoder failure and duplicate-start rejection.

Add `-- --audio` to check native MCI playback from a thread-pool caller, active
playback, nonzero samples in the app's audio session, and stop. This plays a
short tone at 35% of system volume. Add `--online` to test CDN downloading,
Windows decoding, MP3 import and cancellation cleanup in the isolated directory.

To exercise the 32-bit path on a 64-bit PC:

```powershell
dotnet build tools/RegressionCheck -c Release -p:PlatformTarget=x86 -o artifacts/check-x86
./artifacts/check-x86/RegressionCheck.exe --audio
```

## CoreCheck

Tests live timetable retrieval, a whole-year scan, cache round trips, next/current
prayers, DST boundaries, helper functions, name search and coordinate search.

```powershell
$env:MAWAQIT_ADHAN_DATA_DIR = Join-Path $PWD 'artifacts/test-data/core'
dotnet run --project tools/CoreCheck -c Release -- omar-witten grande-mosquee-de-paris anatolia-north-york
Remove-Item Env:MAWAQIT_ADHAN_DATA_DIR
```

## Packaging and time-zone data

`powershell -NoProfile -File tools/package.ps1` builds into an isolated staging
folder and creates `dist/MawaqitAdhan`, the release ZIP and a SHA-256 checksum.
It fails if an unexpected DLL dependency appears in the output.

`tools/update-timezones.ps1` rebuilds the embedded mapping from pinned Unicode
CLDR 48 Windows mappings and IANA aliases. It needs internet access; ordinary
builds use the checked-in table and do not fetch it.

## Optional manual UI diagnostics

`ui-automation.ps1` contains legacy Win32 window diagnostics and captures.
`ui-automation-buttons.ps1` adds button helpers and resolves paths relative to
this repository. Its `Start-App` stops an existing app instance, so use it only
when intentionally restarting the app. Prefer an isolated data directory.
The old `mci-apartment-probe.cs` records the STA/MTA decoder diagnostic that led
to the dedicated audio thread. RegressionCheck now exercises the actual player.

Manual release checks still useful on each target Windows version: first-run
cancel/reopen; city search; settings/scrolling and Fajr preview; close/minimise to
tray; relaunch and single-instance activation; Windows startup; notifications;
real media-player pause/resume; and playback-device changes during a fade.

## Standard Inno Setup installer

Install Inno Setup 6, then run `powershell -NoProfile -File tools/package.ps1`.
The script uses `tools/installer.iss` to build the standard Windows setup wizard.
Pass `-IsccPath` if the compiler is installed in a nonstandard location.

Run `powershell -NoProfile -File tools/test-installer.ps1` after packaging.
This compiles the same Inno script with an isolated application ID and startup
value. Tests cover clean installation, repeat upgrade, beta 2 custom-installer
migration, portable startup-path detection, settings/custom-file preservation,
and standard uninstallation. The user's normal registration is never modified.
If the normal app is not running, a previous executable is launched with isolated
data to exercise Restart Manager. Otherwise that process test is skipped.

Integration logs and test files are kept under `artifacts/inno-test`.
The old custom installer and its test harness were removed; history retains them.
