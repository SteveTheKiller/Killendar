using System;
using System.CodeDom.Compiler;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Killendar.Services;
using Microsoft.CSharp;
using Xunit;

namespace Killendar.Tests
{
    public sealed class DeferredUninstallRegressionTests
    {
        [Theory]
        [InlineData("complete", 0)]
        [InlineData("parent-identity", 1625)]
        [InlineData("parent-timeout", 1618)]
        [InlineData("tampered-helper", 1625)]
        [InlineData("metadata-denied", 5)]
        public void CopiedWorkerPublishesOnlyVerifiedTerminalResults(string scenario, int expected)
        {
            using var fixture = new Fixture();
            var processes = new Processes();
            var workflow = new DeferredUninstall(processes);
            Assert.Equal(997, workflow.Start(fixture.Image, fixture.Staging, fixture.Installation,
                new[] { "Killendar.exe" }, false, fixture.OperationId));
            string worker = processes.Started!.FileName;
            var pending = UninstallWorkflow.ReadReceipt(fixture.Receipt);
            Assert.Equal("Pending", pending.State);
            Assert.False(pending.MetadataRemoved);
            Assert.True(File.Exists(fixture.Image));
            Assert.True(File.Exists(fixture.Marker));
            processes.ParentPresent = scenario.StartsWith("parent-", StringComparison.Ordinal);
            processes.ParentCreated = scenario == "parent-identity" ? 101 : 100;
            processes.ParentExited = scenario != "parent-timeout";
            if (scenario == "tampered-helper") File.WriteAllText(worker, "changed copy");
            int result = workflow.RunWorker(worker, fixture.Staging, fixture.Installation,
                new[] { "Killendar.exe" }, false, fixture.OperationId, () =>
                {
                    if (scenario == "metadata-denied") throw new UnauthorizedAccessException("fixture metadata denied");
                    Assert.False(File.Exists(fixture.Image));
                    File.Delete(fixture.Marker);
                }, 1, 1, 0);
            Assert.Equal(expected, result);
            var terminal = UninstallWorkflow.ReadReceipt(fixture.Receipt);
            Assert.Equal(expected, terminal.ExitCode);
            Assert.Equal(expected == 0 ? "Completed" : "Failed", terminal.State);
            Assert.Equal(expected != 0, File.Exists(fixture.Marker));
            Assert.Equal("calendar contents", File.ReadAllText(fixture.Calendar));
            if (expected == 0)
            {
                Assert.False(File.Exists(fixture.Image));
                Assert.Empty(terminal.RemainingFiles);
                Assert.True(terminal.MetadataRemoved);
            }
        }

        [Fact]
        public void ExistingOperationDirectoryIsNeverReused()
        {
            using var fixture = new Fixture();
            Directory.CreateDirectory(Path.GetDirectoryName(fixture.Receipt)!);
            File.WriteAllText(fixture.Receipt, "preexisting receipt");
            var processes = new Processes();
            Assert.NotEqual(0, new DeferredUninstall(processes).Start(fixture.Image, fixture.Staging,
                fixture.Installation, new[] { "Killendar.exe" }, false, fixture.OperationId));
            Assert.Null(processes.Started);
            Assert.Equal("preexisting receipt", File.ReadAllText(fixture.Receipt));
            Assert.True(File.Exists(fixture.Image));
        }

