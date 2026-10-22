using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>
    /// Runs the real self-update helper, unelevated, through the same launch the app uses, with a copy of cmd.exe
    /// standing in for the downloaded update. Relaunch and the releases page are recorded instead of opened.
    /// Elevated handoff belongs to the disposable VM.
    /// </summary>
    public sealed class UpdateHelperLaunchTests
    {
        private static readonly byte[] StandIn = File.ReadAllBytes(Path.Combine(Environment.SystemDirectory, "cmd.exe"));

        [Fact]
        public void VerifiedStandInIsPublishedAndRelaunched()
        {
            // The stand-in is catalog-signed, which the embedded-signature check does not read; the unsigned case
            // below runs the real check.
            using var run = new HelperRun("verified", StandIn, acceptSignature: true);
            Assert.True(run.Exit == 0, run.ToString());
            Assert.Equal(StandIn, File.ReadAllBytes(run.Target));
            Assert.True(run.Relaunched, run.ToString());
            Assert.False(run.ReleasesOpened, run.ToString());
            Assert.False(File.Exists(run.Download), run.ToString());
        }

        [Fact]
        public void DownloadSwappedAfterVerificationIsRefused()
        {
            using var run = new HelperRun("swapped", StandIn.Concat(new byte[] { 0 }).ToArray(), acceptSignature: true);
            Assert.True(run.Exit != 0, run.ToString());
            Assert.Equal(HelperRun.Original, File.ReadAllBytes(run.Target));
            Assert.False(run.Relaunched, run.ToString());
            Assert.True(run.ReleasesOpened, run.ToString());
            Assert.Empty(Directory.GetFiles(run.Root, "Killendar.exe.new-*"));
        }

        [Fact]
        public void ImageWithoutEmbeddedSignatureIsRefused()
        {
            using var run = new HelperRun("unsigned", StandIn, acceptSignature: false);
            Assert.True(run.Exit != 0, run.ToString());
            Assert.Equal(HelperRun.Original, File.ReadAllBytes(run.Target));
            Assert.False(run.Relaunched, run.ToString());
            Assert.True(run.ReleasesOpened, run.ToString());
        }

        [Fact]
        public void ElevatedHelperStartsPowerShellWithItsScriptInTheCommandLine()
        {
            var start = UpdateHelperLaunch.StartInfo(UpdateHelperLaunch.WaitFor(1234) + "exit 0", elevate: true);
            Assert.Equal("runas", start.Verb);
            Assert.True(start.UseShellExecute);
            Assert.EndsWith(@"WindowsPowerShell\v1.0\powershell.exe", start.FileName, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("-EncodedCommand ", start.Arguments);
            Assert.DoesNotContain(".bat", start.Arguments, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.System), start.WorkingDirectory);
        }

        private sealed class HelperRun : IDisposable
        {
            internal static readonly byte[] Original = Encoding.UTF8.GetBytes("original inert application image");

            internal string Root { get; } = Path.Combine(TestScratch.Root, "update-helper-launch", Guid.NewGuid().ToString("N"));
            internal string Target => Path.Combine(Root, "Killendar.exe");
            internal string Download => Path.Combine(Root, "download.exe");
            internal int Exit { get; }
            internal bool Relaunched => File.Exists(Path.Combine(Root, "relaunched.txt"));
            internal bool ReleasesOpened => File.Exists(Path.Combine(Root, "releases.txt"));

            internal HelperRun(string name, byte[] download, bool acceptSignature)
            {
                Directory.CreateDirectory(Root);
                File.WriteAllBytes(Target, Original);
                File.WriteAllBytes(Download, download);
                UpdateImagePublication.RememberVerified(Download, Hash(StandIn));
                string overrides = "$ops.Relaunch = { param($plan) [IO.File]::WriteAllText('" + Path.Combine(Root, "relaunched.txt") + "', 'yes') }\n" +
                    "$ops.Releases = { param($url) [IO.File]::WriteAllText('" + Path.Combine(Root, "releases.txt") + "', $url) }";
                if (acceptSignature) overrides += "\n$ops.VerifySignature = { param($path) $true }";
                string script = UpdateHelperLaunch.WaitFor(0) + UpdateImagePublication.Script(Download, Target, false,
                    "https://example.invalid/releases", "Killendar " + name + " fixture", overrides: overrides);
                using var process = Process.Start(UpdateHelperLaunch.StartInfo(script, elevate: false));
                Assert.NotNull(process);
                if (!process!.WaitForExit(60000))
                {
                    process.Kill();
                    Assert.Fail("The update helper did not finish. Scratch retained: " + Root);
                }
                Exit = process.ExitCode;
            }

            public override string ToString()
            {
                string report = Download + ".recovery.txt";
                return "Exit " + Exit + ", relaunched " + Relaunched + ", releases " + ReleasesOpened +
                    (File.Exists(report) ? Environment.NewLine + File.ReadAllText(report) : "");
            }

            public void Dispose()
            {
                try { Directory.Delete(Root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }

            private static string Hash(byte[] bytes)
            {
                using var sha256 = SHA256.Create();
                return BitConverter.ToString(sha256.ComputeHash(bytes)).Replace("-", "");
            }
        }
    }
}
