using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace Killendar.Tests
{
    /// <summary>A button face never carries a drop shadow: no button style may put an Effect on a
    /// Border in its template, directly or through a trigger. The selected view tab did
    /// (AppStyles.xaml ViewTabButton), which is the family rule broken.</summary>
    public sealed class ButtonFaceShadowTests
    {
        [Theory]
        [InlineData("Controls.xaml")]
        [InlineData("AppStyles.xaml")]
        public void NoButtonStyleShadowsItsFace(string file)
        {
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var xaml = XDocument.Load(Path.Combine(Root(), "Controls", file));
            var offenders = xaml.Descendants().Where(e => e.Name.LocalName == "Style")
                .Where(s => ((string?)s.Attribute("TargetType") ?? "").EndsWith("Button", StringComparison.Ordinal))
                .SelectMany(style =>
                {
                    var borders = style.Descendants().Where(e => e.Name.LocalName == "Border")
                        .Select(e => (string?)e.Attribute(x + "Name")).Where(n => n != null).ToHashSet();
                    string key = (string?)style.Attribute(x + "Key") ?? "(implicit)";
                    var viaSetter = style.Descendants().Where(e => e.Name.LocalName == "Setter"
                            && (string?)e.Attribute("Property") == "Effect"
                            && borders.Contains((string?)e.Attribute("TargetName")))
                        .Select(_ => key);
                    var inline = style.Descendants().Where(e => e.Name.LocalName == "Border.Effect").Select(_ => key);
                    return viaSetter.Concat(inline);
                })
                .Distinct().ToList();
            Assert.Empty(offenders);
        }

        private static string Root()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "Killendar.csproj"))) return dir.FullName;
            throw new InvalidOperationException("Killendar.csproj was not found above the test output.");
        }
    }
}
