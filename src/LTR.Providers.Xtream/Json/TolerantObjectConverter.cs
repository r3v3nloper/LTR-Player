using System.Text.Json;
using System.Text.Json.Serialization;

namespace LTR.Providers.Xtream.Json;

/// <summary>
/// Reads a nested object, treating any other shape as absent instead of failing.
/// </summary>
/// <remarks>
/// <para>
/// PHP is the reason this exists. An empty associative array and an empty list are the same value in
/// PHP, so a panel with nothing to say about a film answers <c>"info": []</c> rather than
/// <c>"info": null</c> — and a typed property would throw on it, turning "this film has no synopsis"
/// into a failed detail call.
/// </para>
/// <para>
/// Applied per property rather than registered globally, which is also what keeps it from recursing:
/// the nested deserialisation looks the converter up in the options, where this one is not.
/// </para>
/// </remarks>
internal sealed class TolerantObjectConverter<T> : JsonConverter<T?>
    where T : class
{
    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            return JsonSerializer.Deserialize<T>(ref reader, options);
        }

        // Consumes the whole of an array or object; a scalar token is already complete.
        if (reader.TokenType is JsonTokenType.StartArray)
        {
            reader.Skip();
        }

        return null;
    }

    public override void Write(Utf8JsonWriter writer, T? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        JsonSerializer.Serialize(writer, value, options);
    }
}
