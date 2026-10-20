function Invoke-KillendarUninstall {
    param(
        [Parameter(Mandatory = $true)][string]$InstallDirectory,
        [scriptblock]$StartUninstaller = {
            param($executable, $arguments)
            $start = New-Object System.Diagnostics.ProcessStartInfo
            $start.FileName = $executable
            $start.Arguments = $arguments
            $start.UseShellExecute = $false
            $start.CreateNoWindow = $true
            $start.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Hidden
            [System.Diagnostics.Process]::Start($start)
        },
        [ValidateRange(1, 300000)][int]$ReceiptTimeoutMilliseconds = 65000,
        [scriptblock]$UninstallEntryExists = {
            Test-Path -LiteralPath 'Registry::HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Uninstall\Killendar'
        }
    )
    $directory = [System.IO.Path]::GetFullPath($InstallDirectory).TrimEnd([char[]]@('\', '/'))
    if ($directory -eq [System.IO.Path]::GetPathRoot($directory).TrimEnd([char[]]@('\', '/'))) {
        throw 'The installation directory cannot be a volume root.'
    }
    $installExe = Join-Path $directory 'Killendar.exe'
    $absent = {
        param($path)
        try { $attributes = [System.IO.File]::GetAttributes($path); return $false }
        catch [System.IO.FileNotFoundException] { return $true }
        catch [System.IO.DirectoryNotFoundException] { return $true }
    }
    if (& $absent $installExe) {
        if (& $UninstallEntryExists) {
            throw 'The installed executable is missing but the uninstall entry remains. Reinstall the original signed build, then retry uninstall.'
        }
        return
    }
    $operationId = [guid]::NewGuid().ToString('N')
    $receiptPath = Join-Path (Join-Path $directory ('Uninstall-' + $operationId)) 'status.json'
    $elapsed = [System.Diagnostics.Stopwatch]::StartNew()
    $uninstaller = $null
    try {
        $uninstaller = & $StartUninstaller $installExe "/uninstall-silent $operationId"
        if ($null -eq $uninstaller) { throw "The uninstall process did not start. Receipt: $receiptPath" }
        $remaining = $ReceiptTimeoutMilliseconds - [int]$elapsed.ElapsedMilliseconds
        if ($remaining -le 0 -or -not $uninstaller.WaitForExit($remaining)) {
            throw "The uninstall process did not finish before the deadline. Receipt: $receiptPath"
        }
        $code = [int]$uninstaller.ExitCode
        if ($code -ne 0 -and $code -ne 997) { throw "Killendar uninstall returned $code. Receipt: $receiptPath" }
        while ($true) {
            $stream = $null
            $reader = $null
            $receipt = $null
            try {
                $stream = [System.IO.File]::Open($receiptPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read,
                    [System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete)
                $reader = New-Object System.IO.StreamReader($stream)
                $receipt = $reader.ReadToEnd() | ConvertFrom-Json -ErrorAction Stop
            }
            catch [System.IO.FileNotFoundException] { }
            catch [System.IO.DirectoryNotFoundException] { }
            finally {
                if ($null -ne $reader) { $reader.Dispose() }
                elseif ($null -ne $stream) { $stream.Dispose() }
            }
            if ($null -ne $receipt) {
                foreach ($field in @('OperationId', 'Machine', 'TargetDirectory', 'State', 'ExitCode', 'MetadataRemoved', 'RemainingFiles')) {
                    if ($receipt.PSObject.Properties.Name -notcontains $field) { throw "Invalid uninstall receipt field $field. Receipt: $receiptPath" }
                }
                if ($receipt.OperationId -cne $operationId -or $receipt.Machine -isnot [bool] -or -not $receipt.Machine -or
                    $receipt.MetadataRemoved -isnot [bool] -or $receipt.RemainingFiles -isnot [array] -or
                    ($receipt.ExitCode -isnot [int] -and $receipt.ExitCode -isnot [long]) -or
                    -not [System.IO.Path]::IsPathRooted([string]$receipt.TargetDirectory) -or
                    -not [string]::Equals([System.IO.Path]::GetFullPath([string]$receipt.TargetDirectory), $directory,
                        [System.StringComparison]::OrdinalIgnoreCase)) {
                    throw "The uninstall receipt does not match this request. Receipt: $receiptPath"
                }
                if ($receipt.State -ceq 'Completed') {
                    if ($receipt.ExitCode -ne 0 -or -not $receipt.MetadataRemoved -or $receipt.RemainingFiles.Count -ne 0 -or
                        -not (& $absent $installExe)) { throw "Uninstall completion was not verified. Receipt: $receiptPath" }
                    return
                }
                if ($receipt.State -ceq 'Failed') { throw "Deferred uninstall failed with $($receipt.ExitCode): $($receipt.Error). Receipt: $receiptPath" }
                if ($receipt.State -cne 'Pending' -or $receipt.ExitCode -ne 997) { throw "Invalid uninstall state. Receipt: $receiptPath" }
            }
            $remaining = $ReceiptTimeoutMilliseconds - [int]$elapsed.ElapsedMilliseconds
            if ($remaining -le 0) { throw "Uninstall completion is unconfirmed at the deadline. Receipt: $receiptPath" }
            [System.Threading.Thread]::Sleep([Math]::Min(100, $remaining))
        }
    }
    finally {
        if ($uninstaller -is [System.IDisposable]) { $uninstaller.Dispose() }
    }
}
