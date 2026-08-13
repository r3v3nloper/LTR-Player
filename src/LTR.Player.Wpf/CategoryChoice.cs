namespace LTR.Player.Wpf;

/// <summary>
/// An entry of the category picker.
/// </summary>
/// <param name="Name">Label shown to the user.</param>
/// <param name="ExternalId">
/// Provider category to restrict to, or <see langword="null"/> for the entry that removes the
/// restriction.
/// </param>
public sealed record CategoryChoice(string Name, string? ExternalId)
{
    /// <summary>
    /// The entry that lifts the category restriction. Always first in the picker, so the list can be
    /// widened again without clearing the selection.
    /// </summary>
    public static CategoryChoice All { get; } = new("All categories", ExternalId: null);
}
