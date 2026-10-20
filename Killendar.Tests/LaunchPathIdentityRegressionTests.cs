using System;
using System.IO;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    public sealed class LaunchPathIdentityRegressionTests
    {
        [Theory]
        [InlineData("calendar.kcal", true)]
        [InlineData("invite.ics", true)]
        [InlineData("calendar.kcal", false)]
        [InlineData("INVITE.ICS", false)]
        public void CapturedLaunchPathPreservesCanonicalFileIdentity(string fileName, bool relative)
        {
            string directory = Path.Combine(@"C:\Users\steve\scratch\killendar-remediation\paths",
                Guid.NewGuid().ToString("N"));
            string sender = Path.Combine(directory, "sender");
            string receiver = Path.Combine(directory, "receiver");
            Directory.CreateDirectory(Path.Combine(sender, "child"));
            Directory.CreateDirectory(receiver);
            string requested = Path.Combine(sender, fileName);
            File.WriteAllText(requested, "sender calendar");
            File.WriteAllText(Path.Combine(receiver, fileName), "receiver calendar");
            try
            {
                string argument = Path.Combine(sender, ".", "child", "..", fileName);
                if (relative)
                {
                    var current = new Uri(Directory.GetCurrentDirectory().TrimEnd('\\') + "\\");
                    argument = Uri.UnescapeDataString(current.MakeRelativeUri(new Uri(sender + "\\"))
                        .ToString()).Replace('/', '\\') + @".\child\..\" + fileName;
                    Assert.False(Path.IsPathRooted(argument));
                }

                string? forwarded = FileAssociations.CalendarPathFrom([argument]);

                Assert.Equal(requested, forwarded);
                Assert.Equal(requested, FileAssociations.CalendarPathFrom([forwarded!]));
                Assert.Equal("sender calendar", File.ReadAllText(forwarded!));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
