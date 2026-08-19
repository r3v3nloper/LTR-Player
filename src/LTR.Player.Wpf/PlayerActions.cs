namespace LTR.Player.Wpf;

/// <summary>
/// Carries out what a keystroke asked for.
/// </summary>
/// <remarks>
/// <para>
/// Lifted out of the shell view model, which M5 had grown from 395 back to 483 lines — past the size that
/// earned it a split after M4, by the same mechanism as both previous times: it is the only place that can
/// reach everything, so everything lands in it.
/// </para>
/// <para>
/// The split it makes is the one the design already draws. Four actions need the shell, because they decide
/// *what* plays or what the window shows; every other action works on a stream already open and therefore
/// belongs to the overlay. Those four arrive as delegates, which is how the shell already hands work to
/// <see cref="PlaybackCoordinator.ProgressRecorded"/> and <see cref="GuideImportCoordinator.Start"/>.
/// </para>
/// </remarks>
internal sealed class PlayerActions
{
    private readonly PlayerOverlayViewModel _overlay;
    private readonly Func<CancellationToken, Task> _stop;
    private readonly Func<int, CancellationToken, Task> _playAdjacent;
    private readonly Func<CancellationToken, Task> _toggleGuide;

    /// <param name="playAdjacent">
    /// Moves the given number of places through whatever is playing — episodes of a series, channels when
    /// watching live — and plays what it lands on.
    /// </param>
    public PlayerActions(
        PlayerOverlayViewModel overlay,
        Func<CancellationToken, Task> stop,
        Func<int, CancellationToken, Task> playAdjacent,
        Func<CancellationToken, Task> toggleGuide)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(stop);
        ArgumentNullException.ThrowIfNull(playAdjacent);
        ArgumentNullException.ThrowIfNull(toggleGuide);

        _overlay = overlay;
        _stop = stop;
        _playAdjacent = playAdjacent;
        _toggleGuide = toggleGuide;
    }

    public Task PerformAsync(PlayerAction action, CancellationToken cancellationToken)
    {
        return action switch
        {
            PlayerAction.Stop => _stop(cancellationToken),
            PlayerAction.PlayNext => _playAdjacent(1, cancellationToken),
            PlayerAction.PlayPrevious => _playAdjacent(-1, cancellationToken),
            PlayerAction.ToggleGuide => _toggleGuide(cancellationToken),
            _ => PerformOnOverlay(action),
        };
    }

    /// <summary>
    /// Everything that acts on a stream already open, none of which needs awaiting.
    /// </summary>
    private Task PerformOnOverlay(PlayerAction action)
    {
        switch (action)
        {
            case PlayerAction.TogglePause:
                _overlay.TogglePauseCommand.Execute(parameter: null);
                break;

            case PlayerAction.VolumeUp:
                _overlay.ChangeVolume(PlayerOverlayViewModel.VolumeStep);
                break;

            case PlayerAction.VolumeDown:
                _overlay.ChangeVolume(-PlayerOverlayViewModel.VolumeStep);
                break;

            case PlayerAction.ToggleMute:
                _overlay.ToggleMuteCommand.Execute(parameter: null);
                break;

            case PlayerAction.SkipBack:
                _overlay.Skip(-PlayerOverlayViewModel.SkipStep);
                break;

            case PlayerAction.SkipForward:
                _overlay.Skip(PlayerOverlayViewModel.SkipStep);
                break;

            case PlayerAction.ToggleFullscreen:
                _overlay.ToggleFullscreenCommand.Execute(parameter: null);
                break;

            case PlayerAction.LeaveFullscreen:
                _overlay.LeaveFullscreen();
                break;

            case PlayerAction.ShowInfo:
                _overlay.Reveal();
                break;

            case PlayerAction.CycleAspectRatio:
                _overlay.CycleAspectRatio();
                break;

            default:
                break;
        }

        return Task.CompletedTask;
    }
}
