using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LTR.Core.Content;
using LTR.Core.Playback;
using LTR.Player.Wpf.Views;
using LTR.TestSupport;

namespace LTR.Player.Wpf;

/// <summary>
/// Builds the real controls in a window and moves the pointer over it, which is the only way to state where
/// the controls take their wake-up from.
/// </summary>
/// <remarks>
/// <para>
/// The defect this exists for: the controls take themselves away after four seconds, and the only thing that
/// brought them back was the pointer entering the shell's side panel. The picture is drawn by a window of its
/// own — <c>VideoView</c> hosts the overlay content over a native video surface — so a move over the picture
/// reaches neither the shell's handlers nor, apparently, this control's. In fullscreen there is no side panel
/// left, so the controls could not be reached at all.
/// </para>
/// <para>
/// The window here stands in for that hosting window, which is what makes the test worth having: it asserts
/// that a move seen by the window the controls live in is enough, whatever lies between the two.
/// </para>
/// </remarks>
public sealed class PlayerOverlayViewTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 12, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PointerMovingOverThePicture_BringsTheControlsBack()
    {
        // Arrange & Act
        var (hiddenWhileIdle, shownAfterTheMove) = VisualTreeHarness.OnStaThread(() =>
        {
            var (window, overlay, clock) = ShowControlsOverAStream();

            try
            {
                // The controls have been up and have taken themselves away again.
                clock.Advance(PlayerOverlayViewModel.IdleBeforeHiding);
                overlay.Sample();

                var hidden = overlay.IsVisible;

                // The pointer moves over the picture — over the window, not over any control of the
                // overlay's own, because when the bar is away there is nothing of its to move over.
                window.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, timestamp: 0)
                {
                    RoutedEvent = UIElement.PreviewMouseMoveEvent,
                });

                overlay.Sample();

                return (hidden, overlay.IsVisible);
            }
            finally
            {
                window.Close();
            }
        });

        // Assert
        hiddenWhileIdle.ShouldBeFalse("nothing had happened for four seconds");
        shownAfterTheMove.ShouldBeTrue("the pointer over the picture is what brings them back");
    }

    /// <remarks>
    /// The measured fact behind this, and the reason the whole surface is painted at all: `VideoView` draws
    /// this content in a layered window, and Windows hit-tests one of those by its alpha. Over a fully
    /// transparent pixel — which is what <c>Transparent</c> is — <c>WindowFromPoint</c> returns the window
    /// underneath, so the pointer reached the video and never the controls. A single count of alpha is
    /// invisible over a picture and is the difference between the two.
    /// </remarks>
    [Fact]
    public void ThePictureIsCoveredByASurfaceThePointerCannotFallThrough()
    {
        // Arrange & Act
        var background = VisualTreeHarness.OnStaThread(() =>
            ((Grid)new PlayerOverlayView().Content).Background as SolidColorBrush);

        // Assert
        background.ShouldNotBeNull("an unpainted surface is not hit at all");
        background.Color.A.ShouldNotBe((byte)0, "a fully transparent one is passed straight through");
    }

    [Fact]
    public void ThePointerGoesAwayWithTheControls_ButOnlyInFullscreen()
    {
        // Arrange & Act
        var (inAWindow, inFullscreen, whileTheControlsAreUp) = VisualTreeHarness.OnStaThread(() =>
        {
            var (window, overlay, clock) = ShowControlsOverAStream();

            try
            {
                var surface = (Grid)((PlayerOverlayView)window.Content).Content;

                clock.Advance(PlayerOverlayViewModel.IdleBeforeHiding);
                overlay.Sample();

                var windowed = surface.Cursor;

                overlay.IsFullscreen = true;
                VisualTreeHarness.PumpDispatcher(window);

                var full = surface.Cursor;

                overlay.Reveal();
                overlay.Sample();
                VisualTreeHarness.PumpDispatcher(window);

                return (windowed, full, surface.Cursor);
            }
            finally
            {
                window.Close();
            }
        });

        // Assert: an arrow over a film is as unwanted as the bar is, and for the same reason. In a window
        // it stays, because there the pointer is on its way to the channel list as often as not.
        inAWindow.ShouldNotBe(Cursors.None);
        inFullscreen.ShouldBe(Cursors.None);
        whileTheControlsAreUp.ShouldNotBe(Cursors.None, "there is something to aim at again");
    }

    [Fact]
    public void ControlsLeavingTheWindow_StopWakingIt()
    {
        // Arrange: the hosting window is not this view's own, so a handler left on it after the content has
        // moved elsewhere would outlive what it reveals.
        var wokeAfterUnloading = VisualTreeHarness.OnStaThread(() =>
        {
            var (window, overlay, clock) = ShowControlsOverAStream();

            try
            {
                window.Content = null;
                VisualTreeHarness.PumpDispatcher(window);

                clock.Advance(PlayerOverlayViewModel.IdleBeforeHiding);
                overlay.Sample();

                window.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, timestamp: 0)
                {
                    RoutedEvent = UIElement.PreviewMouseMoveEvent,
                });

                return overlay.IsVisible;
            }
            finally
            {
                window.Close();
            }
        });

        // Assert
        wokeAfterUnloading.ShouldBeFalse();
    }

    /// <summary>
    /// Shows the real controls over a stream that is playing, in a window standing in for the picture's.
    /// </summary>
    /// <remarks>
    /// Off the screen and without taking focus: this has to be a shown window for the content to be loaded
    /// into one at all, and a test run should not have windows appearing in front of whoever started it.
    /// </remarks>
    private static (Window Window, PlayerOverlayViewModel Overlay, TestClock Clock) ShowControlsOverAStream()
    {
        var session = new FakePlaybackSession();

        session.SwitchToAsync(
            new MediaRequest(
                new Uri("http://panel.example/live/u/p/1.ts"),
                "TestAgent/1.0",
                StreamFormat.MpegTs,
                "Erste"),
            CancellationToken.None).GetAwaiter().GetResult();

        var clock = new TestClock(Now);
        var overlay = new PlayerOverlayViewModel(session, new PlayerSettings(), clock);

        var window = new Window
        {
            Content = new PlayerOverlayView { DataContext = new OverlayHost(overlay) },
            Width = 640,
            Height = 360,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -32000,
            Top = -32000,
            ShowActivated = false,
            ShowInTaskbar = false,
        };

        window.Show();
        VisualTreeHarness.PumpDispatcher(window);

        overlay.Reveal();
        overlay.Sample();

        return (window, overlay, clock);
    }

    /// <summary>Stands in for the shell, which is what the overlay binds its own view model through.</summary>
    private sealed class OverlayHost
    {
        public OverlayHost(PlayerOverlayViewModel overlay)
        {
            PlayerOverlay = overlay;
        }

        public PlayerOverlayViewModel PlayerOverlay { get; }
    }
}
