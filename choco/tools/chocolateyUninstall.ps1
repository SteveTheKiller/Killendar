$ErrorActionPreference = 'Stop'

$installDir = Join-Path $env:ProgramFiles 'Killendar'
$installExe = Join-Path $installDir 'Killendar.exe'

if (Test-Path $installExe) {
    $uninstaller = Start-Process -FilePath $installExe -ArgumentList '/uninstall-silent' -Wait -PassThru -WindowStyle Hidden
    if ($uninstaller.ExitCode -ne 0) { throw "Killendar uninstall failed with exit code $($uninstaller.ExitCode)." }
}