        [Fact]
        public void RealOwnedParentMustExitBeforeTheCopiedWorkerDeletesItsImage()
        {
            using var fixture = new Fixture();
            fixture.CompileHost();
            using var parent = Process.Start(fixture.StartInfo("--parent"));
            Assert.NotNull(parent);
            Process? worker = null;
            try
            {
                Assert.True(SpinWait.SpinUntil(() => File.Exists(fixture.Ready), 10000), fixture.ChildDiagnostics());
                Assert.Equal("997", File.ReadAllText(fixture.Ready));
                Assert.Equal("Pending", UninstallWorkflow.ReadReceipt(fixture.Receipt).State);
                Assert.True(File.Exists(fixture.Image));
                Assert.True(File.Exists(fixture.Marker));
                Assert.False(parent!.HasExited);
                File.WriteAllText(fixture.Release, "exit");
                Assert.True(parent.WaitForExit(60000), fixture.ChildDiagnostics());
                Assert.Equal(997, parent.ExitCode);
                Assert.True(SpinWait.SpinUntil(() =>
                {
                    try { return UninstallWorkflow.ReadReceipt(fixture.Receipt).State != "Pending"; }
                    catch (IOException) { return false; }
                }, 10000), fixture.ChildDiagnostics());
                var result = UninstallWorkflow.ReadReceipt(fixture.Receipt);
                Assert.Equal("Completed", result.State);
                Assert.Equal(0, result.ExitCode);
                Assert.False(File.Exists(fixture.Image));
                Assert.False(File.Exists(fixture.Marker));
                Assert.Equal("calendar contents", File.ReadAllText(fixture.Calendar));
                if (File.Exists(fixture.WorkerId))
                {
                    worker = fixture.OpenWorker();
                    if (worker != null) Assert.True(worker.WaitForExit(60000));
                }
            }
            catch (Exception error) { fixture.Failure = error; throw; }
            finally
            {
                File.WriteAllText(fixture.Release, "exit");
                if (parent != null && !parent.WaitForExit(10000)) { parent.Kill(); parent.WaitForExit(5000); }
                worker ??= fixture.OpenWorker();
                if (worker != null) { if (!worker.WaitForExit(10000)) { worker.Kill(); worker.WaitForExit(5000); } worker.Dispose(); }
            }
        }

        [Fact]
        public void ActualDelegatedChildExitCodeIsPropagated()
        {
            using var fixture = new Fixture();
            fixture.CompileHost();
            int? result = UninstallWorkflow.RequestElevation(true, () => false,
                () => Process.Start(fixture.StartInfo("--exit-seven")), _ => { }, 10000);
            Assert.True(result == 7, "Delegated exit was " + result + ". " + fixture.ChildDiagnostics());
        }

        [Fact]
        public void ReservedRetainedWorkerWithoutArgumentsCannotEnterNormalStartup()
        {
            using var fixture = new Fixture();
            string image = Path.Combine(DeferredUninstall.OperationDirectory(fixture.Staging, fixture.OperationId), DeferredUninstall.WorkerName);
            Assert.True(DeferredUninstall.IsHelperRequest(Array.Empty<string>(), image, fixture.Installation, fixture.Staging));
        }

        [Theory]
        [InlineData("outside-root")]
        [InlineData("invalid-operation")]
        [InlineData("ordinary-image")]
        public void PortableImagesOutsideTheReservedWorkerRoleKeepNormalStartup(string scenario)
        {
            using var fixture = new Fixture();
            string root = scenario == "outside-root" ? Path.Combine(fixture.Root, "portable") : fixture.Staging;
            string operation = scenario == "invalid-operation" ? Path.Combine(root, "Uninstall-not-a-guid")
                : DeferredUninstall.OperationDirectory(root, fixture.OperationId);
            string image = Path.Combine(operation, scenario == "ordinary-image" ? "Killendar.exe" : DeferredUninstall.WorkerName);
            Assert.False(DeferredUninstall.IsHelperRequest(Array.Empty<string>(), image, fixture.Installation, fixture.Staging));
        }

