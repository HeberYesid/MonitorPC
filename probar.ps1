param([string]$Directorio = 'bin')
$ErrorActionPreference = 'Stop'
$exe = Join-Path (Join-Path $PSScriptRoot $Directorio) 'MonitorPC.exe'
if (-not (Test-Path $exe)) { throw 'Ejecute compilar.ps1 primero.' }
$temp = Join-Path $env:LOCALAPPDATA 'Temp\opencode'
if (-not (Test-Path $temp)) { $temp = [IO.Path]::GetTempPath() }
$log = Join-Path $temp ('MonitorPC-' + [guid]::NewGuid() + '.txt')
$p = Start-Process -FilePath $exe -ArgumentList "--autoprueba `"$log`"" -PassThru
try {
    Write-Output "MonitorPC PID $($p.Id) | $($p.StartTime.ToString('O')) | --autoprueba"
    if (-not $p.WaitForExit(40000)) {
        if ($p.CloseMainWindow()) { $null = $p.WaitForExit(5000) }
        if (-not $p.HasExited -and $p.Path -eq $exe) { taskkill /PID $p.Id /T /F; $null = $p.WaitForExit(5000) }
        throw 'Tiempo límite de autoprueba agotado.'
    }
    Get-Content -Encoding utf8 $log
    if ($p.ExitCode -ne 0) { throw "Autoprueba fallida (código $($p.ExitCode))." }
} finally {
    Write-Output "Proceso de prueba cerrado: $($p.HasExited) | Informe: $log"
    $p.Dispose()
}
