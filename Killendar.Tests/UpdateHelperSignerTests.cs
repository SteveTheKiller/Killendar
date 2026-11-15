using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using Killendar.Services;
using Microsoft.CSharp;
using Xunit;

namespace Killendar.Tests
{
    // GB-004, helper side: the C# that the embedded PowerShell helper compiles reads its signer
    // from the WinVerifyTrust state that verified the file, as CodeSignature does.
    public sealed class UpdateHelperSignerTests
    {
        private static Type CompileGuard()
        {
            string script = UpdateImagePublication.Guard;
            const string open = "$killerGuardCode = @'";
            int start = script.IndexOf(open, StringComparison.Ordinal) + open.Length;
            int end = script.IndexOf("\n'@", start, StringComparison.Ordinal);
            string source = script.Substring(start, end - start);
            using var provider = new CSharpCodeProvider(new System.Collections.Generic.Dictionary<string, string> { ["CompilerVersion"] = "v4.0" });
            var options = new CompilerParameters(new[] { "System.dll", "System.Core.dll" }) { GenerateInMemory = true };
            var result = provider.CompileAssemblyFromSource(options, source);
            Assert.True(result.Errors.Count == 0, string.Join(Environment.NewLine, result.Errors.Cast<CompilerError>().Select(e => e.ToString())));
            return result.CompiledAssembly.GetType("KillerUpdateGuard", true)!;
        }

        private static string? Thumbprint(string path)
        {
            try { using var cert = X509Certificate.CreateFromSignedFile(path); return new X509Certificate2(cert).Thumbprint; }
            catch { return null; }
        }

        [Fact]
        public void TheHelperReadsTheSignerFromTheVerificationItself()
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet");
            if (!Directory.Exists(root)) return;
            // Two validly signed files from different certificates, to stand for "the file that was
            // read" and "the file that was verified".
            string? first = null, second = null; string? firstThumb = null, secondThumb = null;
            foreach (string candidate in Directory.EnumerateFiles(root, "*.dll", SearchOption.AllDirectories).Take(600))
            {
                string? thumb = Thumbprint(candidate);
                if (thumb == null) continue;
                if (first == null) { first = candidate; firstThumb = thumb; }
                else if (thumb != firstThumb && CodeSignature.GetSignerInfo(candidate).valid && CodeSignature.GetSignerInfo(first).valid)
                { second = candidate; secondThumb = thumb; break; }
            }
            if (first == null || second == null) return;
            string directory = Path.Combine(TestScratch.Root, "gb004h-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string copy = Path.Combine(directory, "app.dll");
            try
            {
                File.Copy(first, copy);
                var guard = CompileGuard();
                var method = guard.GetMethod("Signature", new[] { typeof(string), typeof(string).MakeByRefType(), typeof(string).MakeByRefType(), typeof(Action) })!;
                var arguments = new object?[] { copy, null, null, (Action)(() => File.Copy(second, copy, true)) };

                bool valid = (bool)method.Invoke(null, arguments)!;

                Assert.True(valid);
                Assert.Equal(secondThumb, (string)arguments[1]!, ignoreCase: true);
            }
            finally { Directory.Delete(directory, true); }
        }
    }
}
