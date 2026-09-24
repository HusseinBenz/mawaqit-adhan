$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = [xml](Get-Content (Join-Path $root 'src/MawaqitAdhan/MawaqitAdhan.csproj') -Raw)
$version = [string]$project.Project.PropertyGroup.Version
$stage = Join-Path $root ("artifacts/package/" + [Guid]::NewGuid().ToString('N'))
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $stage, $dist | Out-Null
dotnet build (Join-Path $root 'src/MawaqitAdhan') -c Release -o $stage --nologo
if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
if (Get-ChildItem $stage -Filter '*.dll') { throw 'Unexpected runtime dependency in release.' }
Copy-Item (Join-Path $root 'README.md') (Join-Path $stage 'README.md')
$archiveName = "MawaqitAdhan-$version.zip"
$zip = Join-Path $dist $archiveName
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
$portable = Join-Path $dist "MawaqitAdhan-$version"
New-Item -ItemType Directory -Force $portable | Out-Null
Copy-Item (Join-Path $stage '*') $portable -Recurse -Force
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash
"$hash  $archiveName" | Set-Content (Join-Path $dist 'SHA256SUMS.txt') -Encoding ASCII
Write-Output "Portable executable: $portable\MawaqitAdhan.exe"
Write-Output "Archive: $zip"
Write-Output "Archive size: $([math]::Round((Get-Item $zip).Length / 1MB, 2)) MiB"
