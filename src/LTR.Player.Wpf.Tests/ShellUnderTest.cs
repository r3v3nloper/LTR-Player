using System.ComponentModel;

namespace LTR.Player.Wpf;

/// <summary>
/// The two things every test does to a composed shell, said once.
/// </summary>
/// <remarks>
/// <para>
/// Extension methods rather than members of <see cref="MainViewModelHarness"/>, which is what the backlog
/// entry proposed: both of these read the *view model*, not the fakes, and a test may build two shells from one
/// harness — <c>CategoryPinTests</c> does, to prove a pin survives the window reopening. A harness holding "the"
/// view model would have to pick one of them.
/// </para>
/// <para>
/// Neither is something the application does. Both exist because a test has to observe what a viewer simply
/// sees: whether the shell has finished reacting, and which rows the list is showing.
/// </para>
/// </remarks>
internal static class ShellUnderTest
{
    /// <summary>
    /// Waits for the shell to finish reacting to the last selection or search.
    /// </summary>
    /// <remarks>
    /// Deterministic, unlike the yield loop this replaced: it waits on the actual work rather than on enough
    /// scheduler turns having passed. The loop is there because one reload can raise the property change that
    /// starts the next — a selection clearing, then its detail loading — and it terminates because it only
    /// goes round again while something is genuinely still running.
    /// </remarks>
    public static async Task WaitForIdleAsync(this MainViewModel viewModel)
    {
        while (!viewModel.SectionWorkCompletion.IsCompleted)
        {
            await viewModel.SectionWorkCompletion;
        }
    }

    /// <summary>
    /// The channel rows the list is currently showing, in the order it shows them.
    /// </summary>
    /// <remarks>
    /// A snapshot, deliberately. <see cref="ChannelListViewModel.ChannelView"/> is an
    /// <see cref="ICollectionView"/> over the whole catalogue and re-reads itself when the filter changes, so a
    /// test that held one and asserted after a change would be asserting about the new list under the old name.
    /// Every caller wants what is on screen *now*.
    /// </remarks>
    public static IReadOnlyList<ChannelItemViewModel> VisibleChannels(this MainViewModel viewModel)
    {
        return [.. viewModel.Channels.ChannelView.Cast<ChannelItemViewModel>()];
    }
}
