using Killendar.Features;
using Xunit;

namespace Killendar.Tests
{
    // IE-001: a repeat whose time zone cannot be written is exported on UTC's clock instead of
    // failing the export, and the status line says how many were.
    public sealed class ExportZoneFallbackStatusTests
    {
        [Fact]
        public void AnExportWithoutFallbacksReportsOnlyTheCount()
        {
            var host = new EnglishCalendarHost();
            Assert.Equal("Exported 12 appointments", IcsTransfer.ExportedStatus(host.Loc, 12, 0));
        }

        [Fact]
        public void FallbacksToUtcAreSaid()
        {
            var host = new EnglishCalendarHost();
            Assert.Equal("Exported 12 appointments. 2 repeating appointments were written in UTC because their time zone could not be exported",
                IcsTransfer.ExportedStatus(host.Loc, 12, 2));
        }
    }
}
