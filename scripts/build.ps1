param([switch]$Test)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Windows .NET Framework 4.x compiler was not found.' }
$outputDir = Join-Path $projectRoot 'dist'
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
$sources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' -Recurse | ForEach-Object { $_.FullName })
$common = @('/nologo', '/optimize+', '/platform:x64', '/warn:4', '/codepage:65001', '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll')
$appOutput = Join-Path $outputDir 'BatteryMonitor.exe'
& $compiler @common '/target:winexe' '/main:BatteryMonitor.Program' "/win32manifest:$(Join-Path $projectRoot 'src\app.manifest')" "/out:$appOutput" @sources
if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed.' }
Write-Output "Built: $appOutput"
& $appOutput --export-icon (Join-Path $outputDir 'BatteryMonitor.ico')
if ($LASTEXITCODE -ne 0) { throw 'Icon export failed.' }
if ($Test) {
    $testOutput = Join-Path $projectRoot 'tests\TestRunner.exe'
    $testSources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'tests') -Filter '*.cs' -Recurse | ForEach-Object { $_.FullName })
    & $compiler @common '/target:exe' '/main:BatteryMonitor.Tests.TestRunner' "/out:$testOutput" @sources @testSources
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    & $testOutput
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
