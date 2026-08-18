# Pinned categories

A panel lists its categories in whatever order it holds them, and there are a couple of hundred of them. The
two or three somebody actually watches are as likely to sit at the bottom as anywhere, so every session began
with the same scroll. A star pins one to the top.

## Pinning rather than reordering

Free reordering was the alternative, and it loses on the case that motivated this: moving a category from
position forty to the top is thirty-nine presses of a button, or a drag inside a drop-down, which is not a
gesture a ComboBox offers. Pinning is one press and needs no order of its own — the provider's order is kept
underneath, so a category that is unpinned goes back exactly where it was rather than somewhere it was
dragged to once.

It also reuses a concept the application already has. A favourite channel is the viewer's own data stored
next to the provider's; a pinned category is the same thing one level up, and behaves the same way on a
refresh: `Category.AdoptProviderFields` does not copy it, so an import may rename a category and reposition
it without unpinning it.

## Where the order is decided

**In the store.** `LtrDbContext.GetCategoriesAsync` orders by the pin, then by the provider's `SortOrder`,
then by name. There are three pickers and the CLI besides, and an ordering stated four times is one that will
be stated differently in one of them.

**And once more in the picker**, deliberately, in `CategoryPicker`. A pin has to take effect the moment it is
pressed, and the only other way to do that is to reread the catalogue — which refills the bound collection,
and an emptied ComboBox writes a null selection back through the binding. The rule is therefore stated twice,
in the two places it has to hold, and `CategoryPinTests` asserts them against each other: it pins a category
through the picker and then reloads the source through the store, which is the assertion that keeps them
agreeing.

Two consequences of the same reasoning:

- **The entry is moved, not replaced.** `CategoryChoice` is an object with an observable pin rather than a
  record, because a record replaced by a differing copy is a different item to the picker: the selection goes,
  and the filter reading it widens to everything. Someone pinning the category they are watching would find
  the list they were looking at replaced by all seventeen thousand channels.
- **Pinning is not a stack.** Two pinned categories keep the provider's order between them, so the picker does
  not reshuffle itself according to what was starred most recently.

## One picker, three sections

`Views/CategoryPickerView.xaml` is used by the channel list and by both catalogue sections. Nothing in it
names a section: its data context is whichever section it is placed in, and the three offer the same three
members — `Categories`, `SelectedCategory`, `ToggleCategoryFavoriteCommand`. That is what lets the markup be
one file, and it is why the star's rule cannot drift between live television and films.

The unrestricted entry — "All categories" — is not pinnable. It stands for no category, so there is no stored
identity to write a pin against; the button is disabled on it, which needs `[NotifyCanExecuteChangedFor]` on
the selection like every other guard in this application.

## The identity a pin is written against

The stored category's own, never the provider's number. A panel numbers its categories per section, so `58`
is a live category and a film category at once — pinning "Action" in the film picker must not pin
"DE Deutschland" in the live one. `CategoryPinTests.TheFilmSectionPinsItsOwnCategories` is that case.

## The selection can be empty

`SelectedCategory` is nullable in both view models, and not defensively. A ComboBox pushes a null selection
back through the binding whenever its bound collection is emptied, which is what filling the picker does — so
the first fill at startup passes through every reader of the selection. The pin's command guard is asked on
each selection change and was declared against a non-null property, which made it the one reader that could
not survive that instant. Anything new that reads the selection has to allow for it.
