$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$rootDir = Split-Path -Parent $PSScriptRoot
$vendorDir = Join-Path $rootDir 'vendor'
$viewerDir = Join-Path $rootDir 'web\vendor'
New-Item -ItemType Directory -Force -Path $vendorDir,$viewerDir,(Join-Path $vendorDir 'typescript') | Out-Null
Invoke-WebRequest -UseBasicParsing -Uri 'https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/1.0.2903.40/microsoft.web.webview2.1.0.2903.40.nupkg' -OutFile (Join-Path $vendorDir 'webview2.zip')
Expand-Archive -LiteralPath (Join-Path $vendorDir 'webview2.zip') -DestinationPath (Join-Path $vendorDir 'webview2') -Force
Invoke-WebRequest -UseBasicParsing -Uri 'https://registry.npmjs.org/typescript/-/typescript-5.7.2.tgz' -OutFile (Join-Path $vendorDir 'typescript.tgz')
& tar -xf (Join-Path $vendorDir 'typescript.tgz') -C (Join-Path $vendorDir 'typescript')
if ($LASTEXITCODE -ne 0) { throw 'TypeScript extraction failed.' }
Invoke-WebRequest -UseBasicParsing -Uri 'https://cdn.jsdelivr.net/npm/three@0.170.0/build/three.module.js' -OutFile (Join-Path $viewerDir 'three.module.js')
Invoke-WebRequest -UseBasicParsing -Uri 'https://cdn.jsdelivr.net/npm/three@0.170.0/examples/jsm/controls/OrbitControls.js' -OutFile (Join-Path $viewerDir 'OrbitControls.js')
$orbitPath = Join-Path $viewerDir 'OrbitControls.js'
[IO.File]::WriteAllText($orbitPath,[IO.File]::ReadAllText($orbitPath).Replace("from 'three'","from './three.module.js'"))
Invoke-WebRequest -UseBasicParsing -Uri 'https://cdn.jsdelivr.net/npm/three@0.170.0/LICENSE' -OutFile (Join-Path $viewerDir 'THREE-LICENSE.txt')
Write-Output 'Downloaded pinned WebView2 1.0.2903.40, TypeScript 5.7.2, and Three.js 0.170.0.'
