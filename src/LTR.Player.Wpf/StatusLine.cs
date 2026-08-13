using CommunityToolkit.Mvvm.ComponentModel;

namespace LTR.Player.Wpf;

/// <summary>
/// The one line of text at the bottom of the window that tells the user what just happened.
/// </summary>
/// <remarks>
/// An object of its own rather than a property, because all three halves of the shell write to it —
/// source management while importing, the channel list once a catalogue is on screen, and playback when
/// a channel turns out to be offline. Passing the line itself around keeps that a single observable
/// value instead of three properties forwarding to one another.
/// </remarks>
public sealed partial class StatusLine : ObservableObject
{
    /// <summary>Shown before anything is configured, and again once the last source is removed.</summary>
    public const string NoSourcesConfigured = "Add a subscription to begin.";

    [ObservableProperty]
    private string _text = NoSourcesConfigured;
}
