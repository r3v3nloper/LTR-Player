using LTR.Core.Content;
using LTR.Core.Playback;
using LTR.Playback;
using LTR.TestSupport;

namespace LTR.Player.Wpf;

/// <summary>
/// Covers the on-screen controls: when they appear, when they go away, and what they do to playback.
/// </summary>
/// <remarks>
/// Everything here is logic rather than presentation, which is why it is worth testing at all. Whether the
/// controls are on screen is decided by a clock and a playback state; whether a drag becomes a seek is
/// decided by how far it moved; and whether a track menu is rebuilt is decided by comparing identifiers —
/// each of those has a wrong answer that looks perfectly plausible in a screenshot.
/// </remarks>
public sealed class PlayerOverlayTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 12, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Controls_AreHidden_WhileNothingIsPlaying()
    {
        // Arrange: an empty pane needs a hint, not a transport bar.
        var (overlay, _, _) = Create();

        // Act
        overlay.Reveal();
        overlay.Sample();

        // Assert
        overlay.IsVisible.ShouldBeFalse();
    }

    [Fact]
    public async Task Controls_AppearOnceSomethingIsPlaying()
    {
        // Arrange
        var (overlay, session, _) = Create();
        await Play(session);

        // Act
        overlay.Reveal();
        overlay.Sample();

        // Assert
        overlay.IsVisible.ShouldBeTrue();
    }

    [Fact]
    public async Task Controls_TakeThemselvesAway_OnceNothingHasHappenedForAWhile()
    {
        // Arrange
        var (overlay, session, clock) = Create();
        await Play(session);
        overlay.Reveal();
        overlay.Sample();

        // Act
        clock.Advance(PlayerOverlayViewModel.IdleBeforeHiding);
        overlay.Sample();

        // Assert
        overlay.IsVisible.ShouldBeFalse();
    }

    [Fact]
    public async Task Controls_StayUp_ForAsLongAsTheViewerKeepsDoingThings()
    {
        // Arrange
        var (overlay, session, clock) = Create();
        await Play(session);
        overlay.Reveal();

        // Act: something happens on each pass, well inside the idle time.
        for (var pass = 0; pass < 4; pass++)
        {
            clock.Advance(PlayerOverlayViewModel.IdleBeforeHiding - TimeSpan.FromSeconds(1));
            overlay.Reveal();
            overlay.Sample();
        }

        // Assert
        overlay.IsVisible.ShouldBeTrue();
    }

    [Fact]
    public async Task Controls_StayUp_WhileThePointerRestsOnThem()
    {
        // Arrange: a pointer that has stopped moving raises nothing further, so the idle timer would
        // otherwise take the bar away from under a hand on its way to a button.
        var (overlay, session, clock) = Create();
        await Play(session);
        overlay.Reveal();
        overlay.IsPointerOnControls = true;

        // Act
        clock.Advance(PlayerOverlayViewModel.IdleBeforeHiding * 10);
        overlay.Sample();

        // Assert
        overlay.IsVisible.ShouldBeTrue();

        // Act: the pointer goes elsewhere, and the countdown starts from there rather than from where it
        // had got to before the pointer arrived.
        overlay.IsPointerOnControls = false;
        overlay.Reveal();
        clock.Advance(PlayerOverlayViewModel.IdleBeforeHiding);
        overlay.Sample();

        // Assert
        overlay.IsVisible.ShouldBeFalse();
    }

    [Fact]
    public async Task Controls_StayUp_WhilePlaybackIsPaused()
    {
        // Arrange: a still picture with no controls on it reads as a frozen application, so pausing is
        // deliberately not a state the controls hide from.
        var (overlay, session, clock) = Create();
        await Play(session);
        overlay.Reveal();
        overlay.TogglePauseCommand.Execute(parameter: null);

        // Act
        clock.Advance(PlayerOverlayViewModel.IdleBeforeHiding * 10);
        overlay.Sample();

        // Assert
        overlay.IsVisible.ShouldBeTrue();
        overlay.IsPaused.ShouldBeTrue();
        session.IsPaused.ShouldBeTrue();
    }

    [Fact]
    public async Task Pause_TogglesBackAndForth()
    {
        // Arrange
        var (overlay, session, _) = Create();
        await Play(session);

        // Act & Assert
        overlay.TogglePauseCommand.Execute(parameter: null);
        session.IsPaused.ShouldBeTrue();

        overlay.TogglePauseCommand.Execute(parameter: null);
        session.IsPaused.ShouldBeFalse();
    }

    [Fact]
    public async Task SeekBar_IsOfferedOnlyForAStreamThatCanBePositioned()
    {
        // Arrange
        var (overlay, session, _) = Create();
        await Play(session, seekable: false);

        // Act
        overlay.Sample();

        // Assert
        overlay.IsSeekable.ShouldBeFalse("live television has no position to move to");

        // Act: a film, on the same session
        session.IsSeekable = true;
        overlay.Sample();

        // Assert
        overlay.IsSeekable.ShouldBeTrue();
    }

    [Fact]
    public async Task Position_IsLeftAlone_WhileTheSeekBarIsBeingDragged()
    {
        // Arrange: this is what makes the bar aimable. Writing the position from the timer would drag the
        // thumb out from under the pointer twice a second.
        var (overlay, session, _) = Create();
        await Play(session, seekable: true);
        session.Position = TimeSpan.FromMinutes(10);
        overlay.Sample();

        // Act
        overlay.BeginScrub();
        overlay.PositionSeconds = TimeSpan.FromMinutes(55).TotalSeconds;

        session.Position = TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1);
        overlay.Sample();

        // Assert
        overlay.PositionSeconds.ShouldBe(TimeSpan.FromMinutes(55).TotalSeconds);
    }

    [Fact]
    public async Task LettingTheSeekBarGo_MovesPlaybackToWhereItWasLeft()
    {
        // Arrange
        var (overlay, session, _) = Create();
        await Play(session, seekable: true);
        session.Position = TimeSpan.FromMinutes(10);
        overlay.Sample();

        // Act
        overlay.BeginScrub();
        overlay.PositionSeconds = TimeSpan.FromMinutes(55).TotalSeconds;
        overlay.EndScrub();

        // Assert
        overlay.IsScrubbing.ShouldBeFalse();
        session.SeekedTo.ShouldBe(TimeSpan.FromMinutes(55));
    }

    [Fact]
    public async Task ClickingTheSeekBarWhereItAlreadyIs_DoesNotSeek()
    {
        // Arrange: an idle click would otherwise make a film re-buffer over HTTP for no reason.
        var (overlay, session, _) = Create();
        await Play(session, seekable: true);
        session.Position = TimeSpan.FromMinutes(10);
        overlay.Sample();

        // Act
        overlay.BeginScrub();
        overlay.EndScrub();

        // Assert
        session.SeekedTo.ShouldBeNull();
    }

    [Fact]
    public async Task Skip_MovesFromWhereThePictureIs()
    {
        // Arrange
        var (overlay, session, _) = Create();
        await Play(session, seekable: true);
        session.Position = TimeSpan.FromMinutes(10);

        // Act
        overlay.Skip(PlayerOverlayViewModel.SkipStep);

        // Assert
        session.SeekedTo.ShouldBe(TimeSpan.FromMinutes(10) + PlayerOverlayViewModel.SkipStep);
    }

    [Fact]
    public async Task Skip_WithNoPositionToSkipFrom_DoesNothing()
    {
        // Arrange: a live stream, and the first moments of a film, both report no position at all.
        var (overlay, session, _) = Create();
        await Play(session, seekable: true);
        session.Position = null;

        // Act
        overlay.Skip(PlayerOverlayViewModel.SkipStep);

        // Assert
        session.SeekedTo.ShouldBeNull();
    }

    [Fact]
    public void Volume_IsClampedToWhatTheEngineAccepts()
    {
        // Arrange
        var (overlay, session, _) = Create();

        // Act
        overlay.ChangeVolume(1000);

        // Assert
        overlay.Volume.ShouldBe(100);
        session.Volume.ShouldBe(100);

        // Act
        overlay.ChangeVolume(-1000);

        // Assert
        overlay.Volume.ShouldBe(0);
        session.Volume.ShouldBe(0);
    }

    [Fact]
    public void TurningTheVolumeUp_UnmutesFirst()
    {
        // Arrange: pressing volume-up on a muted player and still hearing nothing reads as a broken key.
        var (overlay, session, _) = Create();
        overlay.ToggleMuteCommand.Execute(parameter: null);

        // Act
        overlay.ChangeVolume(PlayerOverlayViewModel.VolumeStep);

        // Assert
        overlay.IsMuted.ShouldBeFalse();
        session.IsMuted.ShouldBeFalse();
    }

    [Fact]
    public async Task TheViewersVolume_IsPushedAtEachNewStream()
    {
        // Arrange: an engine is entitled to forget it when media is opened, and the volume a viewer chose is
        // not a per-channel decision.
        var (overlay, session, _) = Create();
        overlay.Volume = 30;

        // Act
        await Play(session);
        session.Volume = 100;
        overlay.Sample();

        // Assert
        session.Volume.ShouldBe(30);
    }

    [Fact]
    public void AspectRatio_CyclesThroughTheOfferedRatiosAndBackToTheStreamsOwn()
    {
        // Arrange
        var (overlay, session, _) = Create();

        // Act & Assert
        for (var step = 0; step < AspectRatioChoice.All.Count; step++)
        {
            overlay.CycleAspectRatio();
            session.AspectRatio.ShouldBe(overlay.SelectedAspectRatio.Value);
        }

        overlay.SelectedAspectRatio.Value.ShouldBe(VideoAspectRatio.Source, "it wraps round");
    }

    [Fact]
    public async Task AudioMenu_ShowsWhatTheStreamAnnouncedAndWhatTheEngineChose()
    {
        // Arrange
        var (overlay, session, _) = Create();
        await Play(session);

        session.Tracks[MediaTrackKind.Audio] =
        [
            new MediaTrack(1, MediaTrackKind.Audio, "Deutsch", Language: null),
            new MediaTrack(2, MediaTrackKind.Audio, "English", Language: null),
        ];

        // What the stream itself chose, which is the entry the menu has to show as selected.
        session.PlayingTrack[MediaTrackKind.Audio] = 2;

        // Act
        overlay.Sample();

        // Assert
        overlay.AudioTracks.Tracks.Select(choice => choice.Label).ShouldBe(["Deutsch", "English"]);
        overlay.AudioTracks.IsAvailable.ShouldBeTrue();
        overlay.AudioTracks.SelectedTrack.ShouldNotBeNull();
        overlay.AudioTracks.SelectedTrack.Label.ShouldBe("English");
    }

    /// <remarks>
    /// The defect this guards against is subtle and would have been invisible: a stream announces its tracks
    /// a moment after starting, and a menu that reported its own first entry as selected would tell the
    /// engine to switch to it — overriding the language the stream itself declared.
    /// </remarks>
    [Fact]
    public async Task AudioMenu_DoesNotChooseForTheStream()
    {
        // Arrange
        var (overlay, session, _) = Create();
        await Play(session);

        session.Tracks[MediaTrackKind.Audio] =
        [
            new MediaTrack(1, MediaTrackKind.Audio, "Deutsch", Language: null),
            new MediaTrack(2, MediaTrackKind.Audio, "English", Language: null),
        ];

        // The stream declared its own default, as a real one does.
        session.PlayingTrack[MediaTrackKind.Audio] = 2;

        // Act
        overlay.Sample();
        overlay.Sample();

        // Assert
        session.SelectedTracks.ShouldBeEmpty();
    }

    [Fact]
    public async Task ChoosingATrack_TellsTheEngine()
    {
        // Arrange
        var (overlay, session, _) = Create();
        await Play(session);

        session.Tracks[MediaTrackKind.Audio] =
        [
            new MediaTrack(1, MediaTrackKind.Audio, "Deutsch", Language: null),
            new MediaTrack(2, MediaTrackKind.Audio, "English", Language: null),
        ];

        overlay.Sample();

        // Act
        overlay.AudioTracks.SelectedTrack =
            overlay.AudioTracks.Tracks.First(choice => choice.Label == "English");

        // Assert
        session.SelectedTracks.ShouldBe([(MediaTrackKind.Audio, 2)]);
    }

    [Fact]
    public async Task TrackMenus_AreNotRebuiltWhileTheStreamKeepsAnnouncingTheSameThing()
    {
        // Arrange: the menu is synced several times a second while the controls are up, and rebuilding it
        // regardless would close the drop-down on every tick.
        var (overlay, session, _) = Create();
        await Play(session);

        session.Tracks[MediaTrackKind.Audio] =
        [
            new MediaTrack(1, MediaTrackKind.Audio, "Deutsch", Language: null),
            new MediaTrack(2, MediaTrackKind.Audio, "English", Language: null),
        ];

        overlay.Sample();

        var resets = 0;
        overlay.AudioTracks.Tracks.CollectionChanged += (_, _) => resets++;

        // Act
        overlay.Sample();
        overlay.Sample();

        // Assert
        resets.ShouldBe(0);
    }

    [Fact]
    public async Task SubtitleMenu_OffersOffAndTheAudioMenuDoesNot()
    {
        // Arrange: subtitles start off and are chosen deliberately; switching sound off is what mute is for.
        var (overlay, session, _) = Create();
        await Play(session);

        session.Tracks[MediaTrackKind.Audio] = [new MediaTrack(1, MediaTrackKind.Audio, "Deutsch", null)];
        session.Tracks[MediaTrackKind.Subtitle] = [new MediaTrack(4, MediaTrackKind.Subtitle, "Deutsch", null)];

        // Act
        overlay.Sample();

        // Assert
        overlay.SubtitleTracks.Tracks.Select(choice => choice.Label)
            .ShouldBe([TrackSelectionViewModel.OffLabel, "Deutsch"]);
        overlay.SubtitleTracks.IsAvailable.ShouldBeTrue();

        overlay.AudioTracks.Tracks.Select(choice => choice.Label).ShouldBe(["Deutsch"]);
        overlay.AudioTracks.IsAvailable.ShouldBeFalse("one option is not a choice");
    }

    [Fact]
    public async Task TrackMenus_AreEmptied_WhenTheStreamAnnouncesNothing()
    {
        // Arrange: a channel change replaces the tracks entirely, and the previous channel's languages must
        // not be left on offer.
        var (overlay, session, _) = Create();
        await Play(session);

        session.Tracks[MediaTrackKind.Audio] =
        [
            new MediaTrack(1, MediaTrackKind.Audio, "Deutsch", Language: null),
            new MediaTrack(2, MediaTrackKind.Audio, "English", Language: null),
        ];

        overlay.Sample();
        overlay.AudioTracks.Tracks.Count.ShouldBe(2);

        // Act
        session.Tracks[MediaTrackKind.Audio] = [];
        overlay.Sample();

        // Assert
        overlay.AudioTracks.Tracks.ShouldBeEmpty();
        overlay.AudioTracks.IsAvailable.ShouldBeFalse();
    }

    [Fact]
    public void Fullscreen_TogglesAndCanBeLeftDirectly()
    {
        // Arrange
        var (overlay, _, _) = Create();

        // Act & Assert
        overlay.ToggleFullscreenCommand.Execute(parameter: null);
        overlay.IsFullscreen.ShouldBeTrue();

        overlay.LeaveFullscreen();
        overlay.IsFullscreen.ShouldBeFalse();

        // Escape while not in fullscreen is a no-op rather than a way into it.
        overlay.LeaveFullscreen();
        overlay.IsFullscreen.ShouldBeFalse();
    }

    [Fact]
    public async Task Times_AreShownAsAViewerReadsThem()
    {
        // Arrange
        var (overlay, session, _) = Create();
        await Play(session, seekable: true);
        session.Position = TimeSpan.FromMinutes(40);
        session.Duration = TimeSpan.FromMinutes(115);

        // Act
        overlay.Sample();

        // Assert
        overlay.PositionLabel.ShouldBe("40:00");
        overlay.DurationLabel.ShouldBe("1:55:00");
        overlay.DurationSeconds.ShouldBe(TimeSpan.FromMinutes(115).TotalSeconds);
    }

    private static (PlayerOverlayViewModel Overlay, FakePlaybackSession Session, TestClock Clock) Create()
    {
        var session = new FakePlaybackSession();
        var clock = new TestClock(Now);

        return (new PlayerOverlayViewModel(session, new PlayerSettings(), clock), session, clock);
    }

    private static async Task Play(FakePlaybackSession session, bool seekable = false)
    {
        session.IsSeekable = seekable;

        await session.SwitchToAsync(
            new MediaRequest(
                new Uri("http://panel.example/live/u/p/1.ts"),
                "TestAgent/1.0",
                StreamFormat.MpegTs,
                "Erste"),
            TestContext.Current.CancellationToken);
    }
}
