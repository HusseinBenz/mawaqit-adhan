$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = [xml](Get-Content (Join-Path $root 'src/MawaqitAdhan/MawaqitAdhan.csproj') -Raw)
$version = [string]$project.Project.PropertyGroup.Version
$stage = Join-Path $root ("artifacts/package/" + [Guid]::NewGuid().ToString('N'))
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $stage, $dist | Out-Null
dotnet build (Join-Path $root 'src/MawaqitAdhan/MawaqitAdhan.csproj') -c Release -o $stage --nologo
if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
if (Get-ChildItem $stage -Filter '*.dll') { throw 'Unexpected runtime dependency in release.' }
Copy-Item (Join-Path $root 'README.md') (Join-Path $stage 'README.md')
Copy-Item (Join-Path $root 'RELEASE_NOTES.md') (Join-Path $stage 'RELEASE_NOTES.md')
$archiveName = "MawaqitAdhan-$version.zip"
$zip = Join-Path $dist $archiveName
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
$portable = Join-Path $dist "MawaqitAdhan-$version"
New-Item -ItemType Directory -Force $portable | Out-Null
Copy-Item (Join-Path $stage '*') $portable -Recurse -Force
$setupStage = Join-Path $stage 'setup-build'
dotnet build (Join-Path $root 'tools/Setup/Setup.csproj') -c Release -o $setupStage "-p:PayloadPath=$zip" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
$installerName = "MawaqitAdhan-$version-Setup.exe"
$installer = Join-Path $dist $installerName
Copy-Item (Join-Path $setupStage 'MawaqitAdhan-Setup.exe') $installer -Force
@($zip, $installer) | ForEach-Object {
    "$( (Get-FileHash $_ -Algorithm SHA256).Hash )  $(Split-Path $_ -Leaf)"
} | Set-Content (Join-Path $dist 'SHA256SUMS.txt') -Encoding ASCII
Write-Output "Portable executable: $portable\MawaqitAdhan.exe"
Write-Output "Archive: $zip"
Write-Output "Installer: $installer"
Write-Output "Archive size: $([math]::Round((Get-Item $zip).Length / 1MB, 2)) MiB"
