using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>Every Str_ key the code asks Loc() for exists in every locale file, so no language
    /// ever shows a raw key. Keys built by concatenation (a prefix plus a number or name) are left
    /// out; the scan only reads whole literals, including both arms of a conditional.</summary>
    public sealed class StringKeyCompletenessTests
    {
        private static readonly string[] SkippedFolders = { "obj", "bin", "Killendar.Tests", "tests", "third_party", "killendar-landing" };

        [Fact]
        public void EveryLocKeyExistsInEveryLocale()
        {
            string root = Root();
            var keys = LocKeys(root);
            Assert.NotEmpty(keys);
            var locales = Directory.GetFiles(Path.Combine(root, "Strings"), "*.xaml");
            Assert.Equal(20, locales.Length);
            var missing = new List<string>();
            foreach (string locale in locales)
            {
                var defined = new HashSet<string>(Regex.Matches(File.ReadAllText(locale), @"x:Key=""(Str_\w+)""")
                    .Cast<Match>().Select(m => m.Groups[1].Value));
                foreach (var key in keys.Where(k => !defined.Contains(k.Key)))
                    missing.Add($"{Path.GetFileName(locale)} lacks {key.Key} ({key.Value})");
            }
            Assert.True(missing.Count == 0, string.Join(Environment.NewLine, missing));
        }

        // Key to the first file that asks for it.
        private static Dictionary<string, string> LocKeys(string root)
        {
            var keys = new Dictionary<string, string>();
            foreach (string file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                string relative = file.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar);
                if (SkippedFolders.Any(f => relative.Split(Path.DirectorySeparatorChar).Contains(f))) continue;
                string text = File.ReadAllText(file);
                foreach (Match call in Regex.Matches(text, @"\bLoc\("))
                {
                    string argument = Argument(text, call.Index + call.Length);
                    foreach (Match literal in Regex.Matches(argument, @"(?<!\+\s*)""(Str_\w+)""(?!\s*\+)"))
                        if (!keys.ContainsKey(literal.Groups[1].Value)) keys[literal.Groups[1].Value] = relative;
                }
            }
            return keys;
        }

        // The text up to the parenthesis that closes the call opened just before start.
        private static string Argument(string text, int start)
        {
            int depth = 1;
            for (int i = start; i < text.Length; i++)
            {
                if (text[i] == '(') depth++;
                else if (text[i] == ')' && --depth == 0) return text.Substring(start, i - start);
            }
            return "";
        }

        // From this file's own path, so the check works wherever the test build lands.
        private static string Root([System.Runtime.CompilerServices.CallerFilePath] string source = "")
        {
            string root = Path.GetDirectoryName(Path.GetDirectoryName(source))!;
            if (!Directory.Exists(Path.Combine(root, "Strings")))
                throw new InvalidOperationException("Strings/ was not found beside the test project.");
            return root;
        }
    }
}
