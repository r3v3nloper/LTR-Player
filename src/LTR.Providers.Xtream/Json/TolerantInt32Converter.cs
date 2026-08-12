using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LTR.Providers.Xtream.Json;

/// <summary>
/// Reads an <see cref="int"/> that a panel may have encoded as a number, a string, a boolean or
/// an empty value.
/// </summary>
/// <remarks>
/// Xtream panels are PHP applications with no serialisation contract, so the same field arrives as
/// <c>5</c> from one panel and <c>"5"</c> from another, and as <c>""</c> or <c>null</c> when unset.
/// Unparseable input yields zero rather than throwing: a single odd field must not cost the user
/// their entire channel list.
/// </remarks>
internal sealed class TolerantInt32Converter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                return reader.TryGetInt32(out var number) ? number : (int)reader.GetDouble();

            case JsonTokenType.String:
                return int.TryParse(reader.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out var parsed)
                    ? parsed
                    : 0;

            case JsonTokenType.True:
                return 1;

            case JsonTokenType.False:
            case JsonTokenType.Null:
                return 0;

            default:
                throw new JsonException($"Cannot read an integer from a {reader.TokenType} token.");
        }
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value);
    }
}
