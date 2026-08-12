using System.Text.Json;
using System.Text.Json.Serialization;

namespace LTR.Providers.Xtream.Json;

/// <summary>
/// Deserialisation settings for Xtream panel responses.
/// </summary>
/// <remarks>
/// The tolerant converters are registered globally rather than attributed onto individual
/// properties: every scalar a panel emits can arrive in more than one shape, so the tolerance
/// belongs to the protocol rather than to particular fields.
/// </remarks>
internal static class XtreamJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        options.Converters.Add(new TolerantInt32Converter());
        options.Converters.Add(new TolerantNullableInt64Converter());
        options.Converters.Add(new TolerantBooleanConverter());
        options.Converters.Add(new TolerantStringConverter());

        // populateMissingResolver installs the reflection-based resolver. Without it, freezing the
        // options throws, because no resolver was configured explicitly.
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
