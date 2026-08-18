using CommunityToolkit.Mvvm.ComponentModel;

namespace LTR.Player.Wpf;

/// <summary>
/// An entry of the category picker.
/// </summary>
/// <remarks>
/// An object with an observable pin rather than a record, and the difference is load-bearing: pinning has to
/// leave the entry that is selected the same entry, and a record replaced by a differing copy is a different
/// item to the picker — which drops the selection and, through the binding, the filter with it.
/// </remarks>
public sealed partial class CategoryChoice : ObservableObject
{
    /// <summary>Where the store had this category, which is the order to fall back to when unpinned.</summary>
    private readonly int _providerOrder;

    [ObservableProperty]
    private bool _isFavorite;

    /// <param name="name">Label shown to the viewer.</param>
    /// <param name="externalId">
    /// Provider category to restrict to, or <see langword="null"/> for the entry that removes the
    /// restriction.
    /// </param>
    /// <param name="id">The stored category's own identity, which is what a pin is written against.</param>
    /// <param name="providerOrder">Its position in what the store returned.</param>
    public CategoryChoice(
        string name,
        string? externalId,
        int id = 0,
        bool isFavorite = false,
        int providerOrder = 0)
    {
        Name = name;
        ExternalId = externalId;
        Id = id;
        _isFavorite = isFavorite;
        _providerOrder = providerOrder;
    }

    /// <summary>
    /// The entry that lifts the category restriction. Always first in the picker, so the list can be
    /// widened again without clearing the selection.
    /// </summary>
    public static CategoryChoice All { get; } = new("All categories", externalId: null);

    public string Name { get; }

    public string? ExternalId { get; }

    public int Id { get; }

    /// <summary>Whether this entry stands for a category that can be pinned at all.</summary>
    public bool IsPinnable => ExternalId is not null;

    /// <summary>
    /// Where this entry belongs in the picker: the unrestricted entry, then what the viewer pinned, then
    /// the provider's own order.
    /// </summary>
    /// <remarks>
    /// The same rule the store sorts by, restated here only because a pin has to take effect without
    /// rereading the catalogue — rereading it would refill the picker, and an emptied picker writes a null
    /// selection back through the binding.
    /// </remarks>
    public (int Group, int Order) Rank => (IsPinnable ? (IsFavorite ? 1 : 2) : 0, _providerOrder);
}
