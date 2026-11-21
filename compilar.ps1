param([string]$Directorio = 'bin')
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $compiler)) { throw 'No se encontró el compilador de .NET Framework x64.' }
$output = Join-Path $PSScriptRoot $Directorio
New-Item -ItemType Directory -Force -Path $output | Out-Null
$sources = @(Get-ChildItem (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | Sort-Object Name | ForEach-Object FullName)
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /warn:4 /warnaserror+ /codepage:65001 `
    /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Core.dll `
    "/win32manifest:$PSScriptRoot\MonitorPC.manifest" "/out:$output\MonitorPC.exe" @sources
if ($LASTEXITCODE -ne 0) { throw 'La compilación de MonitorPC falló.' }
Write-Output (Join-Path $output 'MonitorPC.exe')
