param([switch]$NoLaunch, [switch]$EnableStartup)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$sourceExe = (Get-Item -LiteralPath (Join-Path $projectRoot 'dist\BatteryMonitor.exe')).FullName
$installDir = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'BatteryMonitor'
$installExe = Join-Path $installDir 'BatteryMonitor.exe'
$iconSource = Join-Path $projectRoot 'dist\BatteryMonitor.ico'
if (-not (Test-Path -LiteralPath $iconSource)) { throw 'Run scripts/build.ps1 before installation to create the icon.' }
$desktop = [Environment]::GetFolderPath('Desktop')
$shortcutName = (-join ([char[]](0x7535,0x91CF,0x76D1,0x63A7))) + '.lnk'
$shortcutPath = Join-Path $desktop $shortcutName
$shell = New-Object -ComObject WScript.Shell
$previousTarget = $null
if (Test-Path -LiteralPath $shortcutPath) {
    $oldShortcut = $shell.CreateShortcut($shortcutPath)
    $previousTarget = $oldShortcut.TargetPath
    if ($previousTarget -ne $installExe) {
        # Packaged Windows hosts can resolve LocalAppData to a physical cache path.
        # Accept only an existing copy of this exact installed binary.
        $sameInstalledCopy = (Test-Path -LiteralPath $previousTarget) -and (Test-Path -LiteralPath $installExe) -and
            ([IO.Path]::GetFileName($previousTarget) -eq 'BatteryMonitor.exe') -and
            ((Get-FileHash -LiteralPath $previousTarget).Hash -eq (Get-FileHash -LiteralPath $installExe).Hash)
        if (-not $sameInstalledCopy) { throw 'A different shortcut already uses the requested desktop name.' }
    }
}
# Only stop this project's old or installed executable before replacing it.
Get-Process -Name BatteryMonitor -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -eq $sourceExe -or $_.Path -eq $installExe -or ($previousTarget -and $_.Path -eq $previousTarget) } | Stop-Process
New-Item -ItemType Directory -Force -Path $installDir | Out-Null
Copy-Item -LiteralPath $sourceExe -Destination $installExe -Force
if ($previousTarget -and $previousTarget -ne $installExe) {
    Copy-Item -LiteralPath $sourceExe -Destination $previousTarget -Force
}
$installedIcon = Join-Path $installDir 'BatteryMonitor-mono.ico'
Copy-Item -LiteralPath $iconSource -Destination $installedIcon -Force
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $installExe
$shortcut.WorkingDirectory = $installDir
$shortcut.IconLocation = $installedIcon + ',0'
$shortcut.Description = 'Battery Monitor - click the tray icon to query battery levels.'
$shortcut.Save()
$runPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if ($EnableStartup) {
    if (-not (Test-Path -LiteralPath $runPath)) { New-Item -Path $runPath -Force | Out-Null }
    New-ItemProperty -LiteralPath $runPath -Name 'BatteryMonitorDualRing' -PropertyType String -Value ('"' + $installExe + '"') -Force | Out-Null
}
if (-not $NoLaunch) {
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = $installExe
    $info.WorkingDirectory = $installDir
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Hidden
    [System.Diagnostics.Process]::Start($info) | Out-Null
}
Write-Output "Installed: $installExe"
Write-Output "Desktop shortcut: $shortcutPath"
if ($EnableStartup) { Write-Output 'Current-user startup: enabled' }
else { Write-Output 'Current-user startup: preserved (use -EnableStartup to enable)' }
