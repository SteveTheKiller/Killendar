using System;
using System.IO;
using Xunit;

namespace Killendar.Tests
{
    // VE-007: Escape mid-drag snaps the chip home. The window's own Escape handling (close the
    // sidebar, leave a panel) must not run for that same key, so the cancel comes first.
    public sealed class DragEscapeRoutingTests
    {
        [Fact]
        public void EscapeCancelsADragBeforeAnyWindowShortcut()
        {
            string? root = AppContext.BaseDirectory;
            while (root != null && !File.Exists(Path.Combine(root, "Killendar.csproj"))) root = Path.GetDirectoryName(root);
            Assert.NotNull(root);
            string source = File.ReadAllText(Path.Combine(root!, "Shell", "Shortcuts.cs"));

            int handler = source.IndexOf("private void Window_PreviewKeyDown(", StringComparison.Ordinal);
            Assert.True(handler > 0, "Shortcuts.cs has the window key handler.");
            int body = source.IndexOf('{', handler) + 1;
            int firstStatement = source.IndexOf(';', body);
            Assert.Contains("DragCommit.TryCancel()", source.Substring(body, firstStatement - body));
        }
    }
}
