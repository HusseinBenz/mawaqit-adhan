param([string]$Release = 'release-48')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$base = "https://raw.githubusercontent.com/unicode-org/cldr/$Release/common"
$zones = [xml](Invoke-WebRequest "$base/supplemental/windowsZones.xml" -UseBasicParsing).Content
$aliases = [xml](Invoke-WebRequest "$base/bcp47/timezone.xml" -UseBasicParsing).Content
$map = @{}
foreach ($zone in $zones.supplementalData.windowsZones.mapTimezones.mapZone) {
    foreach ($iana in $zone.type.Split(' ', [StringSplitOptions]::RemoveEmptyEntries)) {
        $map[$iana] = $zone.other
    }
}
foreach ($type in $aliases.ldmlBCP47.keyword.key.type) {
    if (!$type.alias) { continue }
    $names = $type.alias.Split(' ', [StringSplitOptions]::RemoveEmptyEntries)
    $known = $names | Where-Object { $map.ContainsKey($_) } | Select-Object -First 1
    if ($known) { foreach ($name in $names) { $map[$name] = $map[$known] } }
}
$lines = $map.Keys | Sort-Object | ForEach-Object { "$_`t$($map[$_])" }
$lines | Set-Content (Join-Path $root 'src/MawaqitAdhan/Core/windows-zones.tsv') -Encoding UTF8
Write-Output "Wrote $($map.Count) IANA names and aliases from Unicode CLDR $Release."
