param([int]$Port=8765)
$ErrorActionPreference='Stop'
$rootDir=Split-Path -Parent $PSScriptRoot
& python (Join-Path $rootDir 'tests\make-fixture.py')
if($LASTEXITCODE -ne 0){throw 'Synthetic fixture generation failed.'}
$source=[IO.File]::ReadAllText((Join-Path $rootDir 'web\index.html'))
$source=$source.Replace('<head>','<head><base href="/web/">').Replace("base-uri 'none'","base-uri 'self'")
$source=[regex]::Replace($source,'<script type="module" src="app\.js[^"]*"></script>','<script src="/tests/harness.js"></script>$0')
[IO.File]::WriteAllText((Join-Path $rootDir 'build\viewer-harness.html'),$source)
Write-Output "Synthetic editor preview: http://127.0.0.1:$Port/build/viewer-harness.html"
& python -m http.server $Port --bind 127.0.0.1 --directory $rootDir
