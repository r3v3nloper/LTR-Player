using System.Collections.ObjectModel;
using LTR.Catalogue;
using LTR.Core.Content;

namespace LTR.Player.Wpf;

/// <summary>
/// The rules every category picker follows: how it is filled, and what pinning one does to it.
/// </summary>
/// <remarks>
/// Three pickers use this — the channel list and both catalogue sections — and each of them had the filling
/// half already, written twice over. The pinning half is what made a single statement of it worth having:
/// where a pinned category goes has to agree with where the store puts it on the next load, and two
/// implementations of that agree only until one of them is edited.
/// </remarks>
internal static class CategoryPicker
{
    /// <summary>
    /// Refills <paramref name="picker"/> from what the store returned, keeping its order.
    /// </summary>
    /// <remarks>
    /// The caller selects afterwards and never before: emptying a bound collection makes a ComboBox write a
    /// null selection back through the binding, so a selection assigned first is discarded while the filter,
    /// reading the same null, still admits everything.
    /// </remarks>
    public static void Fill(ObservableCollection<CategoryChoice> picker, IReadOnlyList<Category> categories)
    {
        ArgumentNullException.ThrowIfNull(picker);
        ArgumentNullException.ThrowIfNull(categories);

        picker.Clear();
        picker.Add(CategoryChoice.All);

        for (var order = 0; order < categories.Count; order++)
        {
            var category = categories[order];

            picker.Add(new CategoryChoice(
                category.Name,
                category.ExternalId,
                category.Id,
                category.IsFavorite,
                order));
        }
    }

    /// <summary>
    /// Pins or unpins <paramref name="choice"/>, moves it to where that puts it, and records it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Moved rather than re-added, and the entry itself is never replaced. Both keep the picker's selection
    /// pointing at the same object, so pinning the category being watched does not change what is on screen —
    /// which is the whole reason someone pins the one they are watching.
    /// </para>
    /// <para>
    /// The unrestricted entry is not pinnable and stays first regardless; a caller reaching here with it has
    /// a command guard that is not doing its job, so this returns rather than writing a pin against the
    /// identity nothing has.
    /// </para>
    /// </remarks>
    public static async Task ToggleFavoriteAsync(
        ObservableCollection<CategoryChoice> picker,
        CategoryChoice choice,
        ISourceStore sources,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(picker);
        ArgumentNullException.ThrowIfNull(choice);
        ArgumentNullException.ThrowIfNull(sources);

        if (!choice.IsPinnable)
        {
            return;
        }

        choice.IsFavorite = !choice.IsFavorite;
        Reorder(picker);

        await sources.SetCategoryFavoriteAsync(choice.Id, choice.IsFavorite, cancellationToken)
            .ConfigureAwait(true);
    }

    /// <summary>
    /// Puts the picker back in rank order, one move at a time.
    /// </summary>
    /// <remarks>
    /// A move rather than a clear and refill, because a refill is what empties the ComboBox for an instant
    /// and takes the selection with it. Insertion sort over a couple of hundred entries, of which one has
    /// changed rank, so it settles in a single pass in every case that occurs.
    /// </remarks>
    private static void Reorder(ObservableCollection<CategoryChoice> picker)
    {
        for (var index = 1; index < picker.Count; index++)
        {
            var moving = picker[index];
            var target = index;

            while (target > 1 && Follows(picker[target - 1], moving))
            {
                target--;
            }

            if (target != index)
            {
                picker.Move(index, target);
            }
        }
    }

    private static bool Follows(CategoryChoice left, CategoryChoice right)
    {
        return left.Rank.CompareTo(right.Rank) > 0;
    }
}
