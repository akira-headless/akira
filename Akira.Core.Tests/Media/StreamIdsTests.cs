using Akira.Core.Enum;
using Akira.Core.Media;
using Xunit;

namespace Akira.Core.Tests.Media;

public class StreamIdsTests
{
    private static readonly Guid File = Guid.Parse("01a196ce-263c-7c0d-954d-f08450b4d2da");
    private static readonly string English = StreamIds.Signature("eng", "ac3", 6, false, false, false, false, "English 5.1");

    [Fact]
    public void Embedded_IsDeterministic()
    {
        Assert.Equal(StreamIds.Embedded(File, StreamKind.Audio, English, 0), StreamIds.Embedded(File, StreamKind.Audio, English, 0));
        Assert.Equal(32, StreamIds.Embedded(File, StreamKind.Audio, English, 0).Length);
    }

    [Fact]
    public void Embedded_DependsOnFileKindOrdinalAndSignature()
    {
        var id = StreamIds.Embedded(File, StreamKind.Audio, English, 0);

        Assert.NotEqual(id, StreamIds.Embedded(Guid.NewGuid(), StreamKind.Audio, English, 0));
        Assert.NotEqual(id, StreamIds.Embedded(File, StreamKind.Subtitle, English, 0));
        Assert.NotEqual(id, StreamIds.Embedded(File, StreamKind.Audio, English, 1));
        Assert.NotEqual(id, StreamIds.Embedded(File, StreamKind.Audio,
            StreamIds.Signature("eng", "ac3", 6, false, false, true, false, "English 5.1"), 0));
    }

    [Fact]
    public void Signature_IgnoresTitleCaseAndSpacing()
    {
        Assert.Equal(English, StreamIds.Signature("eng", "AC3", 6, false, false, false, false, "  english   5.1 "));
    }

    [Fact]
    public void External_DependsOnFileAndName()
    {
        var id = StreamIds.External(File, "Movie.nl.srt");

        Assert.Equal(id, StreamIds.External(File, "Movie.nl.srt"));
        Assert.NotEqual(id, StreamIds.External(File, "Movie.en.srt"));
        Assert.NotEqual(id, StreamIds.External(Guid.NewGuid(), "Movie.nl.srt"));
    }
}
