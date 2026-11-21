using Akira.Core.Media;
using Xunit;

namespace Akira.Core.Tests.Media;

public class TrackTitlesTests
{
    [Theory]
    [InlineData("ENG SDH", true)]
    [InlineData("English (CC)", true)]
    [InlineData("English [HI]", true)]
    [InlineData("Hearing Impaired", true)]
    [InlineData("Hi-Score Girl", false)]
    [InlineData("ENG", false)]
    [InlineData(null, false)]
    public void IsHearingImpaired(string? title, bool expected) => Assert.Equal(expected, TrackTitles.IsHearingImpaired(title));

    [Theory]
    [InlineData("Director's Commentary", true)]
    [InlineData("commentary with cast", true)]
    [InlineData("English 5.1", false)]
    public void IsCommentary(string title, bool expected) => Assert.Equal(expected, TrackTitles.IsCommentary(title));

    [Theory]
    [InlineData("English (Forced)", true)]
    [InlineData("Forced Narrative", true)]
    [InlineData("Signs & Songs", false)]
    [InlineData("Enforcement", false)]
    public void IsForced(string title, bool expected) => Assert.Equal(expected, TrackTitles.IsForced(title));

    [Theory]
    [InlineData("English Audio Description", true)]
    [InlineData("Descriptive Video Service", true)]
    [InlineData("English", false)]
    public void IsDescriptive(string title, bool expected) => Assert.Equal(expected, TrackTitles.IsDescriptive(title));
}
