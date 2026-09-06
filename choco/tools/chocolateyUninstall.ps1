$ErrorActionPreference = 'Stop'

$installDir = Join-Path $env:ProgramFiles 'Killendar'
$installExe = Join-Path $installDir 'Killendar.exe'

if (Test-Path $installExe) {
    Start-Process -FilePath $installExe -ArgumentList '/uninstall' -Wait -NoNewWindow
} elseif (Test-Path $installDir) {
    Remove-Item $installDir -Recurse -Force
}

$startMenuPath = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\Killendar'
if (Test-Path $startMenuPath) { Remove-Item $startMenuPath -Recurse -Force }
