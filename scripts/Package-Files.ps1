# Explicit runtime allowlist. Never collect an entire build, web or workspace folder.
$RuntimePackageFiles = @(
    'CAD-URDF Maker.dll', 'CadUrdfMaker.Core.dll',
    'Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.WinForms.dll', 'WebView2Loader.dll',
    'SolidWorks.Interop.sldworks.dll', 'SolidWorks.Interop.swconst.dll', 'SolidWorks.Interop.swpublished.dll',
    'Install.exe', 'Uninstall.exe', 'Install.ps1', 'Uninstall.ps1',
    'README.md', 'KEY-FUNCTIONS.md', 'WORKFLOWS.md', 'INERTIA-VALIDATION.md', 'INSTALLATION.md', 'PROJECT-FORMAT.md', 'THIRD-PARTY-NOTICES.md',
    'web/index.html', 'web/app.js', 'web/math.js', 'web/types.js', 'web/style.css', 'web/icon.bmp',
    'web/vendor/three.module.js', 'web/vendor/OrbitControls.js', 'web/vendor/THREE-LICENSE.txt',
    'licenses/WEBVIEW2-LICENSE.txt'
)

function Write-RuntimePackageManifest {
    param([string]$Directory, [string]$Version)
    $files = foreach ($relative in $RuntimePackageFiles) {
        $file = Join-Path $Directory $relative
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing release file: $relative" }
        [ordered]@{ path = $relative; sha256 = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() }
    }
    $manifest = [ordered]@{ version = $Version; files = @($files) } | ConvertTo-Json -Depth 4
    [IO.File]::WriteAllText((Join-Path $Directory 'package-manifest.json'), $manifest, [Text.UTF8Encoding]::new($false))
}
