using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LTR.Providers.Xtream.Json;

/// <summary>
/// Reads a string that a panel may have encoded as a number or a boolean.
/// </summary>
/// <remarks>
/// Identifier fields are the usual offenders: <c>category_id</c> arrives as <c>"12"</c> from most
/// panels and as <c>12</c> from others, and both must land in the same property.
/// </remarks>
internal sealed class TolerantStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return reader.GetString();

            case JsonTokenType.Number:
                return reader.TryGetInt64(out var number)
                    ? number.ToString(CultureInfo.InvariantCulture)
                    : reader.GetDouble().ToString(CultureInfo.InvariantCulture);

            case JsonTokenType.True:
                return "1";

            case JsonTokenType.False:
                return "0";

            case JsonTokenType.Null:
                return null;

            default:
                throw new JsonException($"Cannot read a string from a {reader.TokenType} token.");
        }
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value);
    }
}
