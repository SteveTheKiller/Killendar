using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace Killendar.Tests
{
    public sealed class MenuSeparatorThemeTests
    {
        [Fact]
        public void MenuBrushOverrideLeavesGeneralSeparatorsUnchanged()
        {
            var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "Controls", "Controls.xaml"));
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var styles = document.Descendants().Where(element => element.Name.LocalName == "Style").ToList();
            var general = styles.Single(element => (string?)element.Attribute("TargetType") == "Separator"
                && element.Attribute(x + "Key") is null);
            var menu = styles.Single(element => (string?)element.Attribute(x + "Key") == "{x:Static MenuItem.SeparatorStyleKey}");

            Assert.Equal("{DynamicResource MenuBorderBrush}", BackgroundValue(general));
            Assert.Equal("{DynamicResource MenuSeparatorBrush}", BackgroundValue(menu));
            Assert.Equal("{StaticResource {x:Type Separator}}", (string?)menu.Attribute("BasedOn"));
        }

        [Fact]
        public void OnlyDeliriumOverridesTheMenuSeparatorBrush()
        {
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            string[] themes = Directory.GetFiles(Path.Combine(FindRepositoryRoot(), "Themes"), "*.xaml");
            Assert.NotEmpty(themes);
            foreach (string path in themes)
            {
                var document = XDocument.Load(path);
                var brushes = document.Descendants().Where(element =>
                    (string?)element.Attribute(x + "Key") == "MenuSeparatorBrush").ToList();
                if (Path.GetFileName(path) == "Delirium.xaml")
                    Assert.Equal("#666666", (string?)Assert.Single(brushes).Attribute("Color"));
                else
                    Assert.Empty(brushes);
            }
        }

        private static string? BackgroundValue(XElement style)
            => (string?)style.Elements().Single(element =>
                element.Name.LocalName == "Setter" && (string?)element.Attribute("Property") == "Background").Attribute("Value");

        private static string FindRepositoryRoot()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "Killendar.csproj"))) return directory.FullName;
            throw new DirectoryNotFoundException("Could not find the Killendar repository.");
        }
    }
}