        private sealed class Fixture : IDisposable
        {
            public string Root { get; } = Path.Combine(TestScratch.Root + @"\uninstall", Guid.NewGuid().ToString("N"), "calendar-é-測試");
            public string OperationId { get; } = Guid.NewGuid().ToString("N");
            public string Installation => Path.Combine(Root, "installed");
            public string Staging => Path.Combine(Root, "operations");
            public string Image => Path.Combine(Installation, "Killendar.exe");
            public string Calendar => Path.Combine(Installation, "Work.kcal");
            public string Marker => Path.Combine(Root, "uninstall-marker.txt");
            public string Receipt => Path.Combine(DeferredUninstall.OperationDirectory(Staging, OperationId), DeferredUninstall.ReceiptName);
            public string Ready => Path.Combine(Root, "ready.txt");
            public string Release => Path.Combine(Root, "release.txt");
            public string WorkerId => Path.Combine(Root, "worker.txt");
            public Exception? Failure;
            public Fixture()
            {
                Directory.CreateDirectory(Installation);
                Directory.CreateDirectory(Staging);
                File.WriteAllText(Image, "inert copied image");
                File.WriteAllText(Calendar, "calendar contents");
                File.WriteAllText(Marker, "registered");
            }
            public ProcessStartInfo StartInfo(string argument)
            {
                var start = new ProcessStartInfo(Image, argument)
                    { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
                start.EnvironmentVariables["UNINSTALL_FIXTURE_ROOT"] = Root;
                start.EnvironmentVariables["UNINSTALL_FIXTURE_ID"] = OperationId;
                start.EnvironmentVariables["UNINSTALL_FIXTURE_ASSEMBLY"] = new Uri(typeof(UninstallOwnedProcessFixture).Assembly.CodeBase).LocalPath;
                return start;
            }
            public void CompileHost()
            {
                File.Delete(Image);
                using var provider = new CSharpCodeProvider();
                string host = Path.Combine(Root, "OwnedUninstallHost.exe");
                var parameters = new CompilerParameters(new[] { "System.dll" }, host)
                {
                    GenerateExecutable = true, CompilerOptions = "/target:winexe /platform:x64",
                    TempFiles = new TempFileCollection(Root)
                };
                var compiled = provider.CompileAssemblyFromSource(parameters, Host);
                Assert.False(compiled.Errors.HasErrors, string.Join("\n", compiled.Errors.Cast<CompilerError>().Select(error => error.ToString())));
                File.Move(host, Image);
                string config = new Uri(typeof(UninstallOwnedProcessFixture).Assembly.CodeBase).LocalPath + ".config";
                if (File.Exists(config)) File.Copy(config, Image + ".config");
            }
            public string ChildDiagnostics() => File.Exists(Path.Combine(Root, "error.txt"))
                ? File.ReadAllText(Path.Combine(Root, "error.txt")) : "The inert uninstall child did not reach its expected boundary.";
            public Process? OpenWorker()
            {
                if (!File.Exists(WorkerId)) return null;
                string[] identity = File.ReadAllText(WorkerId).Split('|');
                Process? process = null;
                try
                {
                    process = Process.GetProcessById(int.Parse(identity[0], System.Globalization.CultureInfo.InvariantCulture));
                    _ = process.Handle;
                    if (process.StartTime.ToFileTimeUtc() == long.Parse(identity[1], System.Globalization.CultureInfo.InvariantCulture)) return process;
                }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception error) when (error.NativeErrorCode == 87) { }
                catch { process?.Dispose(); throw; }
                process?.Dispose();
                return null;
            }
            public void Dispose()
            {
                string root = Path.GetFullPath(Root);
                if (!root.StartsWith(TestScratch.Root + @"\uninstall\", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Unexpected fixture cleanup root.");
                string identity = File.Exists(WorkerId) ? File.ReadAllText(WorkerId) : "no worker identity recorded";
                using (var worker = OpenWorker())
                    if (worker != null && !worker.WaitForExit(5000))
                        throw new IOException("Retained active fixture worker. Root: " + root + ". Identity: " + identity, Failure);
                string image = Path.Combine(DeferredUninstall.OperationDirectory(Staging, OperationId), DeferredUninstall.WorkerName);
                Exception? tailError = null;
                bool released = SpinWait.SpinUntil(() =>
                {
                    try { File.Delete(image); return true; }
                    catch (DirectoryNotFoundException) { return true; }
                    catch (IOException error) { tailError = error; return false; }
                    catch (UnauthorizedAccessException error) { tailError = error; return false; }
                }, 5000);
                if (!released) throw new AggregateException("Retained fixture files after worker exit. Root: " + root + ". Identity: " + identity,
                    new[] { Failure, tailError }.Where(error => error != null).Cast<Exception>());
                foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories)) File.Delete(file);
                foreach (string folder in Directory.GetDirectories(root, "*", SearchOption.AllDirectories).OrderByDescending(path => path.Length))
                    Directory.Delete(folder, false);
                Directory.Delete(root, false);
                string parent = Path.GetDirectoryName(root)!;
                if (!Directory.EnumerateFileSystemEntries(parent).Any()) Directory.Delete(parent, false);
            }
        }

