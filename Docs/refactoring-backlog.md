# Refactoring backlog

Reviewed after M2. Ranks 1–6 are done and committed; what follows is what remains, in the order it
was prioritised — most valuable per unit of effort first.

Ranking rule: criticality against effort. Rank 7 is next not because it is urgent but because M3
(EPG) attaches to exactly the class it splits, and doing it afterwards costs more.

---

## Rank 7 — Split MainViewModel

**Project:** LTR.Player.Wpf · **Area:** Maintainability · **Criticality:** major · **Effort:** high

`MainViewModel` (~500 lines) carries three responsibilities and says so in its own documentation:
managing sources, presenting the channel list, and playback.

Proposed split:

| New class | Owns |
|---|---|
| `SourceManagementViewModel` | `Sources`, `SelectedSource`, the add-source form fields, Connect / Refresh / Remove / ShowAddSource / CancelAddSource |
| `ChannelListViewModel` | `ChannelView`, the backing channel list, category and text filters, favourites-only, `SelectedChannel`, ToggleFavorite |
| `MainViewModel` | Composes the two, owns playback (`NowPlaying`, PlaySelected, Stop) and the status line |

Coordination that has to survive the split:

- A change of `SelectedSource` must load that source's catalogue into the channel list. Today this
  happens through `OnSelectedSourceChanged`. Afterwards it wants to be an event the parent subscribes
  to, not a direct reference from one child to the other.
- Removing a source must stop playback **first** — the stream in flight belongs to the source about to
  disappear.
- `IsBusy` currently guards several commands at once and is read by both halves.

Also required:

- `MainWindow.xaml` bindings become nested paths (`Sources.SelectedSource`, `Channels.ChannelView`, …).
- The 23 tests in `LTR.Player.Wpf.Tests` must keep passing, adjusted for the new structure. They are
  the safety net for this change and the reason it is now much safer than it was before M2's review.

Do not lose these, all of which exist because something went wrong once:

- Every command guard needs `[NotifyCanExecuteChangedFor]` on **every** property it reads. Three
  shipped defects came from omitting it. `MainViewModelTests` asserts the notification, not just
  `CanExecute` — `CanExecute` invokes the guard directly and passes even when the bug is present.
- `RefreshChannelView` rebuilds the filter once and restores the selection. Both matter: building the
  filter per row allocated once per channel per keystroke, and a collection-view reset drops the
  list box selection.
- `PlaySelectedCommand` needs `AllowConcurrentExecutions = true`, or zapping away from a slow channel
  is silently ignored and `PlaybackSession`'s supersession handling becomes unreachable.

---

## Rank 8 — Protocol-neutral URL sanitisation

**Project:** LTR.Providers.* · **Area:** Security · **Criticality:** moderate · **Effort:** medium

`UrlSanitizer` is internal to `LTR.Providers.Xtream` and only understands `XtreamSource`. M3U playlist
URLs also carry credentials in their query string and have no sanitiser. Nothing logs one today, so
this is a missing guard rather than an active leak.

Proposal: `ISensitiveUrlSanitizer` in `LTR.Providers.Abstractions`, one implementation per protocol,
resolved through `IProviderRegistry`.

---

## Rank 9 — Stream the Xtream response instead of buffering it

**Project:** LTR.Providers.Xtream · **Area:** Performance · **Criticality:** moderate · **Effort:** medium

`XtreamApiClient.GetStringAsync` reads the whole response into a `string` and then parses a
`JsonDocument` from it — two multi-megabyte copies for a 17,000-channel catalogue.

Proposal: `JsonDocument.ParseAsync` over the content stream. The HTML-detection guard has to move to
the first bytes of the stream, which is the only fiddly part: panels answer with an HTML error page at
HTTP 200 and that must still be recognised.

---

## Rank 10 — Split MainWindow.xaml

**Project:** LTR.Player.Wpf · **Area:** Maintainability · **Criticality:** moderate · **Effort:** medium

One file holds the add-source form, the channel list and the player overlay. Natural follow-on from
rank 7: one `UserControl` per view model.

The overlay must stay inside `VideoView.Content`. `VideoView` hosts a separate native window over the
WPF tree, so a sibling element is invisible behind the video.

---

## Rank 11 — Command classes in the CLI

**Project:** LTR.Cli · **Area:** Maintainability · **Criticality:** minor · **Effort:** medium

`Program.cs` is mostly five `Build*` functions. Proposal: one class per command exposing
`Command Build()`, leaving `Program` as composition only.

---

## Rank 12 — Keep the domain entity out of the row view model

**Project:** LTR.Player.Wpf · **Area:** Maintainability · **Criticality:** minor · **Effort:** low

`ChannelItemViewModel.OnIsFavoriteChanged` writes back into the `Channel` entity so the filter, which
reads the entity, agrees with the row. Better: have the filter read the wrapper and leave the entity
alone.

---

## Rank 13 — Extract the reconciliation diff

**Project:** LTR.Persistence · **Area:** Maintainability · **Criticality:** minor · **Effort:** medium

Roughly a hundred lines inside `LtrDbContext` compute a diff and have no database concern. A
`CatalogueReconciler` could compute it while the context performs the writes, which keeps §3.3.2
intact and makes the algorithm testable on its own.

---

## Rank 14 — A cancellation token for the window lifetime

**Project:** LTR.Player.Wpf · **Area:** Usability · **Criticality:** minor · **Effort:** low

`InitializeAsync` and the catalogue load receive `CancellationToken.None`, so loading 17,000 channels
cannot be abandoned when the window closes.

---

## Rank 15 — Do not materialise the whole catalogue

**Project:** LTR.Persistence, LTR.Player.Wpf · **Area:** Performance · **Criticality:** moderate · **Effort:** high

Every source switch loads all channels and wraps each in a view model. It works at 17,000 channels.
Worth revisiting only when it demonstrably hurts — filtering and paging in the store rather than in
memory.
