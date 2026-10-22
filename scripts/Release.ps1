param(
    [string]$OutputDirectory = 'build',
    [string]$SolidWorksRoot = 'C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS'
)
$ErrorActionPreference = 'Stop'
$rootDir = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'Package-Files.ps1')
$buildDir = [IO.Path]::GetFullPath((Join-Path $rootDir $OutputDirectory))
$workspacePrefix = [IO.Path]::GetFullPath($rootDir).TrimEnd('\') + '\'
if (-not $buildDir.StartsWith($workspacePrefix, [StringComparison]::OrdinalIgnoreCase) -or
    $buildDir.Substring($workspacePrefix.Length).Split('\') -contains 'testcases') {
    throw 'Release build output must be a workspace subfolder outside private testcases.'
}
& (Join-Path $PSScriptRoot 'Build.ps1') -OutputDirectory $OutputDirectory -SolidWorksRoot $SolidWorksRoot
$fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $buildDir 'CAD-URDF Maker.dll')).FileVersion
$versionParts = [version]$fileVersion
$version = '{0}.{1}.{2}' -f $versionParts.Major, $versionParts.Minor, $versionParts.Build
$distDir = Join-Path $rootDir 'dist'
New-Item -ItemType Directory -Force -Path $distDir | Out-Null
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$stageName = 'cad-urdf-release-' + [guid]::NewGuid().ToString('N')
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
$stage = Join-Path $tempRoot $stageName
try {
    New-Item -ItemType Directory -Path $stage | Out-Null
    foreach ($relative in $RuntimePackageFiles) {
        $source = Join-Path $buildDir $relative
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing release file: $relative" }
        $target = Join-Path $stage $relative
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
        Copy-Item -LiteralPath $source -Destination $target
    }
    Write-RuntimePackageManifest -Directory $stage -Version $version
    $stagedArchive = Join-Path $stage 'payload.zip'
    $stream = [IO.File]::Create($stagedArchive)
    $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($relative in @($RuntimePackageFiles) + @('package-manifest.json')) {
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $stage $relative), $relative, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $zip.Dispose(); $stream.Dispose() }
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    $stagedSetup = Join-Path $stage 'Setup.exe'
    $references = @('/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Web.Extensions.dll','/reference:System.IO.Compression.dll','/reference:System.IO.Compression.FileSystem.dll','/reference:System.Core.dll')
    & $compiler /nologo /target:winexe /platform:x64 /optimize+ ("/out:" + $stagedSetup) ("/win32manifest:" + (Join-Path $rootDir 'src\Installer\Installer.manifest')) ("/resource:" + $stagedArchive + ',payload.zip') $references (Join-Path $rootDir 'src\Installer\Installer.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Standalone setup compilation failed.' }
    $check = Start-Process -FilePath $stagedSetup -ArgumentList '--verify-package' -PassThru -Wait -WindowStyle Hidden
    if ($check.ExitCode -ne 0) { throw 'Standalone setup failed embedded package verification.' }
    # Publish only after the embedded payload has been verified without registration.
    $archive = Join-Path $distDir "CAD-URDF Maker-$version.zip"
    $setup = Join-Path $distDir "CAD-URDF Maker-$version-Setup.exe"
    Copy-Item -LiteralPath $stagedArchive -Destination $archive -Force
    Copy-Item -LiteralPath $stagedSetup -Destination $setup -Force
    $checksums = @($setup, $archive) | ForEach-Object { '{0}  {1}' -f (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant(), [IO.Path]::GetFileName($_) }
    [IO.File]::WriteAllLines((Join-Path $distDir "CAD-URDF Maker-$version-SHA256SUMS.txt"), [string[]]$checksums, [Text.UTF8Encoding]::new($false))
    Write-Output "Standalone installer: $setup"
    Write-Output "Portable / command-line package: $archive"
} finally {
    $resolvedStage = [IO.Path]::GetFullPath($stage)
    if ($resolvedStage.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetFileName($resolvedStage) -eq $stageName -and (Test-Path -LiteralPath $resolvedStage)) {
        Remove-Item -LiteralPath $resolvedStage -Recurse -Force
    }
}
