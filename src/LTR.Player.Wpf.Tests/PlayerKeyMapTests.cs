using System.Windows.Input;

namespace LTR.Player.Wpf;

/// <summary>
/// Covers which key means what.
/// </summary>
/// <remarks>
/// Trivial-looking and worth having, because the two rules that matter here are both invisible from the code
/// itself: a key the channel list needs must not be taken, and a modified key must not be answered at all.
/// Both are the kind of thing a later shortcut is added straight over the top of.
/// </remarks>
public sealed class PlayerKeyMapTests
{
    [Theory]
    [InlineData(Key.Space, PlayerAction.TogglePause)]
    [InlineData(Key.PageDown, PlayerAction.ZapNext)]
    [InlineData(Key.PageUp, PlayerAction.ZapPrevious)]
    [InlineData(Key.OemPlus, PlayerAction.VolumeUp)]
    [InlineData(Key.OemMinus, PlayerAction.VolumeDown)]
    [InlineData(Key.M, PlayerAction.ToggleMute)]
    [InlineData(Key.Left, PlayerAction.SkipBack)]
    [InlineData(Key.Right, PlayerAction.SkipForward)]
    [InlineData(Key.F, PlayerAction.ToggleFullscreen)]
    [InlineData(Key.F11, PlayerAction.ToggleFullscreen)]
    [InlineData(Key.Escape, PlayerAction.LeaveFullscreen)]
    [InlineData(Key.G, PlayerAction.ToggleGuide)]
    [InlineData(Key.I, PlayerAction.ShowInfo)]
    [InlineData(Key.A, PlayerAction.CycleAspectRatio)]
    public void EachShortcut_ResolvesToWhatItPromises(Key key, PlayerAction expected)
    {
        PlayerKeyMap.Resolve(key, ModifierKeys.None).ShouldBe(expected);
    }

    /// <remarks>
    /// The arrow keys are what someone uses to look down a list of seventeen thousand channels. Taking them
    /// for zapping would open every channel on the way past.
    /// </remarks>
    [Theory]
    [InlineData(Key.Up)]
    [InlineData(Key.Down)]
    [InlineData(Key.Home)]
    [InlineData(Key.End)]
    [InlineData(Key.Enter)]
    [InlineData(Key.Tab)]
    public void TheKeysTheChannelListNeeds_AreLeftAlone(Key key)
    {
        PlayerKeyMap.Resolve(key, ModifierKeys.None).ShouldBeNull();
    }

    /// <remarks>
    /// Ctrl+F is what a person reaches for to search. Answering it with fullscreen would be actively wrong,
    /// not merely surprising.
    /// </remarks>
    [Theory]
    [InlineData(ModifierKeys.Control)]
    [InlineData(ModifierKeys.Alt)]
    [InlineData(ModifierKeys.Shift)]
    [InlineData(ModifierKeys.Control | ModifierKeys.Shift)]
    public void AModifiedKey_MeansNothingToThePlayer(ModifierKeys modifiers)
    {
        PlayerKeyMap.Resolve(Key.F, modifiers).ShouldBeNull();
        PlayerKeyMap.Resolve(Key.Space, modifiers).ShouldBeNull();
    }

    /// <remarks>
    /// Every action has to be reachable, or it exists only as an enum member. This fails when a shortcut is
    /// added to <see cref="PlayerAction"/> and not to the map.
    /// </remarks>
    [Fact]
    public void EveryAction_HasAKey()
    {
        var reachable = Enum.GetValues<Key>()
            .Select(key => PlayerKeyMap.Resolve(key, ModifierKeys.None))
            .OfType<PlayerAction>()
            .Distinct()
            .ToList();

        reachable.ShouldBe(Enum.GetValues<PlayerAction>(), ignoreOrder: true);
    }
}
