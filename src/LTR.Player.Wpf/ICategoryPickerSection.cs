using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;

namespace LTR.Player.Wpf;

/// <summary>
/// What a section has to offer for the shared category picker to be placed in it.
/// </summary>
/// <remarks>
/// <para>
/// The picker's markup names no section: its data context is whichever one it sits in, which is what lets a
/// single view serve live television, films and series. What that costs is a contract nothing checked — a
/// renamed member would leave the picker bound to nothing, in one section only, and WPF reports a failed
/// binding to a trace listener rather than to the log or the compiler.
/// </para>
/// <para>
/// So the shape is stated here and implemented by both view models. It exists to be broken loudly: the
/// bindings still resolve by name at runtime, and this is what makes the compiler notice first.
/// </para>
/// </remarks>
public interface ICategoryPickerSection
{
    /// <summary>The entries of the picker, pinned ones first.</summary>
    ObservableCollection<CategoryChoice> Categories { get; }

    /// <summary>
    /// The entry the picker is on, or null while it is being refilled — see the implementations.
    /// </summary>
    CategoryChoice? SelectedCategory { get; set; }

    /// <summary>Pins the chosen category to the top, or lets it fall back.</summary>
    IAsyncRelayCommand ToggleCategoryFavoriteCommand { get; }
}
