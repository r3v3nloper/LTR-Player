using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LTR.Playback;

namespace LTR.Player.Wpf;

/// <summary>
/// One track menu — the audio languages, or the subtitles — kept in step with what the stream announces.
/// </summary>
/// <remarks>
/// <para>
/// One class used twice rather than two. The audio and subtitle menus differ in exactly one respect, that
/// subtitles can be switched off, and the awkward part is identical for both: a stream announces its tracks
/// as it encounters them, so the list arrives late, grows, and is replaced entirely on a channel change.
/// </para>
/// <para>
/// Which makes <see cref="Sync"/> the whole class. It is called several times a second while the overlay is
/// up, and must not rebuild a menu the viewer may have open — so it compares the identifiers first and does
/// nothing when they agree. Rebuilding regardless would close the drop-down on every tick.
/// </para>
/// </remarks>
public sealed partial class TrackSelectionViewModel : ObservableObject
{
    /// <summary>Label of the off entry, and the only string this class invents.</summary>
    public const string OffLabel = "Off";

    private readonly MediaTrackKind _kind;
    private readonly bool _canBeSwitchedOff;
    private readonly Action<MediaTrackKind, int> _select;

    /// <summary>
    /// Set while the engine's own choice is being adopted, so that it is not sent straight back to it.
    /// </summary>
    /// <remarks>
    /// Without it the menu would tell the engine to select the track the engine had just reported — and
    /// worse, since a stream announces its tracks a moment after starting, it would be overriding the
    /// default the stream itself declared with whichever entry happened to arrive first.
    /// </remarks>
    private bool _isAdoptingEnginesChoice;

    [ObservableProperty]
    private TrackChoice? _selectedTrack;

    /// <param name="canBeSwitchedOff">
    /// Whether the menu offers an off entry. True for subtitles, which start off and are chosen
    /// deliberately; false for audio, where switching sound off is what the mute button is for.
    /// </param>
    public TrackSelectionViewModel(
        MediaTrackKind kind,
        bool canBeSwitchedOff,
        Action<MediaTrackKind, int> select)
    {
        ArgumentNullException.ThrowIfNull(select);

        _kind = kind;
        _canBeSwitchedOff = canBeSwitchedOff;
        _select = select;
    }

    public ObservableCollection<TrackChoice> Tracks { get; } = [];

    /// <summary>
    /// Whether the menu is worth showing.
    /// </summary>
    /// <remarks>
    /// A menu offering one option is not a choice. That covers the common case of a channel with a single
    /// audio track and no subtitles, where showing two disabled pickers would suggest the player had failed
    /// to find something.
    /// </remarks>
    public bool IsAvailable => Tracks.Count > 1;

    /// <summary>
    /// Brings the menu in line with what the stream announces and what the engine has selected.
    /// </summary>
    public void Sync(IReadOnlyList<MediaTrack> announced, int selectedId)
    {
        ArgumentNullException.ThrowIfNull(announced);

        RebuildIfChanged(announced);
        AdoptSelection(selectedId);
    }

    /// <summary>
    /// Replaces the entries, but only when the stream is actually offering something different.
    /// </summary>
    private void RebuildIfChanged(IReadOnlyList<MediaTrack> announced)
    {
        var wanted = Build(announced);

        if (wanted.Select(choice => choice.Id).SequenceEqual(Tracks.Select(choice => choice.Id)))
        {
            return;
        }

        // Emptied and refilled before anything is selected. A bound picker writes a null selection back
        // through the binding the moment its list is cleared, and a selection assigned before the list is
        // complete is discarded — the trap that rendered two pickers blank in the milestone before this one.
        // Nothing needs suppressing around it, because a null selection is never passed on.
        Tracks.Clear();

        foreach (var choice in wanted)
        {
            Tracks.Add(choice);
        }

        SelectedTrack = null;

        OnPropertyChanged(nameof(IsAvailable));
    }

    /// <summary>
    /// Shows what is playing, which is the engine's business rather than this menu's.
    /// </summary>
    private void AdoptSelection(int selectedId)
    {
        var current = Tracks.FirstOrDefault(choice => choice.Id == selectedId);

        if (current is null || current == SelectedTrack)
        {
            return;
        }

        _isAdoptingEnginesChoice = true;

        try
        {
            SelectedTrack = current;
        }
        finally
        {
            _isAdoptingEnginesChoice = false;
        }
    }

    private List<TrackChoice> Build(IReadOnlyList<MediaTrack> announced)
    {
        var choices = new List<TrackChoice>();

        if (announced.Count > 0 && _canBeSwitchedOff)
        {
            choices.Add(new TrackChoice(MediaTrack.DisabledId, OffLabel));
        }

        choices.AddRange(announced.Select(track => new TrackChoice(track.Id, track.DisplayLabel)));

        return choices;
    }

    partial void OnSelectedTrackChanged(TrackChoice? value)
    {
        if (_isAdoptingEnginesChoice || value is null)
        {
            return;
        }

        _select(_kind, value.Id);
    }
}
