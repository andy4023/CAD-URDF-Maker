param([string]$OutputDirectory='build')
$ErrorActionPreference = 'Stop'
$rootDir = Split-Path -Parent $PSScriptRoot
$buildDir = Join-Path $rootDir $OutputDirectory
& (Join-Path $PSScriptRoot 'Build.ps1') -OutputDirectory $OutputDirectory
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:exe /platform:x64 ("/out:" + (Join-Path $buildDir 'CoreTests.exe')) ("/reference:" + (Join-Path $buildDir 'CadUrdfMaker.Core.dll')) /reference:System.Web.Extensions.dll /reference:System.Xml.dll (Join-Path $rootDir 'tests\CoreTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
& (Join-Path $buildDir 'CoreTests.exe') $buildDir
if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
& $compiler /nologo /target:exe /platform:x64 ("/out:" + (Join-Path $buildDir 'OriginTests.exe')) ("/reference:" + (Join-Path $buildDir 'CadUrdfMaker.Core.dll')) /reference:System.Web.Extensions.dll /reference:System.Xml.dll (Join-Path $rootDir 'tests\OriginTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Origin test compilation failed.' }
& (Join-Path $buildDir 'OriginTests.exe') $buildDir
if ($LASTEXITCODE -ne 0) { throw 'Origin workflow tests failed.' }
& $compiler /nologo /target:exe /platform:x64 ("/out:" + (Join-Path $buildDir 'InertiaTests.exe')) ("/reference:" + (Join-Path $buildDir 'CadUrdfMaker.Core.dll')) /reference:System.Web.Extensions.dll (Join-Path $rootDir 'tests\InertiaTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Inertia test compilation failed.' }
& (Join-Path $buildDir 'InertiaTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Native inertia regression tests failed.' }
& $compiler /nologo /target:exe /platform:x64 ("/out:" + (Join-Path $buildDir 'ComStartupTests.exe')) ("/reference:" + (Join-Path $buildDir 'CAD-URDF Maker.dll')) ("/reference:" + (Join-Path $buildDir 'SolidWorks.Interop.swpublished.dll')) (Join-Path $rootDir 'tests\ComStartupTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'COM startup test compilation failed.' }
& (Join-Path $buildDir 'ComStartupTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'COM startup tests failed.' }
& node (Join-Path $rootDir 'tests\math-tests.mjs')
if ($LASTEXITCODE -ne 0) { throw 'Browser math tests failed.' }
