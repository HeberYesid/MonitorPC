param(
    [string]$Directorio = 'bin',
    [switch]$SinEscritorio,
    [string]$Informe
)
$ErrorActionPreference = 'Stop'
$exe = Join-Path (Join-Path $PSScriptRoot $Directorio) 'MonitorPC.exe'
if (-not (Test-Path $exe)) { throw 'Ejecute compilar.ps1 primero.' }
if ($Informe) {
    $log = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Informe)
} else {
    $temp = Join-Path $env:LOCALAPPDATA 'Temp\opencode'
    if (-not (Test-Path $temp)) { $temp = [IO.Path]::GetTempPath() }
    $log = Join-Path $temp ('MonitorPC-' + [guid]::NewGuid() + '.txt')
}
$archivo = [IO.File]::Open($log, [IO.FileMode]::CreateNew)
$archivo.Dispose()
$modo = if ($SinEscritorio) { '--autoprueba-sin-escritorio' } else { '--autoprueba' }
$p = Start-Process -FilePath $exe -ArgumentList "$modo `"$log`"" -PassThru
try {
    Write-Output "MonitorPC PID $($p.Id) | $($p.StartTime.ToString('O')) | $modo"
    if (-not $p.WaitForExit(40000)) {
        if ($p.CloseMainWindow()) { $null = $p.WaitForExit(5000) }
        if (-not $p.HasExited -and $p.Path -eq $exe) { taskkill /PID $p.Id /T /F; $null = $p.WaitForExit(5000) }
        throw 'Tiempo límite de autoprueba agotado.'
    }
    Get-Content -Encoding utf8 -LiteralPath $log
    if ($p.ExitCode -ne 0) { throw "Autoprueba fallida (código $($p.ExitCode))." }
    if ((Get-Item -LiteralPath $log).Length -eq 0) { throw 'La autoprueba no produjo un informe.' }
} finally {
    Write-Output "Proceso de prueba cerrado: $($p.HasExited) | Informe: $log"
    $p.Dispose()
}
