using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LTR.Core.Content;
using LTR.Player.Wpf.Views;
using LTR.TestSupport;

namespace LTR.Player.Wpf;

/// <summary>
/// Loads the guide's real markup and measures it, which is the only way to state a layout property.
/// </summary>
/// <remarks>
/// <para>
/// The first test in this project to build a visual tree, and it exists for one reason: the timeline's
/// channel names used to scroll away with the programme blocks, and nothing but a running window could say
/// whether they still do. Every WPF defect this repository has shipped was in markup, which is the part
/// nothing measured.
/// </para>
/// <para>
/// It also settles a question the compiler does not: the blocks follow the header's scroller through an
/// <c>ElementName</c> binding reaching out of a <c>DataTemplate</c>, which resolves through the outer
/// namescope — the same way the two bindings already in that template do.
/// </para>
/// </remarks>
public sealed class GuideOverlayViewTests
{
    private const double ScrolledBy = 200;

    [Fact]
    public void ScrollingTheTimeline_LeavesTheChannelNameWhereItWas()
    {
        // Arrange & Act
        var (before, after) = VisualTreeHarness.OnStaThread(() =>
        {
            var view = BuildGuide(out var scroller, out _);
            var name = FirstChannelName(view);

            var start = OffsetWithin(name, view);

            scroller.ScrollToHorizontalOffset(ScrolledBy);
            Settle(view);

            return (start, OffsetWithin(name, view));
        });

        // Assert
        after.X.ShouldBe(before.X, "the channel column is pinned; that is the whole point");
        after.Y.ShouldBe(before.Y);
    }

    [Fact]
    public void ScrollingTheTimeline_MovesTheProgrammeBlocksWithIt()
    {
        // Arrange & Act
        var (before, after) = VisualTreeHarness.OnStaThread(() =>
        {
            var view = BuildGuide(out var scroller, out _);
            var programmes = FirstProgrammeStrip(view);

            var start = OffsetWithin(programmes, view);

            scroller.ScrollToHorizontalOffset(ScrolledBy);
            Settle(view);

            return (start, OffsetWithin(programmes, view));
        });

        // Assert: pinning the names is only half of it. Blocks that did not follow the header would leave
        // every programme sitting under the wrong time, which is worse than a name scrolling away.
        (before.X - after.X).ShouldBe(ScrolledBy, tolerance: 0.5);
    }

    [Fact]
    public void ScrollingTheTimeline_MovesTheNowMarkerWithTheBlocks()
    {
        // Arrange & Act
        var (blocks, marker) = VisualTreeHarness.OnStaThread(() =>
        {
            var view = BuildGuide(out var scroller, out var markerLayer);
            var programmes = FirstProgrammeStrip(view);

            var blocksBefore = OffsetWithin(programmes, view);
            var markerBefore = OffsetWithin(markerLayer, view);

            scroller.ScrollToHorizontalOffset(ScrolledBy);
            Settle(view);

            return (
                blocksBefore.X - OffsetWithin(programmes, view).X,
                markerBefore.X - OffsetWithin(markerLayer, view).X);
        });

        // Assert: the marker states a time, so it has to travel exactly as far as the blocks do.
        marker.ShouldBe(blocks, tolerance: 0.5);
    }

    /// <summary>
    /// Builds the real view over a guide holding one channel and one programme, and lays it out narrow
    /// enough that four hours of timeline cannot fit.
    /// </summary>
    private static GuideOverlayView BuildGuide(out ScrollViewer scroller, out FrameworkElement nowMarker)
    {
        VisualTreeHarness.EnsureThemeLoaded();

        var guide = new GuideViewModel(new FakeCatalogueStore(), new TestClock(MainViewModelHarness.Now))
        {
            IsVisible = true,
            NowPosition = 120,
            IsNowVisible = true,
        };

        guide.TimeMarkers.Add(new GuideTimeMarker(0, "18:00"));
        guide.Rows.Add(new GuideRowViewModel(
            new Channel { Id = 1, SourceId = 1, ExternalId = "101", Name = "Erste" },
            [new GuideProgrammeViewModel(Programme(), guide.Timeline)]));

        var view = new GuideOverlayView { DataContext = new GuideHost(guide) };

        // Narrower than the timeline's four hours at its fixed scale, so there is something to scroll.
        view.Width = 600;
        view.Height = 400;
        Settle(view);

        scroller = VisualTreeHarness.Descendant<ScrollViewer>(view, "TimelineScroller");
        nowMarker = VisualTreeHarness.Descendant<FrameworkElement>(view, "NowMarkerLayer");

        return view;
    }

    private static EpgEntry Programme()
    {
        return new EpgEntry
        {
            GuideChannelId = 1,
            Title = "Tagesschau",
            StartUtc = MainViewModelHarness.Now,
            StopUtc = MainViewModelHarness.Now.AddMinutes(15),
        };
    }

    /// <summary>Where an element sits relative to the whole overlay.</summary>
    private static Point OffsetWithin(Visual element, Visual root)
    {
        return element.TransformToAncestor(root).Transform(new Point(0, 0));
    }

    private static TextBlock FirstChannelName(DependencyObject view)
    {
        return VisualTreeHarness.Descendants<TextBlock>(view).First(text => text.Text == "Erste");
    }

    /// <summary>
    /// The strip of programme blocks belonging to the first row, found by the width only it has.
    /// </summary>
    private static ItemsControl FirstProgrammeStrip(GuideOverlayView view)
    {
        var guide = ((GuideHost)view.DataContext).Guide;

        return VisualTreeHarness.Descendants<ItemsControl>(view)
            .First(items => items is not ListBox
                && Math.Abs(items.Width - guide.Timeline.Width) < 0.5
                && items.Items.Count > 0
                && items.Items[0] is GuideProgrammeViewModel);
    }

    /// <summary>
    /// Runs a layout pass, twice over: a scroll is applied during arrange, and the bindings that follow it
    /// are only up to date once that pass has been through.
    /// </summary>
    private static void Settle(UIElement element)
    {
        element.Measure(new Size(600, 400));
        element.Arrange(new Rect(0, 0, 600, 400));
        element.UpdateLayout();
        element.UpdateLayout();
    }

    /// <summary>Stands in for the shell, which is what the overlay binds its <c>Guide</c> through.</summary>
    private sealed class GuideHost
    {
        public GuideHost(GuideViewModel guide)
        {
            Guide = guide;
        }

        public GuideViewModel Guide { get; }
    }
}
