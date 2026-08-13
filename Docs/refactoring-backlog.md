# Refactoring backlog

Reviewed after M2. Ranks 1–7 are done; what follows is what remains, in the order it was prioritised —
most valuable per unit of effort first. Items 16 and 17 came out of M3 and have not been ranked against
the rest.

Ranking rule: criticality against effort.

---

## Rank 16 — Pin the timeline's channel-name column

**Project:** LTR.Player.Wpf · **Area:** Usability · **Criticality:** moderate · **Effort:** medium

The guide timeline puts the header and every row inside one horizontal `ScrollViewer`, so the channel names
scroll out of view with the programme blocks. Names staying put is most of what makes an EPG readable.

Doing it properly means two vertically synchronised lists, or a custom panel — which is why it was not done
now. It is mostly hidden today because the window is moved with the buttons rather than by scrolling.

---

## Rank 17 — Page the timeline instead of capping it

**Project:** LTR.Player.Wpf, LTR.Persistence · **Area:** Performance · **Criticality:** minor · **Effort:** high

`GuideViewModel.MaximumRows` draws at most 200 channels and says so on screen. The honest fix is to load
rows as they are scrolled into view, which needs the store to page and the timeline to build rows lazily.
Related to rank 15, and worth doing at the same time or not at all.

---

## Rank 7 — Split MainViewModel · done

`MainViewModel` became four types: `SourceManagementViewModel`, `ChannelListViewModel`, a `StatusLine`
shared by all three, and a `MainViewModel` that composes them and owns playback.

What the split settled, and is worth knowing before touching it again:

- The two halves do not know each other. `MainViewModel` implements `ISourceCoordinator`, which is how
  source management gets a catalogue on screen and a stream released without holding a reference to
  either. Its two methods are awaitable rather than events on purpose: `RefreshAsync` has to keep
  `IsBusy` raised until the rebuilt list is showing, and an event handler cannot be awaited.
- The status line is an object, not a property. All three halves write to it, and forwarding one
  property through two owners would have been worse.
- `IsBusy` turned out to belong entirely to source management — nothing else reads it, and no binding
  ever did.
- `PlaySelectedCommand` stayed on `MainViewModel` while the selection it guards on moved to the channel
  list. `[NotifyCanExecuteChangedFor]` cannot cross an object boundary, so `MainViewModel` subscribes to
  the channel list's `PropertyChanged` and notifies the command by hand. Removing that subscription
  fails exactly one test, which was verified rather than assumed.

Rank 10 splits `MainWindow.xaml` along the same seam; the source-management markup already narrows its
data context in one place, so it lifts out as a `UserControl` without further change.

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
