param([string]$IsccPath = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe")
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$testRoot = Join-Path $root ('artifacts/inno-test/' + [Guid]::NewGuid().ToString('N'))
$payload = Join-Path $root 'dist/MawaqitAdhan-1.0.0-beta.3'
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\MawaqitAdhan-InstallerTest_is1'
$legacyKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\MawaqitAdhan-InstallerTest-Legacy'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runName = 'MawaqitAdhan-InstallerTest'
if ((Test-Path $uninstallKey) -or (Test-Path $legacyKey) -or (Get-ItemProperty $runKey).PSObject.Properties[$runName]) {
    throw 'A previous installer test registration exists; inspect it before running again.'
}
New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
& $IsccPath /Qp /DInstallerTest "/DPayloadDir=$payload" "/DOutputDir=$testRoot" (Join-Path $PSScriptRoot 'installer.iss')
if ($LASTEXITCODE -ne 0) { throw 'Test installer compilation failed.' }
$setup = Join-Path $testRoot 'MawaqitAdhan-1.0.0-beta.3-Setup.exe'
function Check([bool]$ok, [string]$message) {
    if (!$ok) { throw "FAIL: $message" }
    Write-Output "PASS: $message"
}
function RunSetup([string]$name, [string]$directory = '') {
    $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/NOICONS', '/CLOSEAPPLICATIONS', '/NOFORCECLOSEAPPLICATIONS', ('/LOG="' + (Join-Path $testRoot "$name.log") + '"'))
    if ($directory) { $arguments += '/DIR="' + $directory + '"' }
    $process = Start-Process -FilePath $setup -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(60000)) { throw 'Setup did not complete within 60 seconds.' }
    Check ($process.ExitCode -eq 0) "$name completes successfully"
}
function Uninstall([string]$directory) {
    $process = Start-Process -FilePath (Join-Path $directory 'unins000.exe') -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(60000)) { throw 'Uninstall did not complete within 60 seconds.' }
    Check ($process.ExitCode -eq 0) 'standard Inno uninstall succeeds'
    # Inno hands off to a temporary uninstaller; the original process can exit first.
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        $pending = (Test-Path $uninstallKey) -or (Test-Path (Join-Path $directory 'unins000.exe')) -or
            [bool](Get-ItemProperty $runKey).PSObject.Properties[$runName]
        if (!$pending) { break }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    Check (!$pending) 'uninstall worker finishes cleanup'
}

$fresh = Join-Path $testRoot 'fresh install'
RunSetup 'fresh' $fresh
Check ((Get-ItemProperty $uninstallKey).DisplayVersion -eq '1.0.0-beta.3') 'Windows Installed Apps records beta 3'
Check (!(Get-ItemProperty $runKey).PSObject.Properties[$runName]) 'startup stays disabled on a clean install'
RunSetup 'repeat'
Check ((Get-ItemProperty $uninstallKey).InstallLocation.TrimEnd('\') -eq $fresh) 'repeat setup reuses the installed location automatically'
Uninstall $fresh
Check (!(Test-Path $uninstallKey)) 'uninstall removes the Inno registration'

$legacy = Join-Path $testRoot 'legacy portable'
New-Item -ItemType Directory -Force -Path $legacy | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'dist/MawaqitAdhan-1.0.0-beta.2/MawaqitAdhan.exe') -Destination $legacy
Set-Content -LiteralPath (Join-Path $legacy 'Uninstall.exe') -Value 'old custom uninstaller sentinel'
Set-Content -LiteralPath (Join-Path $legacy 'installation.json') -Value '{"Product":"MawaqitAdhan"}'
Set-Content -LiteralPath (Join-Path $legacy 'custom-voice.mp3') -Value 'preserve me'
New-Item -Path $legacyKey -Force | Out-Null
Set-ItemProperty $legacyKey -Name InstallLocation -Value $legacy
Set-ItemProperty $runKey -Name $runName -Value ('"' + (Join-Path $legacy 'MawaqitAdhan.exe') + '" --tray')
$testApp = $null
try {
    if (!(Get-Process MawaqitAdhan -ErrorAction SilentlyContinue)) {
        $data = Join-Path $testRoot 'isolated app data'
        New-Item -ItemType Directory -Path $data | Out-Null
        Set-Content (Join-Path $data 'settings.json') '{"MosqueSlug":"omar-witten","AdhanEnabled":[false,false,false,false,false],"ShowNotifications":false}'
        $info = New-Object Diagnostics.ProcessStartInfo
        $info.FileName = Join-Path $legacy 'MawaqitAdhan.exe'
        $info.Arguments = '--tray'
        $info.UseShellExecute = $false
        $info.EnvironmentVariables['MAWAQIT_ADHAN_DATA_DIR'] = $data
        $testApp = [Diagnostics.Process]::Start($info)
        Start-Sleep -Seconds 2
        Check (!$testApp.HasExited) 'old application starts with isolated settings'
    }
    RunSetup 'custom-installer-upgrade'
    if ($testApp) { Check ($testApp.WaitForExit(10000)) 'Inno Restart Manager closes the old running tray app' }
    Check ((Get-Item (Join-Path $legacy 'MawaqitAdhan.exe')).VersionInfo.FileVersion -eq '1.0.0.3') 'beta 2 is upgraded in place to beta 3'
    Check (!(Test-Path $legacyKey)) 'obsolete custom uninstall registration is removed'
    Check (!(Test-Path (Join-Path $legacy 'Uninstall.exe')) -and !(Test-Path (Join-Path $legacy 'installation.json'))) 'custom installer files are replaced by Inno uninstall files'
    Check ((Get-ItemProperty $runKey).$runName -eq ('"' + (Join-Path $legacy 'MawaqitAdhan.exe') + '" --tray')) 'enabled startup keeps the upgraded target'
    Uninstall $legacy
    Check (Test-Path (Join-Path $legacy 'custom-voice.mp3')) 'uninstall preserves custom files'
    Check (!(Get-ItemProperty $runKey).PSObject.Properties[$runName]) 'uninstall removes only its own startup value'
    # Test portable discovery with no uninstall registration at all.
    Copy-Item -LiteralPath (Join-Path $root 'dist/MawaqitAdhan-1.0.0-beta.2/MawaqitAdhan.exe') -Destination $legacy
    Set-ItemProperty $runKey -Name $runName -Value ('"' + (Join-Path $legacy 'MawaqitAdhan.exe') + '" --tray')
    RunSetup 'portable-upgrade'
    Check ((Get-ItemProperty $uninstallKey).InstallLocation.TrimEnd('\') -eq $legacy) 'portable startup path is detected without installer registration'
    Uninstall $legacy
    Write-Output "ALL INNO INSTALLER CHECKS PASSED. Logs: $testRoot"
}
finally {
    if ($testApp -and !$testApp.HasExited) { $testApp.Kill() }
    # Do not remove test files/registrations on failure: retain them for diagnosis.
}
