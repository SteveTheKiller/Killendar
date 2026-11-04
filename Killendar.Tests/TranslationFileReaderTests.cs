using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // TranslationFileReader reads a --lang-file as data only. A file is accepted only in the locale
    // file shape with keys the English strings already have.
    public sealed class TranslationFileReaderTests
    {
        private static readonly string[] Known = ["Str_Btn_New", "Str_Btn_Today", "Str_Width"];

        private const string Open =
            "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:sys=\"clr-namespace:System;assembly=mscorlib\">";

        private static Dictionary<string, object> Read(string xml, params string[] keys)
            => TranslationFileReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(xml)), keys.Length == 0 ? Known : keys);

        private static string Dictionary(string body) => Open + body + "</ResourceDictionary>";

        [Fact]
        public void StringAndNumberEntriesAreReadByKey()
        {
            var entries = Read(Dictionary(
                "<!-- a comment -->\n<sys:String x:Key=\"Str_Btn_New\">Neu</sys:String>\n" +
                "<sys:String x:Key=\"Str_Btn_Today\" xml:space=\"preserve\"> Heute </sys:String>\n" +
                "<sys:Double x:Key=\"Str_Width\">42.5</sys:Double>"));

            Assert.Equal(3, entries.Count);
            Assert.Equal("Neu", entries["Str_Btn_New"]);
            Assert.Equal(" Heute ", entries["Str_Btn_Today"]);
            Assert.Equal(42.5, Assert.IsType<double>(entries["Str_Width"]));
        }

        [Fact]
        public void EntityAndCdataTextIsDecoded()
        {
            var entries = Read(Dictionary(
                "<sys:String x:Key=\"Str_Btn_New\">A &amp; B &#x41;</sys:String><sys:String x:Key=\"Str_Btn_Today\"><![CDATA[<b>]]></sys:String>"));

            Assert.Equal("A & B A", entries["Str_Btn_New"]);
            Assert.Equal("<b>", entries["Str_Btn_Today"]);
        }

        [Fact]
        public void AnEmptyDictionaryAndEmptyEntriesAreAccepted()
        {
            Assert.Empty(Read("<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"/>"));
            Assert.Empty(Read(Dictionary("")));
            var entries = Read(Dictionary("<sys:String x:Key=\"Str_Btn_New\"/>"));
            Assert.Equal("", entries["Str_Btn_New"]);
        }

        [Theory]
        [InlineData("clr-namespace:System;assembly=System.Runtime")]
        [InlineData("clr-namespace:System;assembly=netstandard")]
        public void TheOtherSystemAssembliesAreAccepted(string ns)
        {
            string xml = "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:sys=\"" + ns + "\">" +
                "<sys:String x:Key=\"Str_Btn_New\">Neu</sys:String></ResourceDictionary>";

            Assert.Equal("Neu", Read(xml)["Str_Btn_New"]);
        }

        [Theory]
        [InlineData("<Window xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"/>", "root element")]
        [InlineData("<ResourceDictionary xmlns=\"urn:other\"/>", "root element")]
        [InlineData("<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Source=\"evil.xaml\"/>", "Source is not allowed")]
        public void ARootThatIsNotAPlainResourceDictionaryIsRefused(string xml, string expected)
        {
            var error = Assert.Throws<FormatException>(() => Read(xml));
            Assert.Contains(expected, error.Message);
        }

        [Theory]
        [InlineData("<Button x:Key=\"Str_Btn_New\"/>", "Button is not allowed")]
        [InlineData("<sys:Int32 x:Key=\"Str_Btn_New\">1</sys:Int32>", "sys:Int32 is not allowed")]
        [InlineData("<sys:String x:Key=\"Str_Btn_New\" Foreground=\"Red\">x</sys:String>", "attribute Foreground is not allowed")]
        [InlineData("<sys:String>no key</sys:String>", "needs an x:Key")]
        [InlineData("<sys:String x:Key=\"\">no key</sys:String>", "needs an x:Key")]
        [InlineData("<sys:String x:Key=\"Str_Unknown\">x</sys:String>", "Str_Unknown is not in the English strings")]
        [InlineData("<sys:String x:Key=\"Str_Btn_New\">a</sys:String><sys:String x:Key=\"Str_Btn_New\">b</sys:String>", "appears twice")]
        [InlineData("<sys:Double x:Key=\"Str_Width\">wide</sys:Double>", "not a number")]
        [InlineData("<sys:Double x:Key=\"Str_Width\">NaN</sys:Double>", "not a number")]
        [InlineData("<sys:Double x:Key=\"Str_Width\">Infinity</sys:Double>", "not a number")]
        [InlineData("<sys:String x:Key=\"Str_Btn_New\">a<b/>c</sys:String>", "only text")]
        [InlineData("text outside an entry", "Only keyed string and number entries")]
        public void AnythingButAKeyedKnownStringOrNumberIsRefused(string body, string expected)
        {
            var error = Assert.Throws<FormatException>(() => Read(Dictionary(body)));
            Assert.Contains(expected, error.Message);
        }

        [Fact]
        public void AStringEntryInAnotherNamespaceIsRefused()
        {
            string xml = "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
                "xmlns:sys=\"clr-namespace:System.Windows;assembly=PresentationFramework\">" +
                "<sys:String x:Key=\"Str_Btn_New\">Neu</sys:String></ResourceDictionary>";

            var error = Assert.Throws<FormatException>(() => Read(xml));
            Assert.Contains("is not allowed", error.Message);
        }

        [Fact]
        public void ADoctypeIsNeverProcessed()
        {
            string xml = "<!DOCTYPE ResourceDictionary [<!ENTITY x \"expanded\">]>" +
                Dictionary("<sys:String x:Key=\"Str_Btn_New\">&x;</sys:String>");

            // The DTD is prohibited outright, so no entity can be declared or expanded.
            Assert.ThrowsAny<Exception>(() => Read(xml));
        }

        [Fact]
        public void AFileOverTheSizeLimitIsRefusedBeforeItIsParsed()
        {
            var stream = new MemoryStream(new byte[TranslationFileReader.MaximumFileBytes + 1]);

            var error = Assert.Throws<FormatException>(() => TranslationFileReader.Read(stream, Known));
            Assert.Equal("The translation file is too large.", error.Message);
        }

        [Fact]
        public void AFileThatIsNotXmlIsRefused()
        {
            Assert.ThrowsAny<XmlException>(() => Read("this is not xml"));
        }
    }
}
