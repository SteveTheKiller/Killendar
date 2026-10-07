using System;
using System.IO;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    public sealed class KillendarImportTests
    {
        [Fact]
        public void ImportReusesAFileAlreadyInTheDataFolder()
        {
            string directory = Path.Combine(Path.GetTempPath(), "KillendarImportTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string source = Path.Combine(directory, "Personal.kcal");
            File.WriteAllText(source, "existing calendar");

            try
            {
                Assert.Equal("Personal.kcal", EventStore.ImportKillendarInto(source, directory));
                Assert.Single(Directory.GetFiles(directory, "*.kcal"));
                Assert.Equal("existing calendar", File.ReadAllText(source));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void ImportCopiesAnExternalFileAndKeepsExistingCopies()
        {
            string directory = Path.Combine(Path.GetTempPath(), "KillendarImportTests", Guid.NewGuid().ToString("N"));
            string sourceDir = Path.Combine(directory, "external");
            string dataDir = Path.Combine(directory, "data");
            Directory.CreateDirectory(sourceDir);
            Directory.CreateDirectory(dataDir);
            string source = Path.Combine(sourceDir, "Personal.kcal");
            File.WriteAllText(source, "external calendar");
            File.WriteAllText(Path.Combine(dataDir, "Personal.kcal"), "existing calendar");

            try
            {
                Assert.Equal("Personal-2.kcal", EventStore.ImportKillendarInto(source, dataDir));
                Assert.Equal("external calendar", File.ReadAllText(Path.Combine(dataDir, "Personal-2.kcal")));
                Assert.Equal("existing calendar", File.ReadAllText(Path.Combine(dataDir, "Personal.kcal")));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
