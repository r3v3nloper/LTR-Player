namespace LTR.Core.Content;

public sealed class StreamFormatExtensionsTests
{
    [Theory]
    [InlineData(StreamFormat.MpegTs, "ts")]
    [InlineData(StreamFormat.HlsPlaylist, "m3u8")]
    public void ToUrlExtension_MapsEachFormatToItsWireExtension(StreamFormat format, string expected)
    {
        // Arrange & Act
        var extension = format.ToUrlExtension();

        // Assert
        extension.ShouldBe(expected);
    }

    [Fact]
    public void ToUrlExtension_ForAnUndefinedFormat_IsRejected()
    {
        // Arrange: a value cast from an int, as could arrive from stale persisted data.
        var format = (StreamFormat)99;

        // Act
        var act = () => format.ToUrlExtension();

        // Assert
        act.ShouldThrow<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ToUrlExtension_ForAProgressiveFile_SaysTheContainerIsThePanels()
    {
        // Arrange: no caller passes this today, so what the method does with it is only ever discovered by
        // the caller that eventually does. The message is the point — it says what to use instead.
        // Act
        var act = () => StreamFormat.ProgressiveFile.ToUrlExtension();

        // Assert
        var exception = act.ShouldThrow<ArgumentOutOfRangeException>();
        exception.Message.ShouldContain("container");
        exception.Message.ShouldNotContain("Unknown");
    }

    [Theory]
    [InlineData("ts", StreamFormat.MpegTs)]
    [InlineData("TS", StreamFormat.MpegTs)]
    [InlineData("  ts  ", StreamFormat.MpegTs)]
    [InlineData("m3u8", StreamFormat.HlsPlaylist)]
    [InlineData("hls", StreamFormat.HlsPlaylist)]
    public void FromProviderFormatName_RecognisesTheNamesPanelsReport(string name, StreamFormat expected)
    {
        // Arrange & Act
        var format = StreamFormatExtensions.FromProviderFormatName(name);

        // Assert
        format.ShouldBe(expected);
    }

    [Theory]
    [InlineData("rtmp")]
    [InlineData("rtsp")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void FromProviderFormatName_IgnoresFormatsThisPlayerDoesNotHandle(string? name)
    {
        // Arrange: rtmp appears in allowed_output_formats routinely and must not be selected.
        // Act
        var format = StreamFormatExtensions.FromProviderFormatName(name);

        // Assert
        format.ShouldBeNull();
    }
}
