using System;
using System.IO;
using System.Threading;
using Killendar.Features;
using Killendar.Models;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // SS-008: the backups taken before a password change and before an import are best effort,
    // and a failed one is carried back to the caller so the user is told the safety net is missing.
    [Collection("Calendar settings")]
    public sealed class SafetyBackupReportRegressionTests
    {
        private static CalendarEvent Appointment(string title) => new()
        {
            Title = title,
            Start = new DateTime(2026, 10, 22, 9, 0, 0),
            End = new DateTime(2026, 10, 22, 10, 0, 0),
        };

        // A file where the Backups folder belongs: no backup can be written today.
        private static void BlockBackups(EventStore store) =>
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(store.FilePath)!, "Backups"), "");

        [Fact]
        public void PasswordChangeCarriesAFailedSafetyBackup()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.Add(Appointment("Kept"));
            BlockBackups(fixture.Store);

            var outcome = fixture.Store.SetPassword("pw");

            Assert.True(fixture.Store.HasPassword);
            Assert.False(string.IsNullOrEmpty(outcome.SafetyBackupError));
        }

        [Fact]
        public void PasswordChangeWithAGoodSafetyBackupCarriesNothing()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.Add(Appointment("Kept"));

            Assert.Null(fixture.Store.SetPassword("pw").SafetyBackupError);
        }

        [Fact]
        public void ImportCarriesAFailedSafetyBackup()
        {
            using var fixture = new AppointmentFixture();
            fixture.Store.Add(Appointment("Before"));
            BlockBackups(fixture.Store);
            var plan = fixture.Store.PlanImport([Appointment("Imported")]);

            fixture.Store.WriteImport(plan, CancellationToken.None);

            Assert.False(string.IsNullOrEmpty(plan.BackupError));
        }

        [Fact]
        public void PasswordStatusTellsTheUserTheSafetyBackupFailed()
        {
            using var fixture = new AppointmentFixture();
            var security = new SecurityController(new SecurityHostFake(), fixture.Store);

            string status = security.PasswordStatus(
                new PasswordChangeOutcome(null, false, safetyBackupError: "disk full"), "Str_Status_Encrypted");

            Assert.StartsWith("Str_Status_Encrypted", status);
            Assert.Contains("Str_Status_BackupFailed disk full", status);
        }
    }
}
