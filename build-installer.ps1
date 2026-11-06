# Builds dist\FigmaShell-Setup-<version>.exe
#   1. dotnet publish (Release, win-x64, framework-dependent, ReadyToRun)
#   2. Inno Setup compiles installer\FigmaShell.iss
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 not found. Install it with: winget install JRSoftware.InnoSetup' }

if (Test-Path publish\win-x64) { Remove-Item publish\win-x64 -Recurse -Force }
dotnet publish src/FigmaShell -p:PublishProfile=win-x64
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

& $iscc /Qp installer\FigmaShell.iss
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compile failed' }

Get-ChildItem dist\*.exe | Sort-Object LastWriteTime | Select-Object -Last 1 |
    ForEach-Object { "Installer: $($_.FullName)  ($([math]::Round($_.Length / 1MB, 2)) MB)" }
