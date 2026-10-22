$ErrorActionPreference = 'Stop'
$installDir = $PSScriptRoot
if (-not (Test-Path -LiteralPath (Join-Path $installDir 'CAD-URDF Maker.dll'))) { $installDir = Join-Path (Split-Path -Parent $PSScriptRoot) 'build' }
if (Get-Process SLDWORKS -ErrorAction SilentlyContinue) { throw 'Close SolidWorks before unregistering the add-in.' }
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this script in an elevated 64-bit PowerShell.' }
$regasm = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe'
& $regasm (Join-Path $installDir 'CAD-URDF Maker.dll') /unregister
if ($LASTEXITCODE -ne 0) { throw 'COM unregistration failed.' }
Write-Output 'CAD-URDF Maker unregistered. Saved projects and exports remain available.'
