using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>
    /// A bare DragMove() throws InvalidOperationException when the button is already up by the
    /// time it runs (a touch tap or a fast click), and nothing marks that exception handled, so
    /// the app exits. Every title-bar press under Controls/ goes through TitleBarDrag.Start.
    /// </summary>
    public sealed class TitleBarDragRegressionTests
    {
        [Fact]
        public void NoDialogStartsABareDragMoveFromItsTitleBar()
        {
            string controls = Path.Combine(Root(), "Controls");
            var bare = Directory.GetFiles(controls, "*.cs")
                .SelectMany(file => Regex.Matches(File.ReadAllText(file), @"=>\s*DragMove\(\);")
                    .Cast<Match>().Select(_ => Path.GetFileName(file)))
                .ToList();
            Assert.True(bare.Count == 0, "Bare DragMove() title-bar handlers remain in: " + string.Join(", ", bare));
        }

        [Fact]
        public void TitleBarDragLivesInItsOwnFile()
            => Assert.True(File.Exists(Path.Combine(Root(), "Controls", "TitleBarDrag.cs")),
                "Controls/TitleBarDrag.cs does not exist.");

        // From this file's own path, so the check works wherever the test build lands.
        private static string Root([System.Runtime.CompilerServices.CallerFilePath] string source = "")
        {
            string root = Path.GetDirectoryName(Path.GetDirectoryName(source))!;
            if (!Directory.Exists(Path.Combine(root, "Controls")))
                throw new InvalidOperationException("Controls/ was not found beside the test project.");
            return root;
        }
    }
}
