using System;
using System.CodeDom.Compiler;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.CSharp;
using Xunit;

namespace Killendar.Tests
{
    internal static class ScratchUiProcess
    {
        internal static void Run(Type publicFixture, string? scenario = null)
        {
            string root = Path.Combine(@"C:\Users\steve\scratch\killendar-remediation\ui-process",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string executable = Path.Combine(root, "KillendarUiFixture.exe");
                string resources = Path.Combine(root, "KillendarUiFixture.g.resources");
                using (var source = typeof(App).Assembly.GetManifestResourceStream("Killendar.g.resources")!)
                using (var destination = File.Create(resources)) source.CopyTo(destination);
                using (var source = typeof(App).Assembly.GetManifestResourceStream("Killendar.g.resources")!)
                using (var extracted = File.OpenRead(resources))
                using (var hash = SHA256.Create())
                    Assert.Equal(hash.ComputeHash(source), hash.ComputeHash(extracted));
                using (var compiler = new CSharpCodeProvider())
                {
                    var options = new CompilerParameters(new[] { "System.dll" }, executable)
                    {
                        GenerateExecutable = true,
                        CompilerOptions = "/platform:x64",
                        TempFiles = new TempFileCollection(root, false)
                    };
                    options.EmbeddedResources.Add(resources);
                    var result = compiler.CompileAssemblyFromSource(options, HostSource);
                    Assert.False(result.Errors.HasErrors, string.Join(Environment.NewLine,
                        result.Errors.Cast<CompilerError>().Select(error => error.ToString())));
                }
                string assembly = new Uri(publicFixture.Assembly.CodeBase).LocalPath;
                File.Copy(assembly + ".config", executable + ".config");
                var start = new ProcessStartInfo(executable)
                {
                    Arguments = Quote(assembly) + " " + Quote(publicFixture.FullName!) +
                        (scenario == null ? "" : " " + Quote(scenario)),
                    WorkingDirectory = root,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var process = Process.Start(start)!;
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(30000))
                {
                    process.Kill();
                    process.WaitForExit();
                    throw new TimeoutException("The scratch UI fixture process exceeded its deadline.");
                }
                string evidence = "Fixture process " + process.Id + " exited " + process.ExitCode +
                    Environment.NewLine + output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult();
                Assert.True(process.ExitCode == 0, evidence);
            }
            finally { Directory.Delete(root, true); }
        }

        private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

        private const string HostSource = @"
using System;
using System.IO;
using System.Reflection;
public static class Host
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            string output = Path.GetDirectoryName(args[0]);
            AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs request)
            {
                string path = Path.Combine(output, new AssemblyName(request.Name).Name + "".dll"");
                return File.Exists(path) ? Assembly.LoadFrom(path) : null;
            };
            foreach (string name in new[] { ""SQLitePCLRaw.core.dll"", ""SQLitePCLRaw.provider.e_sqlcipher.dll"" })
                Assembly.LoadFrom(Path.Combine(output, name));
            Assembly tests = Assembly.LoadFrom(args[0]);
            Type fixture = tests.GetType(args[1], true);
            MethodInfo run = args.Length > 2
                ? fixture.GetMethod(""Run"", new[] { typeof(string) })
                : fixture.GetMethod(""Run"", Type.EmptyTypes);
            run.Invoke(Activator.CreateInstance(fixture), args.Length > 2 ? new object[] { args[2] } : null);
            Console.WriteLine(""PASS: "" + fixture.FullName);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}";
    }
}
