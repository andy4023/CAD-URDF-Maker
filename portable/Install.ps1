param([switch]$StartWithSolidWorks)
$ErrorActionPreference = 'Stop'
$installDir = $PSScriptRoot
if (-not (Test-Path -LiteralPath (Join-Path $installDir 'CAD-URDF Maker.dll'))) { $installDir = Join-Path (Split-Path -Parent $PSScriptRoot) 'build' }
$dllPath = Join-Path $installDir 'CAD-URDF Maker.dll'
if (-not (Test-Path -LiteralPath $dllPath)) { throw 'Build the add-in first with scripts/Build.ps1.' }
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this script in an elevated 64-bit PowerShell. SolidWorks add-in discovery uses HKLM.' }
if (Get-Process SLDWORKS -ErrorAction SilentlyContinue) { throw 'Close SolidWorks before registering the add-in.' }
$regasm = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe'
& $regasm $dllPath /codebase
if ($LASTEXITCODE -ne 0) { throw 'COM registration failed.' }
$startupKey = 'HKCU:\Software\SolidWorks\AddInsStartup\{136E0D98-8518-4EBC-8BFE-6CA96F4850D8}'
New-Item -Path $startupKey -Force | Out-Null
Set-Item -LiteralPath $startupKey -Value ([int]$StartWithSolidWorks.IsPresent)
Write-Output 'CAD-URDF Maker registered. In SolidWorks, open Tools > Add-Ins and enable CAD-URDF Maker.'
