using System;
using System.IO;
using KillerFamily.CommandLine;
using Xunit;

namespace Killendar.Tests
{
    // The current-folder test runs alone: it changes the process's working directory.
    [CollectionDefinition("Current directory", DisableParallelization = true)]
    public sealed class CurrentDirectoryCollection { }

    // CommandLineEntry.GetArguments decides whether a launch is a command or belongs to the window.
    [Collection("Current directory")]
    public sealed class CommandLineEntryTests
    {
        private static readonly string[] Commands = ["agenda", "create"];

        [Fact]
        public void NoArgumentsIsAnOrdinaryLaunch()
        {
            Assert.Null(CommandLineEntry.GetArguments([], Commands));
        }

        [Theory]
        [InlineData("--cli")]
        [InlineData("--CLI")]
        public void TheCliSwitchIsStrippedAndTheRestPassesThrough(string switchText)
        {
            Assert.Equal(["agenda", "2026-10-01", "1"],
                CommandLineEntry.GetArguments([switchText, "agenda", "2026-10-01", "1"], Commands));
            Assert.Empty(CommandLineEntry.GetArguments([switchText], Commands)!);
        }

        [Theory]
        [InlineData("help")]
        [InlineData("HELP")]
        [InlineData("--help")]
        [InlineData("-h")]
        [InlineData("/?")]
        public void EveryHelpSpellingBecomesTheHelpSwitchAndKeepsItsTopic(string spelling)
        {
            Assert.Equal(["--help"], CommandLineEntry.GetArguments([spelling], Commands));
            Assert.Equal(["--help", "agenda"], CommandLineEntry.GetArguments([spelling, "agenda"], Commands));
        }

        [Fact]
        public void ACommandIsRoutedWithItsArguments()
        {
            Assert.Equal(["agenda", "2026-10-01", "1", "--limit", "5"],
                CommandLineEntry.GetArguments(["agenda", "2026-10-01", "1", "--limit", "5"], Commands));
        }

        [Fact]
        public void TheCommandPrefixIsAddedAndTheCommandKeepsItsOwnSpelling()
        {
            Assert.Equal(["--create", "{}"], CommandLineEntry.GetArguments(["CREATE", "{}"], Commands, "--"));
        }

        [Theory]
        [InlineData("notes.ics")]
        [InlineData("Home.kcal")]
        [InlineData("agendas")]
        [InlineData("--agenda")]
        public void ANameThatIsNotACommandLeavesTheLaunchToTheWindow(string argument)
        {
            Assert.Null(CommandLineEntry.GetArguments([argument], Commands));
            Assert.Null(CommandLineEntry.GetArguments([argument, "extra"], Commands));
        }

        [Fact]
        public void ALoneVerbThatNeedsAnArgumentIsAFileWhenOneByThatNameExists()
        {
            string folder = Path.Combine(TestScratch.Root, "entry-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string previous = Directory.GetCurrentDirectory();
            try
            {
                Directory.SetCurrentDirectory(folder);
                string[] needsArgument = ["create"];

                // No file or folder of that name: it is the command, which then reports its own missing argument.
                Assert.Equal(["create"], CommandLineEntry.GetArguments(["create"], Commands, argumentCommands: needsArgument));

                File.WriteAllText("create", "a document called create");
                Assert.Null(CommandLineEntry.GetArguments(["create"], Commands, argumentCommands: needsArgument));
                // Only a lone verb is the document, and only a verb that needs an argument.
                Assert.Equal(["create", "{}"], CommandLineEntry.GetArguments(["create", "{}"], Commands, argumentCommands: needsArgument));
                Assert.Equal(["create"], CommandLineEntry.GetArguments(["create"], Commands));
                Assert.Equal(["create"], CommandLineEntry.GetArguments(["create"], Commands, argumentCommands: ["agenda"]));

                File.Delete("create");
                Directory.CreateDirectory("create");
                Assert.Null(CommandLineEntry.GetArguments(["create"], Commands, argumentCommands: needsArgument));
            }
            finally
            {
                Directory.SetCurrentDirectory(previous);
                Directory.Delete(folder, true);
            }
        }

        [Fact]
        public void TheCallersArgumentsAreNotModified()
        {
            string[] original = ["help", "agenda"];

            CommandLineEntry.GetArguments(original, Commands);

            Assert.Equal(["help", "agenda"], original);
        }
    }
}
