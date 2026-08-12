using System.Text.Json;
using System.Text.Json.Serialization;

namespace LTR.Providers.Xtream.Json;

/// <summary>
/// Covers the shapes panels actually emit for scalar fields. Every case here was chosen because it
/// occurs in the wild, not because it is theoretically possible.
/// </summary>
public sealed class TolerantConverterTests
{
    [Theory]
    [InlineData("""{"value":42}""", 42)]
    [InlineData("""{"value":"42"}""", 42)]
    [InlineData("""{"value":""}""", 0)]
    [InlineData("""{"value":null}""", 0)]
    [InlineData("""{"value":"not a number"}""", 0)]
    [InlineData("""{"value":true}""", 1)]
    [InlineData("""{"value":false}""", 0)]
    public void Int32_IsReadFromEveryShapePanelsUse(string json, int expected)
    {
        // Arrange & Act
        var result = Deserialize<Int32Holder>(json);

        // Assert
        result.Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData("""{"value":1771000000}""", 1771000000L)]
    [InlineData("""{"value":"1771000000"}""", 1771000000L)]
    [InlineData("""{"value":null}""", null)]
    [InlineData("""{"value":""}""", null)]
    [InlineData("""{"value":"null"}""", null)]
    [InlineData("""{"value":false}""", null)]
    public void NullableInt64_TreatsEveryAbsentFormAsNull(string json, long? expected)
    {
        // Arrange & Act
        var result = Deserialize<NullableInt64Holder>(json);

        // Assert
        result.Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData("""{"value":true}""", true)]
    [InlineData("""{"value":false}""", false)]
    [InlineData("""{"value":1}""", true)]
    [InlineData("""{"value":0}""", false)]
    [InlineData("""{"value":"1"}""", true)]
    [InlineData("""{"value":"0"}""", false)]
    [InlineData("""{"value":"true"}""", true)]
    [InlineData("""{"value":""}""", false)]
    [InlineData("""{"value":null}""", false)]
    public void Boolean_IsReadFromNumbersAndStringsAlike(string json, bool expected)
    {
        // Arrange & Act
        var result = Deserialize<BooleanHolder>(json);

        // Assert
        result.Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData("""{"value":"12"}""", "12")]
    [InlineData("""{"value":12}""", "12")]
    [InlineData("""{"value":null}""", null)]
    public void String_AcceptsNumericIdentifiers(string json, string? expected)
    {
        // Arrange: category_id arrives quoted from most panels and bare from others.
        // Act
        var result = Deserialize<StringHolder>(json);

        // Assert
        result.Value.ShouldBe(expected);
    }

    [Fact]
    public void LiveStreamDto_IsReadFromAnAllStringPayload()
    {
        // Arrange: a real response from a panel that quotes every value.
        const string json = """
            {
              "num": "7",
              "name": "Sport 1 HD",
              "stream_id": "1234",
              "stream_icon": "http://host/logo.png",
              "epg_channel_id": "sport1.de",
              "category_id": "5",
              "tv_archive": "1",
              "tv_archive_duration": "7"
            }
            """;

        // Act
        var dto = JsonSerializer.Deserialize<Dtos.XtreamLiveStreamDto>(json, XtreamJson.Options);

        // Assert
        dto.ShouldNotBeNull();
        dto.Number.ShouldBe(7);
        dto.StreamId.ShouldBe("1234");
        dto.EpgChannelId.ShouldBe("sport1.de");
        dto.CategoryId.ShouldBe("5");
        dto.HasArchive.ShouldBeTrue();
        dto.ArchiveDurationDays.ShouldBe(7);
    }

    [Fact]
    public void LiveStreamDto_IsReadFromAnAllNumericPayload()
    {
        // Arrange: the same fields from a panel that quotes nothing.
        const string json = """
            {
              "num": 7,
              "name": "Sport 1 HD",
              "stream_id": 1234,
              "category_id": 5,
              "tv_archive": 0,
              "tv_archive_duration": 0
            }
            """;

        // Act
        var dto = JsonSerializer.Deserialize<Dtos.XtreamLiveStreamDto>(json, XtreamJson.Options);

        // Assert
        dto.ShouldNotBeNull();
        dto.StreamId.ShouldBe("1234");
        dto.CategoryId.ShouldBe("5");
        dto.HasArchive.ShouldBeFalse();
    }

    private static T Deserialize<T>(string json)
        where T : class
    {
        var result = JsonSerializer.Deserialize<T>(json, XtreamJson.Options);
        result.ShouldNotBeNull();
        return result;
    }

    private sealed class Int32Holder
    {
        [JsonPropertyName("value")]
        public int Value { get; set; }
    }

    private sealed class NullableInt64Holder
    {
        [JsonPropertyName("value")]
        public long? Value { get; set; }
    }

    private sealed class BooleanHolder
    {
        [JsonPropertyName("value")]
        public bool Value { get; set; }
    }

    private sealed class StringHolder
    {
        [JsonPropertyName("value")]
        public string? Value { get; set; }
    }
}
