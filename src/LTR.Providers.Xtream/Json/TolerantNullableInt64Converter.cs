using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LTR.Providers.Xtream.Json;

/// <summary>
/// Reads a nullable <see cref="long"/>, treating empty strings and the literal text
/// <c>"null"</c> as absent.
/// </summary>
/// <remarks>
/// Used for Unix timestamps such as <c>exp_date</c>, where an unlimited account is signalled
/// inconsistently as <see langword="null"/>, <c>""</c> or the four characters <c>null</c>.
/// </remarks>
internal sealed class TolerantNullableInt64Converter : JsonConverter<long?>
{
    public override long? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                return reader.TryGetInt64(out var number) ? number : (long)reader.GetDouble();

            case JsonTokenType.String:
            {
                var raw = reader.GetString();
                if (string.IsNullOrWhiteSpace(raw) || string.Equals(raw, "null", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : null;
            }

            case JsonTokenType.Null:
            case JsonTokenType.False:
                return null;

            default:
                throw new JsonException($"Cannot read a nullable integer from a {reader.TokenType} token.");
        }
    }

    public override void Write(Utf8JsonWriter writer, long? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteNumberValue(value.Value);
    }
}
