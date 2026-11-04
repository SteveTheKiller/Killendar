using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>The install, uninstall and settings-save messages are read from the string files like
    /// the rest of the interface, every string file still loads, and no translation drops or adds a
    /// format placeholder.</summary>
    [Collection("Calendar settings")]
    public sealed class SetupTextLocalizationTests
    {
        [Fact]
        public void InstallAndUninstallDialogsCarryNoEnglishText()
        {
            string text = File.ReadAllText(Path.Combine(Root(), "App.Installation.cs"));
            var english = new List<string>();
            foreach (Match call in Regex.Matches(text, @"\b(?:ShowInstallDialog|ConfirmDialog)\("))
                foreach (Match literal in Regex.Matches(Argument(text, call.Index + call.Length), @"""((?:[^""\\]|\\.)*)"""))
                    if (Regex.IsMatch(literal.Groups[1].Value, "[A-Za-z]") && !Regex.IsMatch(literal.Groups[1].Value, @"^Str_\w+$"))
                        english.Add(literal.Value);
            Assert.True(english.Count == 0, string.Join(Environment.NewLine, english));
        }

        [Fact]
        public void SettingsSaveFailuresAreReadFromTheStringFiles()
        {
            var english = new List<string>();
            foreach (string file in Directory.GetFiles(Path.Combine(Root(), "Services"), "*.cs"))
            {
                string text = File.ReadAllText(file);
                foreach (Match call in Regex.Matches(text, @"new SettingsWriteException\("))
                {
                    string argument = Argument(text, call.Index + call.Length);
                    if (!Regex.IsMatch(argument, @"^\s*UiText\.Loc\(""Str_\w+""\)\s*$"))
                        english.Add(Path.GetFileName(file) + ": " + argument);
                }
            }
            Assert.True(english.Count == 0, string.Join(Environment.NewLine, english));
        }

        [Fact]
        public void TheDowngradeQuestionIsGivenBothVersions()
        {
            var versions = new Dictionary<string, string?> { ["run.exe"] = "1.2.0.0", ["inst.exe"] = "1.3.0.0" };
            (string, string)? asked = null;
            Assert.False(InstallDowngrade.Accepted("run.exe", "inst.exe", p => versions[p], (running, installed) =>
            {
                asked = (running, installed);
                return false;
            }));
            Assert.Equal(("1.2.0.0", "1.3.0.0"), asked);
        }

        [Fact]
        public void SetupPromptsReadTheSavedLanguageWithoutSavingAnything()
        {
            var get = ThemeManager.GetSetting;
            var set = ThemeManager.SetSetting;
            string? savedLocale = Settings.Get("Locale");
            try
            {
                Settings.Set("Locale", "De");
                string? seen = null;
                SetupLocale.Apply(() =>
                {
                    seen = ThemeManager.GetSetting("Locale");
                    ThemeManager.SetSetting("Locale", "Fr");
                });
                Assert.Equal("De", seen);
                Assert.Equal("De", Settings.Get("Locale"));
                Assert.Same(get, ThemeManager.GetSetting);
                Assert.Same(set, ThemeManager.SetSetting);
            }
            finally
            {
                Settings.Set("Locale", savedLocale ?? "");
                ThemeManager.GetSetting = get;
                ThemeManager.SetSetting = set;
            }
        }

        [Fact]
        public void EveryStringFileLoadsAndKeepsTheEnglishPlaceholders()
        {
            var files = Directory.GetFiles(Path.Combine(Root(), "Strings"), "*.xaml");
            Assert.Equal(20, files.Length);
            var dictionaries = new Dictionary<string, ResourceDictionary>();
            Exception? failure = null;
            // A string file that does not parse is dropped silently at run time and the whole
            // language falls back to English, so each one is loaded here the same way.
            var thread = new Thread(() =>
            {
                try
                {
                    foreach (string file in files)
                        dictionaries[Path.GetFileName(file)] = (ResourceDictionary)XamlReader.Parse(File.ReadAllText(file));
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            Assert.Null(failure);

            var english = dictionaries["en-US.xaml"];
            var wrong = new List<string>();
            foreach (var pair in dictionaries.Where(d => d.Key != "en-US.xaml"))
                foreach (var key in pair.Value.Keys.OfType<string>())
                    if (english[key] is string source && pair.Value[key] is string translated
                        && !Placeholders(source).SetEquals(Placeholders(translated)))
                        wrong.Add($"{pair.Key} {key}: \"{translated}\"");
            Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
        }

        private static HashSet<string> Placeholders(string text)
            => new(Regex.Matches(text, @"\{\d+(?:[,:][^}]*)?\}").Cast<Match>().Select(m => m.Value));

        // The text up to the parenthesis that closes the call opened just before start.
        private static string Argument(string text, int start)
        {
            int depth = 1;
            bool quoted = false;
            for (int i = start; i < text.Length; i++)
            {
                if (text[i] == '"' && text[i - 1] != '\\') quoted = !quoted;
                if (quoted) continue;
                if (text[i] == '(') depth++;
                else if (text[i] == ')' && --depth == 0) return text.Substring(start, i - start);
            }
            return "";
        }

        private static string Root([System.Runtime.CompilerServices.CallerFilePath] string source = "")
            => Path.GetDirectoryName(Path.GetDirectoryName(source))!;
    }
}
