# Mawaqit Adhan

A small Windows tray app that plays the adhan using a masjid's own Mawaqit timetable.
Five adhan recordings are included. No account, installer, administrator rights,
third-party runtime packages, web browser engine, or bundled .NET runtime is required.

## Run

Extract `dist/MawaqitAdhan-1.0.0-beta.1.zip` and run **MawaqitAdhan.exe**. Keep the `audio`
folder beside it. An extracted copy is also available in `dist/MawaqitAdhan-1.0.0-beta.1`.
On first launch, search for a masjid by name/city or paste its Mawaqit link, then
choose **Use this**. **Test adhan** plays the selected recording; **Stop** stops it.
Close normally hides the app in the tray. Use the tray menu's **Exit** to quit.

Requires **.NET Framework 4.8 or later**, already included with Windows 11 and
recent Windows 10 versions. Older installations can use Microsoft's
[.NET Framework 4.8 installer](https://dotnet.microsoft.com/download/dotnet-framework/net48).
The .NET 8 Desktop Runtime is no longer needed.

## Compatibility

The AnyCPU executable runs as 64-bit on x64 Windows and 32-bit on x86 Windows.
The compatibility target is Windows 7 SP1, Windows 8.1, Windows 10 version 1607 or
later, and Windows 11 with .NET Framework 4.8. Windows XP, Vista and Windows 8.0
are outside this target. See Microsoft's
[Framework system requirements](https://learn.microsoft.com/dotnet/framework/get-started/system-requirements).

Both x86 and x64 code paths were tested on Windows 11; physical Windows 7/8.1/10
machines and Windows on ARM have not been tested. Older Windows installations
need working HTTPS/TLS 1.2, current root certificates and time-zone updates for
online retrieval and correct local DST rules. Windows N/KN needs its Media Feature
Pack for MP3 playback. Unsupported time zones produce an error instead of silently
scheduling prayers in the PC's local zone.

## Features

- Search any published Mawaqit masjid by name/city or its link.
- Full-year timetable cached for offline use, refreshed at launch and about every
  12 hours; failed background requests retry after 20 minutes.
- Time-zone and daylight-saving handling, including half-hour DST transitions.
  The masjid's IANA zone is mapped to Windows rules using an embedded CLDR table.
- Five daily adhans, individual prayer switches, a separate Fajr voice and volume.
  Sunrise is displayed but never called.
- Iqama offsets/fixed times, Hijri adjustment and Friday Jumu'ah display. Dhuhr still
  plays at the published Dhuhr time; the separate Jumu'ah display does not replace it.
- Five bundled recordings, additional CDN recordings, MP3 import and preview.
- Configurable fading, media pause/resume, advance reminders and iqama notices.
- Tray operation, optional start with Windows, and single-instance behavior.
- Existing settings and caches from the earlier .NET 8 build are read automatically.

The app must be running and the PC awake to call prayers. A missed prayer can play
up to three minutes late after a brief sleep or stall; older missed prayers are
skipped. Played/disabled prayer markers are saved per masjid to prevent replays
on restart. A cached calendar is valid only for its fetched year; connect again
at the start of a new year. If a masjid publishes only today's times, that cache
is valid only for that date. Notifications follow Windows notification settings.

## Other audio

By default, when another app is producing audio, system volume fades down over
2 seconds, a media play/pause key is sent, and volume returns to its original
level for the adhan. After the adhan finishes, the app waits 5 seconds, resumes
media and fades in over 3 seconds. These steps can be changed or disabled.

Stop and exit cancel delays and finish restoring audio. If other audio has
already resumed, the app skips the second toggle. Media keys are best effort:
players that ignore them can keep playing, and a global key cannot reliably
choose between multiple media players. A silent passage can affect detection.
User volume changes during the adhan are preserved. The app does not override
system mute; its own volume setting is multiplied by the system volume.

## Build and verify

Build on Windows with a modern .NET SDK and the **.NET Framework 4.8 Developer
Pack/targeting pack**. The developer pack is only needed to build, not to run.
There are no NuGet package dependencies.

```powershell
dotnet build src/MawaqitAdhan -c Release
powershell -NoProfile -File tools/package.ps1
```

The packaging script produces the portable folder, ZIP and SHA-256 checksum under
`dist`. The ZIP is approximately 10 MB; almost all of that is the five MP3 files.

```powershell
# Offline regression checks; settings/data are isolated automatically.
dotnet run --project tools/RegressionCheck -c Release
# Also verify native audio (plays a short tone), downloads and MP3 import.
dotnet run --project tools/RegressionCheck -c Release -- --audio --online
# Live Mawaqit parsing, calendar, DST, cache and search verification.
$env:MAWAQIT_ADHAN_DATA_DIR = Join-Path $PWD 'artifacts/test-data/core'
dotnet run --project tools/CoreCheck -c Release -- omar-witten grande-mosquee-de-paris anatolia-north-york
Remove-Item Env:MAWAQIT_ADHAN_DATA_DIR
```

See `tools/README.md` and `HANDOFF.md` for verification details and remaining
manual checks. Passing tests cannot guarantee the absence of every bug.

## Data and implementation

Data lives in `%APPDATA%\MawaqitAdhan`: `settings.json`, `cache\<slug>.json`,
`audio\` for imported/downloaded voices, and `mawaqit-adhan.log` (512 KB plus one
backup). Settings and caches are replaced atomically. For isolated testing,
`MAWAQIT_ADHAN_DATA_DIR` can override the data directory. No telemetry is sent.

The app uses WinForms, Windows MCI on a dedicated STA thread for MP3 playback,
and Core Audio for volume/detection. The scheduler checks once per second and
resolves its rolling schedule once per day or timetable update. Hidden windows
do not repaint the countdown. Network downloads are bounded and cancellable.

Mawaqit data is retrieved from its public search endpoint
`https://mawaqit.net/api/2.0/mosque/search?word=...` and the JSON `confData` embedded
in `https://mawaqit.net/en/<slug>`. MP3 downloads use
`https://cdn.mawaqit.net/audio/<voice>.mp3`. Changes to those public endpoints may
require an app update; a usable cache continues to work offline.

Source: `src/MawaqitAdhan/Core` (data, scheduling, playback) and
`src/MawaqitAdhan/Ui` (main window, settings, picker and painted controls).
The embedded time-zone mapping and aliases come from
[Unicode CLDR 48](https://github.com/unicode-org/cldr/tree/release-48/common).
Regenerate with `tools/update-timezones.ps1`; the Unicode license is included in
`assets/UNICODE-LICENSE.txt` and in the portable release.
