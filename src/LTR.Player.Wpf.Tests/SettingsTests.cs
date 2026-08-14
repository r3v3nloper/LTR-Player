using LTR.Core.Content;
using LTR.Core.Sources;
using LTR.Playback;
using LTR.Playback.LibVlc;

namespace LTR.Player.Wpf;

/// <summary>
/// Covers the settings pane and the state the player remembers between sessions.
/// </summary>
public sealed class SettingsTests
{
    [Fact]
    public async Task OpeningSettings_HidesTheCatalogueAndShowsTheSelectedSource()
    {
        // Arrange
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource());

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        viewModel.IsShowingCatalogue.ShouldBeTrue();

        // Act
        viewModel.ToggleSettingsCommand.Execute(parameter: null);

        // Assert
        viewModel.Settings.IsOpen.ShouldBeTrue();
        viewModel.Settings.HasSource.ShouldBeTrue();
        viewModel.Settings.SourceName.ShouldBe("Panel");
        viewModel.IsShowingCatalogue.ShouldBeFalse();
    }

    /// <remarks>
    /// The same object-boundary problem as the command guards: the pane owns whether it is open and the left
    /// pane's visibility is computed on the shell, so the change has to be forwarded by hand.
    /// </remarks>
    [Fact]
    public void OpeningSettings_AnnouncesThatTheCatalogueIsNoLongerShowing()
    {
        // Arrange
        var context = new MainViewModelHarness();
        var viewModel = context.Build();

        var announcements = 0;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.IsShowingCatalogue))
            {
                announcements++;
            }
        };

        // Act
        viewModel.ToggleSettingsCommand.Execute(parameter: null);

        // Assert
        announcements.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task WithNoSourceSelected_OnlyTheEngineSettingsAreOffered()
    {
        // Arrange: there is no subscription to talk about, so its heading would be describing nothing.
        var context = new MainViewModelHarness();
        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        // Act
        viewModel.ToggleSettingsCommand.Execute(parameter: null);

        // Assert
        viewModel.Settings.HasSource.ShouldBeFalse();
    }

    [Fact]
    public async Task SavingWritesTheTuningAndTheSourcesOwnSettings()
    {
        // Arrange
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource());

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);
        viewModel.ToggleSettingsCommand.Execute(parameter: null);

        viewModel.Settings.LiveNetworkCachingMilliseconds = 900;
        viewModel.Settings.HardwareDecoding = HardwareDecodingChoice.For(HardwareDecoding.Disabled);
        viewModel.Settings.UserAgent = "  Lavf/60.16.100  ";
        viewModel.Settings.PreferredStreamFormat = StreamFormatChoice.For(StreamFormat.HlsPlaylist);

        // Act
        await viewModel.Settings.SaveCommand.ExecuteAsync(null);

        // Assert
        context.Settings.Playback.LiveNetworkCachingMilliseconds.ShouldBe(900);
        context.Settings.Playback.HardwareDecoding.ShouldBe(HardwareDecoding.Disabled);

        var write = context.Store.SourceSettingWrites.ShouldHaveSingleItem();
        write.SourceId.ShouldBe(1);
        write.UserAgent.ShouldBe("Lavf/60.16.100", "trimmed, because a trailing space breaks a header");
        write.Format.ShouldBe(StreamFormat.HlsPlaylist);

        viewModel.Settings.IsOpen.ShouldBeFalse("saving closes the pane");
    }

    /// <remarks>
    /// The stored source is what resolves the next stream's address. Writing only the database would make the
    /// change appear to need a restart when it does not.
    /// </remarks>
    [Fact]
    public async Task SavingAlsoUpdatesTheSourceAlreadyInMemory()
    {
        // Arrange
        var context = new MainViewModelHarness();
        var source = CreateSource();
        context.Store.Sources.Add(source);

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);
        viewModel.ToggleSettingsCommand.Execute(parameter: null);

        viewModel.Settings.UserAgent = "Lavf/60.16.100";

        // Act
        await viewModel.Settings.SaveCommand.ExecuteAsync(null);

        // Assert
        source.UserAgent.ShouldBe("Lavf/60.16.100");
    }

    [Fact]
    public async Task AnEmptyUserAgent_GoesBackToTheDefaultRatherThanSendingNone()
    {
        // Arrange: a request with no agent at all is what many panels refuse outright.
        var context = new MainViewModelHarness();
        context.Store.Sources.Add(CreateSource());

        var viewModel = context.Build();
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);
        viewModel.ToggleSettingsCommand.Execute(parameter: null);

        viewModel.Settings.UserAgent = "   ";

        // Act
        await viewModel.Settings.SaveCommand.ExecuteAsync(null);

        // Assert
        context.Store.SourceSettingWrites.ShouldHaveSingleItem().UserAgent
            .ShouldBe(PlaylistSource.DefaultUserAgent);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-500)]
    [InlineData(int.MaxValue)]
    public async Task AnUnusableBuffer_IsClampedRatherThanStored(int entered)
    {
        // Arrange: both failure modes are silent. No buffer at all becomes a stream that stutters
        // continuously, and a huge one looks exactly like a channel that will not start.
        var context = new MainViewModelHarness();
        var viewModel = context.Build();
        viewModel.ToggleSettingsCommand.Execute(parameter: null);

        viewModel.Settings.LiveNetworkCachingMilliseconds = entered;

        // Act
        await viewModel.Settings.SaveCommand.ExecuteAsync(null);

        // Assert
        context.Settings.Playback.LiveNetworkCachingMilliseconds
            .ShouldBeInRange(SettingsViewModel.MinimumCaching, SettingsViewModel.MaximumCaching);
    }

    [Fact]
    public void ARememberedVolume_IsAppliedToTheEngineOnStartup()
    {
        // Arrange: a player that starts at full volume every evening gets turned down every evening.
        var context = new MainViewModelHarness();
        context.Settings.Player.Volume = 25;
        context.Settings.Player.IsMuted = true;
        context.Settings.Player.AspectRatio = VideoAspectRatio.Standard;

        // Act
        var viewModel = context.Build();

        // Assert
        viewModel.PlayerOverlay.Volume.ShouldBe(25);
        context.Session.Volume.ShouldBe(25);
        context.Session.IsMuted.ShouldBeTrue();
        context.Session.AspectRatio.ShouldBe(VideoAspectRatio.Standard);
    }

    [Fact]
    public void ChangingTheVolume_IsRememberedWithoutWaitingForASave()
    {
        // Arrange
        var context = new MainViewModelHarness();
        var viewModel = context.Build();

        // Act
        viewModel.PlayerOverlay.ChangeVolume(-PlayerOverlayViewModel.VolumeStep);
        viewModel.PlayerOverlay.CycleAspectRatio();

        // Assert: written into the shared settings, which is what the shutdown path persists.
        context.Settings.Player.Volume.ShouldBe(100 - PlayerOverlayViewModel.VolumeStep);
        context.Settings.Player.AspectRatio.ShouldBe(viewModel.PlayerOverlay.SelectedAspectRatio.Value);
    }

    /// <remarks>
    /// The volume never passes through the settings pane, so the pane cannot be what saves it. Both halves go
    /// out together on the way out of the window.
    /// </remarks>
    [Fact]
    public async Task ClosingTheWindow_KeepsWhereTheViewerLeftTheControls()
    {
        // Arrange
        var context = new MainViewModelHarness();
        var viewModel = context.Build();

        viewModel.PlayerOverlay.Volume = 40;

        // Act
        await viewModel.ShutdownAsync();

        // Assert
        context.SavedSettings.ShouldNotBeNull();
        context.SavedSettings.Player.Volume.ShouldBe(40);
    }

    private static XtreamSource CreateSource()
    {
        return new XtreamSource
        {
            Id = 1,
            Name = "Panel",
            BaseUrl = new Uri("http://panel.example:8080", UriKind.Absolute),
            Username = "alice",
            Password = "s3cret",
        };
    }
}
