using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LTR.Providers.Xtream.Json;

/// <summary>
/// Reads a nullable <see cref="double"/>, treating anything unparseable as absent.
/// </summary>
/// <remarks>
/// Ratings are what this exists for. The same field arrives as <c>7.5</c>, <c>"7.5"</c>, <c>"0"</c>,
/// <c>""</c>, <c>"N/A"</c> or <c>null</c> depending on the panel and on whether the film was ever
/// rated. Absent is the honest reading of all but the first two, and a rating is never worth failing
/// an entire catalogue over.
/// </remarks>
internal sealed class TolerantNullableDoubleConverter : JsonConverter<double?>
{
    public override double? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                return reader.GetDouble();

            case JsonTokenType.String:
            {
                var raw = reader.GetString();

                if (string.IsNullOrWhiteSpace(raw))
                {
                    return null;
                }

                // Invariant only, deliberately. A panel writing "7,5" means seven and a half under a
                // German locale and seventy-five under none; guessing wrong is worse than no rating.
                return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : null;
            }

            case JsonTokenType.Null:
            case JsonTokenType.False:
                return null;

            default:
                throw new JsonException($"Cannot read a nullable number from a {reader.TokenType} token.");
        }
    }

    public override void Write(Utf8JsonWriter writer, double? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteNumberValue(value.Value);
    }
}
