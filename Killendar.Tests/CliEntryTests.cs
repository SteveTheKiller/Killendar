using System;
using System.IO;
using Killendar.Cli;
using Xunit;

namespace Killendar.Tests
{
    // The CLI entry point, run in process with its console streams captured. Bad options explain
    // themselves on stderr and exit 2; help names the command it was asked about.
    [Collection("Calendar settings")]
    public sealed class CliEntryTests
    {
        private const string AgendaUsage = "killendar agenda <yyyy-MM-dd> <days 1..31> [--limit 1..100] [--database absolute.kcal]";
        private const string CreateUsage = "killendar create <json> (requires an open, unlocked Killendar)";

        private static (int code, string output, string error) Run(params string[] args)
        {
            TextWriter oldOut = Console.Out, oldError = Console.Error;
            var output = new StringWriter();
            var error = new StringWriter();
            Console.SetOut(output);
            Console.SetError(error);
            try { return (Program.Main(args), output.ToString(), error.ToString()); }
            finally { Console.SetOut(oldOut); Console.SetError(oldError); }
        }

        [Theory]
        [InlineData("agenda", "2026-10-01", "1", "--bogus", "x")]
        [InlineData("agenda", "2026-10-01", "1", "--limit")]
        [InlineData("agenda", "2026-10-01", "1", "--limit", "101")]
        [InlineData("agenda", "2026-10-01", "1", "--limit", "0")]
        [InlineData("agenda", "2026-10-01", "1", "--database", "relative.kcal")]
        [InlineData("agenda", "2026-10-01", "32")]
        [InlineData("agenda", "10/01/2026", "1")]
        [InlineData("frobnicate")]
        public void BadOptionsPrintUsageOnStderrAndExitTwo(params string[] args)
        {
            var (code, output, error) = Run(args);

            Assert.Equal(2, code);
            Assert.Equal("", output);
            Assert.Contains(AgendaUsage, error);
        }

        [Theory]
        [InlineData("help", "agenda")]
        [InlineData("--help", "agenda")]
        public void HelpAgendaPrintsHelpForAgenda(params string[] args)
        {
            var (code, output, error) = Run(args);

            Assert.Equal(0, code);
            Assert.Equal("", error);
            Assert.Contains(AgendaUsage, output);
            Assert.DoesNotContain("killendar create", output);
        }

        [Fact]
        public void HelpCreatePrintsHelpForCreate()
        {
            var (code, output, error) = Run("help", "create");

            Assert.Equal(0, code);
            Assert.Equal("", error);
            Assert.Contains(CreateUsage, output);
            Assert.DoesNotContain("killendar agenda", output);
        }

        [Fact]
        public void HelpAloneStillListsEveryCommand()
        {
            var (code, output, error) = Run("--help");

            Assert.Equal(0, code);
            Assert.Equal("", error);
            Assert.Contains(AgendaUsage, output);
            Assert.Contains(CreateUsage, output);
        }

        [Fact]
        public void HelpForAnUnknownCommandIsAnErrorOnStderr()
        {
            var (code, output, error) = Run("help", "frobnicate");

            Assert.Equal(2, code);
            Assert.Equal("", output);
            Assert.Contains("frobnicate", error);
            Assert.Contains(AgendaUsage, error);
        }
    }
}
