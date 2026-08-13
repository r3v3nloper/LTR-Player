namespace LTR.Core.Sources;

public sealed class SourceAddressTests
{
    [Theory]
    [InlineData("http://host:8080")]
    [InlineData("http://host/list.m3u?user=u&pass=p")]
    [InlineData("https://host/guide.xml")]
    public void TryParse_AcceptsAbsoluteUrls(string value)
    {
        // Arrange & Act
        var parsed = SourceAddress.TryParse(value, out var address);

        // Assert
        parsed.ShouldBeTrue();
        address.AbsoluteUri.ShouldStartWith("http");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("not an address")]
    [InlineData("host:8080")]
    [InlineData("ftp://host/list.m3u")]
    public void TryParse_RejectsWhatIsNeitherWebAddressNorExistingFile(string? value)
    {
        // Arrange: "host:8080" is the trap — it parses as an absolute URI whose scheme is "host", so a
        // check that only asks whether parsing succeeded would accept a half-typed address.
        // Act
        var parsed = SourceAddress.TryParse(value, out _);

        // Assert
        parsed.ShouldBeFalse();
    }

    [Theory]
    [InlineData("http://panel.example:8080", true)]
    [InlineData("https://panel.example", true)]
    [InlineData("panel.example:8080", false)]
    [InlineData("panel.example", false)]
    [InlineData("", false)]
    public void TryParseWebAddress_AcceptsOnlyHttpAddresses(string value, bool expected)
    {
        // Arrange: a panel endpoint can only be a web address, so a file path must not pass here.
        // Act
        var parsed = SourceAddress.TryParseWebAddress(value, out _);

        // Assert
        parsed.ShouldBe(expected);
    }

    [Fact]
    public void TryParseWebAddress_RejectsALocalFile()
    {
        // Arrange
        var path = Path.Combine(Path.GetTempPath(), $"ltr-address-{Guid.NewGuid():N}.m3u");
        File.WriteAllText(path, "#EXTM3U\n");

        try
        {
            // Act
            var parsed = SourceAddress.TryParseWebAddress(path, out _);

            // Assert
            parsed.ShouldBeFalse();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryParse_AcceptsAnExistingLocalPath()
    {
        // Arrange: what a user pastes after being sent a playlist file.
        var path = Path.Combine(Path.GetTempPath(), $"ltr-address-{Guid.NewGuid():N}.m3u");
        File.WriteAllText(path, "#EXTM3U\n");

        try
        {
            // Act
            var parsed = SourceAddress.TryParse(path, out var address);

            // Assert
            parsed.ShouldBeTrue();
            address.IsFile.ShouldBeTrue();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryParse_RejectsAPathThatDoesNotExist()
    {
        // Arrange: a mistyped path accepted here would only fail later, when the source is first used.
        var path = Path.Combine(Path.GetTempPath(), "ltr-definitely-missing.m3u");

        // Act
        var parsed = SourceAddress.TryParse(path, out _);

        // Assert
        parsed.ShouldBeFalse();
    }

    [Fact]
    public void Describe_UsesTheHostForRemoteAddresses()
    {
        // Arrange & Act
        var label = SourceAddress.Describe(new Uri("http://panel.example:8080/get.php?u=a"));

        // Assert
        label.ShouldBe("panel.example");
    }

    [Fact]
    public void Describe_UsesTheFileNameForLocalPaths()
    {
        // Arrange & Act
        var label = SourceAddress.Describe(new Uri(@"C:\playlists\germany.m3u"));

        // Assert
        label.ShouldBe("germany.m3u");
    }
}
