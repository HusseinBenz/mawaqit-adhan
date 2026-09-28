param([string]$IsccPath = '')
$ErrorActionPreference = 'Stop'
if (!$IsccPath) {
    $candidates = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
    )
    $IsccPath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (!$IsccPath -or !(Test-Path -LiteralPath $IsccPath)) { throw 'Install Inno Setup 6, or pass -IsccPath with the compiler path.' }
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
$fileVersion = [string]$project.Project.PropertyGroup.FileVersion
$setupStage = Join-Path $root ("artifacts/setup/" + [Guid]::NewGuid().ToString('N'))
& $IsccPath "/DAppVersion=$version" "/DFileVersion=$fileVersion" "/DPayloadDir=$stage" "/DOutputDir=$setupStage" (Join-Path $root 'tools/installer.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
$installerName = "MawaqitAdhan-$version-Setup.exe"
$installer = Join-Path $dist $installerName
Copy-Item -LiteralPath (Join-Path $setupStage $installerName) -Destination $installer -Force
@($zip, $installer) | ForEach-Object {
    "$( (Get-FileHash $_ -Algorithm SHA256).Hash )  $(Split-Path $_ -Leaf)"
} | Set-Content (Join-Path $dist 'SHA256SUMS.txt') -Encoding ASCII
Write-Output "Portable executable: $portable\MawaqitAdhan.exe"
Write-Output "Archive: $zip"
Write-Output "Installer: $installer"
Write-Output "Archive size: $([math]::Round((Get-Item $zip).Length / 1MB, 2)) MiB"
