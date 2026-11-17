$ErrorActionPreference = 'Stop'
$testRoot = Split-Path -Parent $PSScriptRoot
$runner = Join-Path $testRoot 'TestRunner.exe'
if (-not (Test-Path -LiteralPath $runner)) { throw 'Run scripts/build.ps1 -Test first.' }
$artifacts = Join-Path $testRoot 'artifacts'
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
& $runner --tray-smoke | Tee-Object -FilePath (Join-Path $artifacts 'ui-smoke.txt')
if ($LASTEXITCODE -ne 0) { throw 'Tray smoke test failed.' }
