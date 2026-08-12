using System.Text.Json;
using System.Text.Json.Serialization;

namespace LTR.Providers.Xtream.Json;

/// <summary>
/// Reads a <see cref="bool"/> from the several shapes panels use for flags: <c>true</c>, <c>1</c>,
/// <c>"1"</c> and <c>"true"</c> all mean set.
/// </summary>
internal sealed class TolerantBooleanConverter : JsonConverter<bool>
{
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.True:
                return true;

            case JsonTokenType.False:
            case JsonTokenType.Null:
                return false;

            case JsonTokenType.Number:
                return reader.TryGetInt64(out var number) && number != 0;

            case JsonTokenType.String:
            {
                var raw = reader.GetString();
                if (string.IsNullOrWhiteSpace(raw))
                {
                    return false;
                }

                if (bool.TryParse(raw, out var parsedBoolean))
                {
                    return parsedBoolean;
                }

                return long.TryParse(raw, out var parsedNumber) && parsedNumber != 0;
            }

            default:
                throw new JsonException($"Cannot read a boolean from a {reader.TokenType} token.");
        }
    }

    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
    {
        writer.WriteBooleanValue(value);
    }
}
