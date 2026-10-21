using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    public sealed class UpdateHelperSignatureRegressionTests
    {
        [Fact]
        public void MatchingChecksumCannotPublishWhenSignatureVerificationRejectsCandidate()
        {
            using var fixture = new HelperFixture();
            var result = fixture.Run();

            Assert.False(result.Published, result.Diagnostics);
            Assert.Equal(fixture.OriginalBytes, File.ReadAllBytes(fixture.Target));
            Assert.Equal(fixture.CandidateBytes, File.ReadAllBytes(fixture.Download));
            Assert.Equal("1", result.Fields["SignatureCalls"]);
            Assert.Equal(fixture.Download, result.Fields["SignaturePath0"]);
            Assert.Equal("0", result.Fields["MetadataCalls"]);
            Assert.Equal("0", result.Fields["RelaunchCalls"]);
            Assert.Empty(Directory.GetFiles(fixture.Root, "target.bin.new-*"));
            Assert.Contains("signature", File.ReadAllText(fixture.Download + ".recovery.txt"),
                StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void EmittedTrustImplementationCompilesAndRejectsRealUnsignedImage()
        {
            using var fixture = new HelperFixture(realUnsignedImage: true);
            var result = fixture.Run("actual");
            Assert.False(result.Published, result.Diagnostics);
            Assert.Equal("True", result.Fields["TrustCompiled"]);
            Assert.Equal("1", result.Fields["SignatureCalls"]);
            Assert.Equal(fixture.OriginalBytes, File.ReadAllBytes(fixture.Target));
            Assert.Equal("0", result.Fields["RelaunchCalls"]);
            Assert.Contains("Authenticode", File.ReadAllText(fixture.Download + ".recovery.txt"));
        }

        [Fact]
        public void RejectedStagedSignaturePreservesOriginalAndRetainsRecoveryEvidence()
        {
            using var fixture = new HelperFixture();
            var result = fixture.Run("reject-stage");
            Assert.False(result.Published, result.Diagnostics);
            Assert.Equal("2", result.Fields["SignatureCalls"]);
            Assert.Equal(fixture.OriginalBytes, File.ReadAllBytes(fixture.Target));
            Assert.Equal(fixture.Download, result.Fields["SignaturePath0"]);
            Assert.StartsWith(fixture.Target + ".new-", result.Fields["SignaturePath1"]);
            Assert.Equal(fixture.CandidateBytes,
                File.ReadAllBytes(Assert.Single(Directory.GetFiles(fixture.Root, "target.bin.new-*"))));
            Assert.Equal("0", result.Fields["MetadataCalls"]);
            Assert.Equal("0", result.Fields["RelaunchCalls"]);
            Assert.Contains("staged update", File.ReadAllText(fixture.Download + ".recovery.txt"));
        }

        [Fact]
        public void AcceptedSignaturesPublishUsingRealFileOperations()
        {
            using var fixture = new HelperFixture();
            var result = fixture.Run("accept");
            Assert.True(result.Published, result.Diagnostics);
            Assert.Equal(fixture.CandidateBytes, File.ReadAllBytes(fixture.Target));
            Assert.Equal("2", result.Fields["SignatureCalls"]);
            Assert.Equal("1", result.Fields["MetadataCalls"]);
            Assert.Equal("1", result.Fields["RelaunchCalls"]);
            Assert.False(File.Exists(fixture.Download));
            Assert.False(File.Exists(fixture.Download + ".recovery.txt"));
            Assert.Empty(Directory.GetFiles(fixture.Root, "target.bin.old-*"));
        }

        [Fact]
        public void ChangedDownloadHashRejectsBeforeSignatureOrPublication()
        {
            using var fixture = new HelperFixture();
            var result = fixture.Run("changed-hash");
            Assert.False(result.Published, result.Diagnostics);
            Assert.Equal("0", result.Fields["SignatureCalls"]);
            Assert.Equal(fixture.OriginalBytes, File.ReadAllBytes(fixture.Target));
            Assert.Equal("0", result.Fields["MetadataCalls"]);
            Assert.Equal("0", result.Fields["RelaunchCalls"]);
            Assert.Empty(Directory.GetFiles(fixture.Root, "target.bin.new-*"));
            Assert.Contains("SHA-256", File.ReadAllText(fixture.Download + ".recovery.txt"));
        }

        private sealed class HelperFixture : IDisposable
        {
            private const string ScratchParent = @"C:\Users\steve\scratch\killendar-remediation\update-helper-signature";
            private Process? _process;
            private bool _exitConfirmed;
            private bool _retainScratch;
            internal string Root { get; } = Path.Combine(ScratchParent, Guid.NewGuid().ToString("N"));
            internal string Target => Path.Combine(Root, "target.bin");
            internal string Download => Path.Combine(Root, "candidate.bin");
            internal byte[] OriginalBytes { get; } = Encoding.UTF8.GetBytes("original inert application image");
            internal byte[] CandidateBytes { get; } = Encoding.UTF8.GetBytes("signature-invalid inert candidate image");

            internal HelperFixture(bool realUnsignedImage = false)
            {
                if (realUnsignedImage)
                    CandidateBytes = File.ReadAllBytes(typeof(UpdateHelperSignatureRegressionTests).Assembly.Location);
                Directory.CreateDirectory(Root);
                File.WriteAllBytes(Target, OriginalBytes);
                File.WriteAllBytes(Download, CandidateBytes);
            }

            internal HelperResult Run(string mode = "reject-download")
            {
                UpdateImagePublication.RememberVerified(Download, Hash(CandidateBytes));
                string command = UpdateImagePublication.Command(Download, Target, false,
                    "https://example.invalid/releases", "Killendar component fixture");
                string artifact = Path.Combine(Root, "generated.ps1");
                File.WriteAllText(artifact, DecodeArtifact(command), Encoding.UTF8);
                string runner = Path.Combine(Root, "fixture.ps1");
                File.WriteAllText(runner, Runner, Encoding.UTF8);

                string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                    @"WindowsPowerShell\v1.0\powershell.exe");
                var start = new ProcessStartInfo(powershell,
                    "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + runner + "\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = Root,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                start.EnvironmentVariables["KILLENDAR_HELPER_FIXTURE_ROOT"] = Root;
                start.EnvironmentVariables["KILLENDAR_HELPER_FIXTURE_MODE"] = mode;
                start.EnvironmentVariables["TEMP"] = Root;
                start.EnvironmentVariables["TMP"] = Root;
                var process = _process = Process.Start(start);
                Assert.NotNull(process);
                var outputTask = process!.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(30000))
                {
                    _retainScratch = true;
                    string termination;
                    try
                    {
                        process.Kill();
                        _exitConfirmed = process.WaitForExit(5000);
                        termination = _exitConfirmed ? "Exit confirmed after termination." : "Exit could not be confirmed.";
                    }
                    catch (Exception exception) { termination = "Termination could not be confirmed: " + exception; }
                    Assert.Fail("The generated-helper component test exceeded its 30-second deadline. "
                        + termination + " PID: " + process.Id + ". Scratch retained: " + Root);
                }
                _exitConfirmed = true;
                try
                {
                    if (!Task.WaitAll(new Task[] { outputTask, errorTask }, 5000))
                    {
                        _retainScratch = true;
                        Assert.Fail("The helper exited, but output did not close within five seconds. PID: "
                            + process.Id + ". Scratch retained: " + Root);
                    }
                }
                catch (AggregateException exception)
                {
                    _retainScratch = true;
                    Assert.Fail("The helper output could not be read. PID: " + process.Id
                        + ". Scratch retained: " + Root + Environment.NewLine + exception);
                }
                string output = outputTask.GetAwaiter().GetResult();
                string error = errorTask.GetAwaiter().GetResult();
                string diagnostics = output + error + Environment.NewLine
                    + "Original target SHA-256: " + Hash(OriginalBytes) + Environment.NewLine
                    + "Current target SHA-256: " + (File.Exists(Target) ? Hash(File.ReadAllBytes(Target)) : "missing");
                Assert.True(process.ExitCode == 0, diagnostics);
                Assert.True(string.IsNullOrWhiteSpace(error), diagnostics);
                return new HelperResult(output, diagnostics);
            }

            public void Dispose()
            {
                if (_process != null && !_exitConfirmed)
                {
                    Console.Error.WriteLine("Helper exit is unconfirmed. Process and scratch retained. PID: "
                        + _process.Id + ". Scratch: " + Root);
                    return;
                }
                _process?.Dispose();
                if (_retainScratch) return;
                try
                {
                    string ownedRoot = Path.GetFullPath(Root);
                    string parent = Path.GetFullPath(ScratchParent) + Path.DirectorySeparatorChar;
                    if (!ownedRoot.StartsWith(parent, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("The fixture cleanup path is outside its scratch directory.");
                    foreach (string file in Directory.GetFiles(ownedRoot)) File.Delete(file);
                    Directory.Delete(ownedRoot, false);
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine("Fixture cleanup did not finish. Scratch retained: " + Root
                        + Environment.NewLine + exception);
                }
            }

            private static string DecodeArtifact(string command)
            {
                var match = Regex.Match(command, @"FromBase64String\('([A-Za-z0-9+/=]+)'\)");
                Assert.True(match.Success, "The emitted helper command did not contain its compressed artifact.");
                using var bytes = new MemoryStream(Convert.FromBase64String(match.Groups[1].Value));
                using var gzip = new GZipStream(bytes, CompressionMode.Decompress);
                using var reader = new StreamReader(gzip, Encoding.UTF8);
                return reader.ReadToEnd();
            }

            private static string Hash(byte[] bytes)
            {
                using var sha256 = SHA256.Create();
                return BitConverter.ToString(sha256.ComputeHash(bytes)).Replace("-", "");
            }

            private const string Runner = """
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($env:KILLENDAR_HELPER_FIXTURE_ROOT)
$artifact = Join-Path $root 'generated.ps1'
$tokens = $null
$parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($artifact, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -ne 0) { throw ($parseErrors | Out-String) }
$definitions = @($ast.EndBlock.Statements | Where-Object {
    $_ -is [Management.Automation.Language.FunctionDefinitionAst] -and $_.Name -eq 'Invoke-UpdateImagePublication'
})
if ($definitions.Count -ne 1) { throw 'The generated publication entry point was not found.' }
. ([scriptblock]::Create($definitions[0].Extent.Text))
foreach ($name in @('$plan', '$ops')) {
    $assignments = @($ast.EndBlock.Statements | Where-Object {
        $_ -is [Management.Automation.Language.AssignmentStatementAst] -and $_.Left.Extent.Text -eq $name
    })
    if ($assignments.Count -ne 1) { throw ('The generated assignment was not found: ' + $name) }
    . ([scriptblock]::Create($assignments[0].Extent.Text))
}
foreach ($path in @($plan.Download, $plan.Target, $plan.Staged, $plan.Backup, $plan.Restore, $plan.Report)) {
    if (![IO.Path]::GetFullPath($path).StartsWith($root + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) { throw 'A generated file path escaped the owned fixture directory.' }
}

# Keep real emitted file operations. Substitute only the external OS verdict and effects.
$script:SignaturePaths = New-Object 'Collections.Generic.List[string]'
$script:MetadataCalls = 0
$script:RelaunchCalls = 0
$actualVerifySignature = $ops.VerifySignature
$ops.VerifySignature = {
    param($path)
    $script:SignaturePaths.Add($path)
    if ($env:KILLENDAR_HELPER_FIXTURE_MODE -eq 'actual') { return (& $actualVerifySignature $path) }
    if ($env:KILLENDAR_HELPER_FIXTURE_MODE -eq 'accept') { return $true }
    if ($env:KILLENDAR_HELPER_FIXTURE_MODE -eq 'reject-stage') { return ($script:SignaturePaths.Count -eq 1) }
    return $false
}
$ops.Metadata = { param($plan) $script:MetadataCalls++ }
$ops.Relaunch = { param($plan) $script:RelaunchCalls++ }
if ($env:KILLENDAR_HELPER_FIXTURE_MODE -eq 'changed-hash') {
    [IO.File]::AppendAllText($plan.Download, 'changed after verification')
}
$published = Invoke-UpdateImagePublication $plan $ops
Write-Output ('Published=' + $published)
Write-Output ('TrustCompiled=' + ($null -ne ('KillendarUpdateTrust' -as [type])))
Write-Output ('SignatureCalls=' + $script:SignaturePaths.Count)
for ($index = 0; $index -lt $script:SignaturePaths.Count; $index++) {
    Write-Output ('SignaturePath' + $index + '=' + $script:SignaturePaths[$index])
}
Write-Output ('MetadataCalls=' + $script:MetadataCalls)
Write-Output ('RelaunchCalls=' + $script:RelaunchCalls)
""";
        }

        private sealed class HelperResult
        {
            internal Dictionary<string, string> Fields { get; } = new Dictionary<string, string>();
            internal string Diagnostics { get; }
            internal bool Published => bool.Parse(Fields["Published"]);

            internal HelperResult(string output, string diagnostics)
            {
                Diagnostics = diagnostics;
                foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    int separator = line.IndexOf('=');
                    Assert.True(separator > 0, diagnostics);
                    Fields.Add(line.Substring(0, separator), line.Substring(separator + 1));
                }
            }
        }
    }
}
