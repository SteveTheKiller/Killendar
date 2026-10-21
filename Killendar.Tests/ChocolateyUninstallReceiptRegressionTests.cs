using System;
using System.Diagnostics;
using System.IO;
using Xunit;

namespace Killendar.Tests
{
    public sealed class ChocolateyUninstallReceiptRegressionTests
    {
        [Fact]
        public void PendingUninstallIsAcceptedOnlyAfterMatchingCompletionAndImageRemoval()
            => Exercise("complete", true);

        [Theory]
        [InlineData("pending")]
        [InlineData("failed")]
        [InlineData("wrong-id")]
        [InlineData("wrong-scope")]
        [InlineData("wrong-target")]
        [InlineData("missing-metadata")]
        [InlineData("remaining-file")]
        [InlineData("image-present")]
        [InlineData("missing-receipt")]
        [InlineData("null-process")]
        [InlineData("wait-timeout")]
        [InlineData("cancelled")]
        public void IncompleteOrMismatchedRequestsFailThePackageOperation(string scenario)
            => Exercise(scenario, false);

        private static void Exercise(string scenario, bool success)
        {
            string root = Path.Combine(TestScratch.Root + @"\uninstall-package", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string repository = FindRepository();
                string helper = Path.Combine(repository, "choco", "tools", "UninstallReceipt.ps1");
                string script = Path.Combine(root, "fixture.ps1");
                File.WriteAllText(script, "$ErrorActionPreference = 'Stop'\r\n. '" + helper.Replace("'", "''") + "'\r\n"
                    + "$root = '" + root.Replace("'", "''") + "'\r\n"
                    + "$scenario = '" + scenario + "'\r\n"
                    + "Set-Content -LiteralPath (Join-Path $root 'Killendar.exe') -Value 'inert image'\r\n"
                    + "Invoke-KillendarUninstall -InstallDirectory $root -ReceiptTimeoutMilliseconds "
                    + (scenario == "pending" || scenario == "missing-receipt" ? "500" : "3000")
                    + " -UninstallEntryExists { throw 'The fixture must not read the registry.' } -StartUninstaller {\r\n"
                    + " param($executable, $arguments)\r\n"
                    + " if ($scenario -eq 'null-process') { return $null }\r\n"
                    + " $operationId = $arguments.Split(' ')[1]\r\n"
                    + " [void][guid]::ParseExact($operationId, 'N')\r\n"
                    + " $directory = Join-Path $root ('Uninstall-' + $operationId)\r\n"
                    + " [void][System.IO.Directory]::CreateDirectory($directory)\r\n"
                    + " if ($scenario -ne 'image-present') { Remove-Item -LiteralPath $executable }\r\n"
                    + " $receipt = @{ State='Completed'; ExitCode=0; OperationId=$operationId; Machine=$true; TargetDirectory=$root; MetadataRemoved=$true; RemainingFiles=@() }\r\n"
                    + " switch ($scenario) {\r\n"
                    + " 'pending' { $receipt.State='Pending'; $receipt.ExitCode=997; $receipt.MetadataRemoved=$false }\r\n"
                    + " 'failed' { $receipt.State='Failed'; $receipt.ExitCode=5; $receipt.MetadataRemoved=$false }\r\n"
                    + " 'wrong-id' { $receipt.OperationId=[guid]::NewGuid().ToString('N') }\r\n"
                    + " 'wrong-scope' { $receipt.Machine=$false }\r\n"
                    + " 'wrong-target' { $receipt.TargetDirectory=Join-Path $root 'other' }\r\n"
                    + " 'missing-metadata' { $receipt.Remove('MetadataRemoved') }\r\n"
                    + " 'remaining-file' { $receipt.RemainingFiles=@($executable) }\r\n"
                    + " }\r\n"
                    + " if ($scenario -ne 'missing-receipt') { $receipt | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $directory 'status.json') -Encoding UTF8 }\r\n"
                    + " $process = [pscustomobject]@{ ExitCode = 997 }\r\n"
                    + " if ($scenario -eq 'cancelled') { $process.ExitCode=1223 }\r\n"
                    + " $process | Add-Member -MemberType ScriptMethod -Name WaitForExit -Value { param($milliseconds) return ($scenario -ne 'wait-timeout') }\r\n"
                    + " return $process\r\n}\r\n");
                using var process = Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + script + "\"")
                {
                    UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true, RedirectStandardError = true
                });
                Assert.NotNull(process);
                Assert.True(process!.WaitForExit(10000));
                string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                Assert.True((process.ExitCode == 0) == success, output);
                if (!success)
                {
                    Assert.Contains("Receipt:", output);
                    string reason = scenario switch
                    {
                        "pending" or "missing-receipt" => "unconfirmed at the deadline",
                        "failed" => "Deferred uninstall failed",
                        "missing-metadata" => "Invalid uninstall receipt field",
                        "remaining-file" or "image-present" => "completion was not verified",
                        "null-process" => "did not start",
                        "wait-timeout" => "did not finish",
                        "cancelled" => "returned 1223",
                        _ => "does not match this request"
                    };
                    Assert.Contains(reason, output);
                }
            }
            finally { Directory.Delete(root, true); }
        }

        private static string FindRepository()
        {
            for (string? directory = AppDomain.CurrentDomain.BaseDirectory; directory != null; directory = Path.GetDirectoryName(directory))
                if (File.Exists(Path.Combine(directory, "choco", "tools", "UninstallReceipt.ps1"))) return directory;
            throw new DirectoryNotFoundException("The actual Killendar package script was not found.");
        }
    }
}