        private sealed class Processes : IInstallRelaunchProcesses
        {
            public ProcessStartInfo? Started;
            public bool ParentPresent, ParentExited = true;
            public long ParentCreated = 100;
            public IInstallRelaunchProcess Current() => new Identity(Started == null ? 1 : 2, 100, true);
            public IInstallRelaunchProcess? Find(int id) => ParentPresent ? new Identity(id, ParentCreated, ParentExited) : null;
            public bool Start(ProcessStartInfo start) { Started = start; return true; }
        }

        private sealed class Identity(int id, long created, bool exited) : IInstallRelaunchProcess
        {
            public int Id => id;
            public long CreationFileTime => created;
            public bool WaitForExit(int milliseconds) => exited;
            public void Dispose() { }
        }

        private const string Host = @"
using System;
using System.IO;
using System.Reflection;
internal static class OwnedUninstallHost
{
    private static int Main(string[] args)
    {
        string root = Environment.GetEnvironmentVariable(""UNINSTALL_FIXTURE_ROOT"");
        try
        {
            string assemblyPath = Environment.GetEnvironmentVariable(""UNINSTALL_FIXTURE_ASSEMBLY"");
            AppDomain.CurrentDomain.AssemblyResolve += (sender, request) =>
            {
                string file = Path.Combine(Path.GetDirectoryName(assemblyPath), new AssemblyName(request.Name).Name + "".dll"");
                return File.Exists(file) ? Assembly.LoadFrom(file) : null;
            };
            var fixture = Assembly.LoadFrom(assemblyPath).GetType(""Killendar.Tests.UninstallOwnedProcessFixture"", true);
            return (int)fixture.GetMethod(""Run"").Invoke(null, new object[] { args });
        }
        catch (Exception error) { File.WriteAllText(Path.Combine(root, ""error.txt""), error.ToString()); return 1; }
    }
}";
    }

    public static class UninstallOwnedProcessFixture
    {
        public static int Run(string[] args)
        {
            if (args.Length == 1 && args[0] == "--exit-seven") return 7;
            string root = Environment.GetEnvironmentVariable("UNINSTALL_FIXTURE_ROOT")!;
            string id = Environment.GetEnvironmentVariable("UNINSTALL_FIXTURE_ID")!;
            string installation = Path.Combine(root, "installed");
            string staging = Path.Combine(root, "operations");
            string image = Process.GetCurrentProcess().MainModule!.FileName;
            if (args.Length == 1 && args[0] == "--parent")
            {
                int result = new DeferredUninstall().Start(image, staging, installation, new[] { "Killendar.exe" }, false, id);
                string ready = Path.Combine(root, "ready.txt");
                File.WriteAllText(ready + ".tmp", result.ToString());
                File.Move(ready + ".tmp", ready);
                if (!SpinWait.SpinUntil(() => File.Exists(Path.Combine(root, "release.txt")), 10000)) return 1618;
                return result;
            }
            if (args.Length == 3 && args[0] == DeferredUninstall.HelperArgument && args[1] == "user" && args[2] == id)
            {
                using (var worker = Process.GetCurrentProcess())
                {
                    string identity = worker.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|"
                        + worker.StartTime.ToFileTimeUtc().ToString(System.Globalization.CultureInfo.InvariantCulture);
                    string marker = Path.Combine(root, "worker.txt");
                    File.WriteAllText(marker + ".tmp", identity);
                    File.Move(marker + ".tmp", marker);
                }
                return new DeferredUninstall().RunWorker(image, staging, installation, new[] { "Killendar.exe" }, false, id,
                    () => File.Delete(Path.Combine(root, "uninstall-marker.txt")), 30000, 30, 100);
            }
            return 1625;
        }
    }
}
