param([string]$ReleaseVersion = '1.3.4')
$ErrorActionPreference = 'Stop'
$rootDir = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'Package-Files.ps1')
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = Join-Path $rootDir "dist\CAD-URDF Maker-$ReleaseVersion.zip"
$setup = Join-Path $rootDir "dist\CAD-URDF Maker-$ReleaseVersion-Setup.exe"
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    $actual = @($zip.Entries | ForEach-Object FullName | Sort-Object)
    $expected = @(@($RuntimePackageFiles) + @('package-manifest.json') | Sort-Object)
    $difference = Compare-Object $expected $actual
    if ($difference) { throw "Release archive differs from runtime allowlist: $($difference | Out-String)" }
} finally { $zip.Dispose() }
$testDir = Join-Path $rootDir ('build-package-tests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testDir | Out-Null
# The framework loader resolves referenced assemblies by name beside the test EXE.
Copy-Item -LiteralPath $setup -Destination (Join-Path $testDir 'Setup.exe')
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$references = @(('/reference:' + (Join-Path $testDir 'Setup.exe')), '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', '/reference:System.IO.Compression.dll', '/reference:System.IO.Compression.FileSystem.dll', '/reference:System.Core.dll')
& $compiler /nologo /target:exe /platform:x64 ("/out:" + (Join-Path $testDir 'PackageTests.exe')) $references (Join-Path $rootDir 'tests\PackageTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Package test compilation failed.' }
& (Join-Path $testDir 'PackageTests.exe') $archive $testDir $ReleaseVersion
if ($LASTEXITCODE -ne 0) { throw 'Package regression tests failed.' }
foreach ($executable in @($setup, (Join-Path $testDir 'fresh\Install.exe'), (Join-Path $testDir 'fresh\Uninstall.exe'))) {
    # Restore the intentionally corrupted test extraction before checking its frontend.
    if ($executable -ne $setup -and -not (Test-Path -LiteralPath (Join-Path $testDir 'frontend-restored'))) {
        $zip = [IO.Compression.ZipFile]::OpenRead($archive)
        try { [IO.Compression.ZipFileExtensions]::ExtractToFile($zip.GetEntry('web/app.js'), (Join-Path $testDir 'fresh\web\app.js'), $true) }
        finally { $zip.Dispose() }
        New-Item -ItemType File -Path (Join-Path $testDir 'frontend-restored') | Out-Null
    }
    $check = Start-Process -FilePath $executable -ArgumentList '--verify-package' -Wait -PassThru -WindowStyle Hidden
    if ($check.ExitCode -ne 0) { throw "Frontend package verification failed: $executable" }
}
Write-Output 'PASS standalone, portable install and portable unregister verification entrypoints'
Write-Output "Installer previews and extraction scratch files: $testDir"
