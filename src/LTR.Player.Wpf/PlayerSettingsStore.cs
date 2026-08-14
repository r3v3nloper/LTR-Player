using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace LTR.Player.Wpf;

/// <summary>
/// Reads and writes the settings file.
/// </summary>
/// <remarks>
/// <para>
/// Both operations refuse to fail. Settings are a convenience, and a player that will not start because its
/// preferences file has a stray comma in it is worse than one that starts with the defaults and says so in
/// the log — the same reasoning that quarantines an unreadable catalogue rather than repairing it.
/// </para>
/// <para>
/// Written through a temporary file and moved into place, so a crash or a full disk mid-write leaves the
/// previous settings intact rather than a truncated document that then reads as corrupt.
/// </para>
/// </remarks>
public sealed class PlayerSettingsStore : IPlayerSettingsStore
{
    /// <summary>
    /// Indented, with enums as names.
    /// </summary>
    /// <remarks>
    /// The file being readable and editable by hand is half the reason it is a file, so it is written for a
    /// person: <c>"HardwareDecoding": "Disabled"</c> rather than <c>2</c>.
    /// </remarks>
    private static readonly JsonSerializerOptions Format = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly ILogger<PlayerSettingsStore> _logger;

    public PlayerSettingsStore(string path, ILogger<PlayerSettingsStore> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(logger);

        _path = path;
        _logger = logger;
    }

    /// <summary>
    /// The stored settings, or the defaults when there are none or they cannot be read.
    /// </summary>
    public PlayerSettings Load()
    {
        if (!File.Exists(_path))
        {
            // The ordinary case on a first run, and not worth a log line.
            return new PlayerSettings();
        }

        try
        {
            var stored = JsonSerializer.Deserialize<PlayerSettings>(File.ReadAllText(_path), Format);

            // Deserialising the literal "null" succeeds and yields nothing, which is a file worth replacing
            // rather than a state worth propagating.
            return stored ?? new PlayerSettings();
        }
        catch (Exception exception) when (exception is JsonException or IOException
            or UnauthorizedAccessException)
        {
            PlayerLog.SettingsNotRead(_logger, exception, _path);
            return new PlayerSettings();
        }
    }

    /// <summary>Stores the settings, and reports in the log if it could not.</summary>
    public void Save(PlayerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var temporaryPath = _path + ".tmp";

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, Format));
            File.Move(temporaryPath, _path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            PlayerLog.SettingsNotSaved(_logger, exception, _path);
        }
    }
}
