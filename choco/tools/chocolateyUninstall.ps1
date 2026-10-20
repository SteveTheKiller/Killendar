$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'UninstallReceipt.ps1')
Invoke-KillendarUninstall -InstallDirectory (Join-Path $env:ProgramFiles 'Killendar')
