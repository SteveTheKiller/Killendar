using Killendar.Services;
using Xunit;

namespace Killendar.Tests
{
    // CodeSignature.SamePublisher decides whether two certificate subjects name one publisher. It sits
    // under every update decision, so each accept and reject shape is pinned here.
    public sealed class CodeSignatureTests
    {
        [Theory]
        [InlineData("Open Source Developer Stephen Riley", "Open Source Developer Stephen Riley")]
        [InlineData("Open Source Developer Stephen Riley", "open source developer stephen riley")]
        [InlineData("  Open Source Developer Stephen Riley ", "Open Source Developer Stephen Riley")]
        [InlineData("Contoso Ltd", "CONTOSO LTD")]
        public void SubjectsThatNameTheSamePublisherMatch(string current, string candidate)
        {
            Assert.True(CodeSignature.SamePublisher(current, candidate));
            Assert.True(CodeSignature.SamePublisher(candidate, current));
        }

        [Theory]
        [InlineData("Open Source Developer Stephen Riley", "Stephen Riley")]
        [InlineData("Stephen Riley", "Open Source Developer Stephen Riley")]
        [InlineData("Open Source Developer Stephen Riley", "Open Source Developer Stephen Rileyson")]
        [InlineData("Contoso Ltd", "Contoso Ltd.")]
        [InlineData("Contoso Ltd", "Fabrikam Inc")]
        public void DifferentOrPartialNamesDoNotMatch(string current, string candidate)
        {
            Assert.False(CodeSignature.SamePublisher(current, candidate));
            Assert.False(CodeSignature.SamePublisher(candidate, current));
        }

        [Theory]
        [InlineData(null, null)]
        [InlineData("", "")]
        [InlineData("   ", "   ")]
        [InlineData(null, "Contoso Ltd")]
        [InlineData("Contoso Ltd", null)]
        [InlineData("", "Contoso Ltd")]
        [InlineData("Contoso Ltd", "")]
        public void AnEmptySubjectNeverMatches(string? current, string? candidate)
        {
            Assert.False(CodeSignature.SamePublisher(current, candidate));
        }

        [Theory]
        [InlineData("(not signed)")]
        [InlineData("(unavailable)")]
        public void APlaceholderIsNotAPublisherEvenAgainstItself(string placeholder)
        {
            Assert.False(CodeSignature.SamePublisher(placeholder, placeholder));
            Assert.False(CodeSignature.SamePublisher(placeholder, "Contoso Ltd"));
        }
    }
}
