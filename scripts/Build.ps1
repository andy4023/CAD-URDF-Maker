param([string]$SolidWorksRoot = 'C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS',[string]$OutputDirectory='build')
$ErrorActionPreference = 'Stop'
$rootDir = Split-Path -Parent $PSScriptRoot
$buildDir = Join-Path $rootDir $OutputDirectory
$buildDir = [IO.Path]::GetFullPath($buildDir)
$workspacePrefix = [IO.Path]::GetFullPath($rootDir).TrimEnd('\') + '\'
if (-not $buildDir.StartsWith($workspacePrefix, [StringComparison]::OrdinalIgnoreCase) -or
    $buildDir.Substring($workspacePrefix.Length).Split('\') -contains 'testcases') {
    throw 'Build output must be a workspace subfolder outside private testcases.'
}
$frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $frameworkDir 'csc.exe'
$swRedist = Join-Path $SolidWorksRoot 'api\redist'
$webviewDir = Join-Path $rootDir 'vendor\webview2\lib\net462'
foreach ($required in @($compiler,(Join-Path $swRedist 'SolidWorks.Interop.sldworks.dll'),(Join-Path $webviewDir 'Microsoft.Web.WebView2.WinForms.dll'))) {
    if (-not (Test-Path -LiteralPath $required)) { throw "Missing prerequisite: $required. See README.md and scripts/Get-Dependencies.ps1." }
}
New-Item -ItemType Directory -Force -Path $buildDir | Out-Null
$keyDir=Join-Path $rootDir 'keys'
$keyPath=Join-Path $keyDir 'CadUrdfMaker.snk'
if(-not(Test-Path -LiteralPath $keyPath)){
    New-Item -ItemType Directory -Force -Path $keyDir | Out-Null
    $keyParameters=New-Object Security.Cryptography.CspParameters
    $keyParameters.KeyNumber=2
    $assemblyKey=New-Object Security.Cryptography.RSACryptoServiceProvider(2048,$keyParameters)
    $assemblyKey.PersistKeyInCsp=$false
    try{[IO.File]::WriteAllBytes($keyPath,$assemblyKey.ExportCspBlob($true))}finally{$assemblyKey.Dispose()}
}
$signingArgument='/keyfile:'+$keyPath
& python (Join-Path $rootDir 'scripts\make-icon.py')
if ($LASTEXITCODE -ne 0) { throw 'Icon generation failed.' }
New-Item -ItemType Directory -Force -Path (Join-Path $buildDir 'licenses') | Out-Null
Copy-Item -LiteralPath (Join-Path $rootDir 'vendor\webview2\LICENSE.txt') -Destination (Join-Path $buildDir 'licenses\WEBVIEW2-LICENSE.txt') -Force
$coreSources = @(Get-ChildItem -LiteralPath (Join-Path $rootDir 'src\Core') -Filter '*.cs' | ForEach-Object FullName)
& $compiler /nologo /target:library /platform:x64 /optimize+ $signingArgument ("/out:" + (Join-Path $buildDir 'CadUrdfMaker.Core.dll')) /reference:System.Web.Extensions.dll /reference:System.Xml.dll $coreSources
if ($LASTEXITCODE -ne 0) { throw 'Core compilation failed.' }
$swSources = @(Get-ChildItem -LiteralPath (Join-Path $rootDir 'src\SolidWorks') -Filter '*.cs' | Where-Object Name -ne 'Launcher.cs' | ForEach-Object FullName)
$references = @(
    (Join-Path $buildDir 'CadUrdfMaker.Core.dll'),
    (Join-Path $swRedist 'SolidWorks.Interop.sldworks.dll'),
    (Join-Path $swRedist 'SolidWorks.Interop.swconst.dll'),
    (Join-Path $swRedist 'SolidWorks.Interop.swpublished.dll'),
    (Join-Path $webviewDir 'Microsoft.Web.WebView2.Core.dll'),
    (Join-Path $webviewDir 'Microsoft.Web.WebView2.WinForms.dll'),
    'System.Windows.Forms.dll','System.Drawing.dll','System.Web.Extensions.dll','System.Xml.dll'
) | ForEach-Object { "/reference:$_" }
& $compiler /nologo /target:library /platform:x64 /optimize+ $signingArgument ("/out:" + (Join-Path $buildDir 'CAD-URDF Maker.dll')) $references $swSources
if ($LASTEXITCODE -ne 0) { throw 'SolidWorks add-in compilation failed.' }
$launcherReferences = $references + @('/reference:' + (Join-Path $buildDir 'CAD-URDF Maker.dll'))
& $compiler /nologo /target:exe /platform:x64 /optimize+ ("/out:" + (Join-Path $buildDir 'CAD-URDF Maker Launcher.exe')) $launcherReferences (Join-Path $rootDir 'src\SolidWorks\Launcher.cs')
if ($LASTEXITCODE -ne 0) { throw 'Launcher compilation failed.' }
foreach ($dll in @('SolidWorks.Interop.sldworks.dll','SolidWorks.Interop.swconst.dll','SolidWorks.Interop.swpublished.dll')) { Copy-Item -LiteralPath (Join-Path $swRedist $dll) -Destination $buildDir -Force }
foreach ($dll in @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll')) { Copy-Item -LiteralPath (Join-Path $webviewDir $dll) -Destination $buildDir -Force }
Copy-Item -LiteralPath (Join-Path $rootDir 'vendor\webview2\runtimes\win-x64\native\WebView2Loader.dll') -Destination $buildDir -Force
if (Test-Path -LiteralPath (Join-Path $rootDir 'vendor\typescript\package\lib\tsc.js')) {
    & node (Join-Path $rootDir 'vendor\typescript\package\lib\tsc.js') -p (Join-Path $rootDir 'web\tsconfig.json')
    if ($LASTEXITCODE -ne 0) { throw 'Editor TypeScript compilation failed.' }
} elseif (Test-Path -LiteralPath (Join-Path $rootDir 'web\app.js')) {
    Write-Warning 'Using the already compiled editor. Download TypeScript to rebuild it from source.'
} else { throw 'TypeScript is missing. Run scripts/Get-Dependencies.ps1.' }
# Version the editor entry points and math module so a previously loaded
# WebView2 session cannot mix new controls with an older cached stylesheet/module.
$assetVersion=[Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $buildDir 'CAD-URDF Maker.dll')).FileVersion
$editorPath=Join-Path $rootDir 'web\app.js'
$editorText=[regex]::Replace([IO.File]::ReadAllText($editorPath),"from '\./math\.js(?:\?v=[^']*)?'",("from './math.js?v="+$assetVersion+"'"))
[IO.File]::WriteAllText($editorPath,$editorText,[Text.UTF8Encoding]::new($false))
$indexPath=Join-Path $rootDir 'web\index.html'
$indexText=[regex]::Replace([IO.File]::ReadAllText($indexPath),'href="style\.css(?:\?v=[^"]*)?"',('href="style.css?v='+$assetVersion+'"'))
$indexText=[regex]::Replace($indexText,'src="app\.js(?:\?v=[^"]*)?"',('src="app.js?v='+$assetVersion+'"'))
[IO.File]::WriteAllText($indexPath,$indexText,[Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath (Join-Path $rootDir 'web') -Destination $buildDir -Recurse -Force
# Keep the repository and installed workflows in sync; package docs live at its root.
$packageReadme = [IO.File]::ReadAllText((Join-Path $rootDir 'docs\PACKAGE-README.md'))
[IO.File]::WriteAllText((Join-Path $buildDir 'README.md'), $packageReadme, [Text.UTF8Encoding]::new($false))
$functionGuide = [IO.File]::ReadAllText((Join-Path $rootDir 'docs\KEY-FUNCTIONS.md')).Replace('(../README.md)', '(README.md)')
[IO.File]::WriteAllText((Join-Path $buildDir 'KEY-FUNCTIONS.md'), $functionGuide, [Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath (Join-Path $rootDir 'THIRD-PARTY-NOTICES.md') -Destination $buildDir -Force -ErrorAction SilentlyContinue
Copy-Item -LiteralPath (Join-Path $rootDir 'docs\WORKFLOWS.md') -Destination $buildDir -Force
Copy-Item -LiteralPath (Join-Path $rootDir 'docs\INSTALLATION.md') -Destination $buildDir -Force
Copy-Item -LiteralPath (Join-Path $rootDir 'docs\VALIDATION.md') -Destination $buildDir -Force
Copy-Item -LiteralPath (Join-Path $rootDir 'docs\PROJECT-FORMAT.md') -Destination $buildDir -Force
Copy-Item -LiteralPath (Join-Path $rootDir 'docs\INERTIA-VALIDATION.md') -Destination $buildDir -Force
Copy-Item -LiteralPath (Join-Path $rootDir 'scripts\Install.ps1') -Destination $buildDir -Force
Copy-Item -LiteralPath (Join-Path $rootDir 'scripts\Uninstall.ps1') -Destination $buildDir -Force
$installerReferences = @('/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Web.Extensions.dll','/reference:System.IO.Compression.dll','/reference:System.IO.Compression.FileSystem.dll','/reference:System.Core.dll')
& $compiler /nologo /target:winexe /platform:x64 /optimize+ ("/out:" + (Join-Path $buildDir 'Install.exe')) ("/win32manifest:" + (Join-Path $rootDir 'src\Installer\Installer.manifest')) $installerReferences (Join-Path $rootDir 'src\Installer\Installer.cs')
if ($LASTEXITCODE -ne 0) { throw 'Graphical installer compilation failed.' }
Copy-Item -LiteralPath (Join-Path $buildDir 'Install.exe') -Destination (Join-Path $buildDir 'Uninstall.exe') -Force
. (Join-Path $PSScriptRoot 'Package-Files.ps1')
$runtimeVersion = [version]$assetVersion
$packageVersion = '{0}.{1}.{2}' -f $runtimeVersion.Major, $runtimeVersion.Minor, $runtimeVersion.Build
Write-RuntimePackageManifest -Directory $buildDir -Version $packageVersion
Write-Output "Built CAD-URDF Maker: $buildDir"
