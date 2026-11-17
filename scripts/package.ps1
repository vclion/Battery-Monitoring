param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'build.ps1') -Test }
$dist = Join-Path $projectRoot 'dist'
$files = @(
    (Join-Path $dist 'BatteryMonitor.exe'),
    (Join-Path $dist 'BatteryMonitor.ico'),
    (Join-Path $projectRoot 'README.md'),
    (Join-Path $projectRoot 'LICENSE'),
    (Join-Path $projectRoot 'THIRD_PARTY_NOTICES.md'),
    (Join-Path $projectRoot 'CONTRIBUTING.md'),
    (Join-Path $projectRoot 'docs')
)
foreach ($file in $files) {
    if (-not (Test-Path -LiteralPath $file)) { throw "Missing package input: $file" }
}
$archive = Join-Path $dist 'BatteryMonitor-windows-x64.zip'
Compress-Archive -LiteralPath $files -DestinationPath $archive -Force
Write-Output "Packaged: $archive"

# Assemble an explicit source-only archive; do not traverse tests/artifacts or dist.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$portableZip = [IO.Compression.ZipFile]::Open($archive, [IO.Compression.ZipArchiveMode]::Update)
try {
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($portableZip,
        (Join-Path $projectRoot 'tests\README.md'), 'tests/README.md') | Out-Null
}
finally { $portableZip.Dispose() }
$sourceFiles = @()
foreach ($directory in @('src', 'scripts', 'docs', '.github')) {
    $sourceFiles += @(Get-ChildItem -LiteralPath (Join-Path $projectRoot $directory) -File -Recurse -Force |
        Where-Object { $_.Extension -in @('.cs', '.manifest', '.ps1', '.md', '.yml', '.yaml') })
}
$sourceFiles += @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'tests') -Filter '*.cs' -File)
$sourceFiles += @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'tests\integration') -Filter '*.ps1' -File)
foreach ($name in @('README.md', 'LICENSE', 'THIRD_PARTY_NOTICES.md', 'CONTRIBUTING.md', '.gitignore', '.gitattributes', 'tests\README.md')) {
    $sourceFiles += Get-Item -LiteralPath (Join-Path $projectRoot $name) -Force
}
$sourceArchive = Join-Path $dist 'BatteryMonitor-source.zip'
$stream = [IO.File]::Open($sourceArchive, [IO.FileMode]::Create)
$zip = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in $sourceFiles) {
        $relativePath = $file.FullName.Substring($projectRoot.Length).TrimStart('\', '/').Replace('\', '/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $relativePath) | Out-Null
    }
}
finally { $zip.Dispose(); $stream.Dispose() }
Write-Output "Source archive: $sourceArchive"
