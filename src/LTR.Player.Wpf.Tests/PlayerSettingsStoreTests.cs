using System.IO;
using LTR.Playback;
using LTR.Playback.LibVlc;
using Microsoft.Extensions.Logging.Abstractions;

namespace LTR.Player.Wpf;

/// <summary>
/// Covers reading and writing the settings file.
/// </summary>
/// <remarks>
/// The interesting cases are all failures, and all of them are the same rule: settings are a convenience, so
/// nothing here may stop the player starting. A hardening milestone that shipped a player refusing to open
/// because its preferences file had a stray comma in it would have missed the point entirely.
/// </remarks>
public sealed class PlayerSettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"ltr-player-settings-{Guid.NewGuid():N}");

    [Fact]
    public void WithNoFileYet_TheDefaultsAreUsed()
    {
        // Arrange: the first run, which must not be an event.
        var store = Create();

        // Act
        var settings = store.Load();

        // Assert
        settings.Player.Volume.ShouldBe(100);
        settings.Playback.LiveNetworkCachingMilliseconds
            .ShouldBe(new LibVlcOptions().LiveNetworkCachingMilliseconds);
    }

    [Fact]
    public void WhatWasSaved_IsWhatComesBack()
    {
        // Arrange
        var store = Create();
        var settings = store.Load();

        settings.Player.Volume = 35;
        settings.Player.IsMuted = true;
        settings.Player.AspectRatio = VideoAspectRatio.Standard;
        settings.Playback.LiveNetworkCachingMilliseconds = 1500;
        settings.Playback.HardwareDecoding = HardwareDecoding.Disabled;

        // Act
        store.Save(settings);
        var reloaded = Create().Load();

        // Assert
        reloaded.Player.Volume.ShouldBe(35);
        reloaded.Player.IsMuted.ShouldBeTrue();
        reloaded.Player.AspectRatio.ShouldBe(VideoAspectRatio.Standard);
        reloaded.Playback.LiveNetworkCachingMilliseconds.ShouldBe(1500);
        reloaded.Playback.HardwareDecoding.ShouldBe(HardwareDecoding.Disabled);
    }

    /// <remarks>
    /// The file being editable by hand is half the reason it is a file rather than a table, and a viewer
    /// cannot edit <c>"HardwareDecoding": 2</c> into anything they can be sure of.
    /// </remarks>
    [Fact]
    public void TheFileIsWrittenForAPersonToRead()
    {
        // Arrange
        var store = Create();
        var settings = store.Load();
        settings.Playback.HardwareDecoding = HardwareDecoding.Disabled;

        // Act
        store.Save(settings);
        var written = File.ReadAllText(SettingsPath);

        // Assert
        written.ShouldContain("\"HardwareDecoding\": \"Disabled\"");
        written.ShouldContain(Environment.NewLine, Case.Sensitive, "indented, not one line");
    }

    [Fact]
    public void ACorruptFile_LeavesThePlayerOnTheDefaults()
    {
        // Arrange
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, "{ \"Player\": { \"Volume\": ");

        // Act
        var settings = Create().Load();

        // Assert
        settings.Player.Volume.ShouldBe(100);
    }

    /// <remarks>
    /// The file is deliberately not replaced or deleted on a failed read. Whatever is wrong with it is worth
    /// being able to look at — the same reasoning that keeps a quarantined catalogue rather than removing it.
    /// </remarks>
    [Fact]
    public void ACorruptFile_IsLeftWhereItIs()
    {
        // Arrange
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, "not json at all");

        // Act
        Create().Load();

        // Assert
        File.ReadAllText(SettingsPath).ShouldBe("not json at all");
    }

    [Fact]
    public void AFileHoldingNull_IsTreatedAsNoFile()
    {
        // Arrange: deserialising the literal null succeeds and yields nothing, which is the one shape a
        // tolerant reader still has to notice.
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, "null");

        // Act
        var settings = Create().Load();

        // Assert
        settings.ShouldNotBeNull();
        settings.Player.Volume.ShouldBe(100);
    }

    [Fact]
    public void AnUnknownSetting_IsIgnoredRatherThanFatal()
    {
        // Arrange: what a file written by a later version of the player looks like to this one.
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            SettingsPath,
            "{ \"Player\": { \"Volume\": 42 }, \"SomethingFromTheFuture\": { \"Nested\": true } }");

        // Act
        var settings = Create().Load();

        // Assert
        settings.Player.Volume.ShouldBe(42);
    }

    [Fact]
    public void SavingCreatesTheDirectory()
    {
        // Arrange: a first run has no data directory at all until something writes to it.
        var store = Create();

        // Act
        store.Save(new PlayerSettings());

        // Assert
        File.Exists(SettingsPath).ShouldBeTrue();
    }

    /// <remarks>
    /// Written through a temporary file and moved into place, so a crash mid-write leaves the previous
    /// settings rather than a truncated document that then reads as corrupt. What is asserted is the visible
    /// consequence: nothing is left behind.
    /// </remarks>
    [Fact]
    public void SavingLeavesNoTemporaryFileBehind()
    {
        // Arrange
        var store = Create();

        // Act
        store.Save(new PlayerSettings());
        store.Save(new PlayerSettings());

        // Assert
        Directory.GetFiles(_directory).ShouldHaveSingleItem().ShouldEndWith("settings.json");
    }

    private string SettingsPath => Path.Combine(_directory, "settings.json");

    private PlayerSettingsStore Create()
    {
        return new PlayerSettingsStore(SettingsPath, NullLogger<PlayerSettingsStore>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
