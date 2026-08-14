# Refactoring backlog

Reviewed after M3. Ranks 1–8 are done; what follows is what remains, in the order it was prioritised —
most valuable per unit of effort first.

Ranking rule: criticality against effort. Renumbered at this review, so a rank quoted in an older commit
message will not line up.

**Rank 9 is the only item here with an effect while the player is running.** Everything else is structure,
a missing guard, or a limit that is stated on screen. Deferred deliberately after the M3 review rather than
overlooked: it wants measuring first.

---

## Ranks 1–8 · done

Cleared in one pass after M3. What each settled, and what is worth knowing before touching it again:

- **1 · One test harness for the shell.** `MainViewModelTests` and `GuideViewModelTests` each had their own
  `TestContextBuilder` assembling the same eight-argument view model. `MainViewModelHarness` replaces both,
  and owns the fixed instant the fake clock stands at.
- **2 · A probe result is persisted on every import.** `UpdateCapabilitiesAsync` existed, was tested, and was
  called by nothing: a refresh probed the panel and discarded the answer. It is now
  `UpdateProbeResultAsync`, takes the probed source whole so that deciding which fields a probe owns stays
  in the persistence layer, and also adopts the guide address an M3U playlist declares — which is what stops
  the guide import re-downloading several megabytes of playlist to rediscover it.
- **3 · `ConsoleText`.** `Truncate` had reached three copies and timestamp formatting two. `sources list` now
  labels its timestamps UTC, which they always were.
- **4 · Guide pruning is scoped to its source.** It deleted across every source from one source's import.
  Same effect, wrong reach.
- **5 · One `TestClock`,** linked into the test projects that need one from `src/TestSupport`.
- **6 · A locked companion no longer undoes a quarantine.** The database is already moved by then, so the
  free path the caller needs exists; throwing turned a recoverable startup back into a failed one.
- **7 · The filter reads the row, not the entity.** `ChannelItemViewModel` no longer mirrors its favourite
  flag into the `Channel` it wraps. `ChannelFilter` gained an overload over the three values it looks at,
  which is also what a server-side filter would want.
- **8 · A shell lifetime token.** Cancelled when the window closes and linked into the catalogue load and the
  guide import, so closing mid-load does not wait for seventeen thousand channels.

Two things came out of doing them, both worth remembering:

- Making the catalogue load cancellable **made an unobserved task exception reachable**. Source management
  starts that load without awaiting it, so `ShowCatalogueAsync` now swallows cancellation as well —
  the comment that used to reason "nothing here cancels" was correct until rank 8 and is no longer.
- `Progress<T>` delivers through a synchronisation context, so in a test with none a stage message can land
  *after* the result message an assertion is reading. The guide-import fake reports progress only when asked.

---

## Rank 9 — Now-and-next transfers far more than it shows

**Project:** LTR.Persistence, LTR.Player.Wpf · **Area:** Performance · **Criticality:** moderate · **Effort:** medium

`GetNowAndNextAsync` returns whole `EpgEntry` rows — `Description` included, up to 4,000 characters — for
every matched channel. Against a real subscription that is roughly 9,000 rows **every minute**, to show two
titles per row. The timer also runs when nothing is matched at all.

Proposal: project onto a narrow read model (title, start, stop) and skip the refresh entirely while
`HasGuide` is false. Unmeasured; derived from the figures a 17,000-channel subscription produces.

---

## Rank 10 — MainViewModel does four things

**Project:** LTR.Player.Wpf · **Area:** Maintainability · **Criticality:** moderate · **Effort:** medium

403 lines carrying composition, playback, the guide import lifecycle (start, cancel, report, reload) and the
status wording for all of it.

Proposal: lift the guide import lifecycle into a `GuideImportCoordinator` and delegate. The shell lifetime
token belongs with it.

---

## Rank 11 — Protocol-neutral URL sanitisation

**Project:** LTR.Providers.* · **Area:** Security · **Criticality:** moderate · **Effort:** medium

`UrlSanitizer` is internal to `LTR.Providers.Xtream` and only understands `XtreamSource`. M3U playlist and
guide URLs also carry credentials in their query string and have no sanitiser. Nothing logs one today, so
this is a missing guard rather than an active leak.

Proposal: `ISensitiveUrlSanitizer` in `LTR.Providers.Abstractions`, one implementation per protocol,
resolved through `IProviderRegistry`.

---

## Rank 12 — Stream the Xtream response instead of buffering it

**Project:** LTR.Providers.Xtream · **Area:** Performance · **Criticality:** moderate · **Effort:** medium

`XtreamApiClient.GetStringAsync` reads the whole response into a `string` and then parses a `JsonDocument`
from it — two multi-megabyte copies for a 17,000-channel catalogue.

Proposal: `JsonDocument.ParseAsync` over the content stream. The HTML-detection guard has to move to the
first bytes of the stream, which is the only fiddly part: panels answer with an HTML error page at HTTP 200
and that must still be recognised.

---

## Rank 13 — Split MainWindow.xaml

**Project:** LTR.Player.Wpf · **Area:** Maintainability · **Criticality:** moderate · **Effort:** medium

472 lines holding the add-source form, the channel list, the player overlay and the guide timeline.

The overlay and the timeline must stay inside `VideoView.Content`. `VideoView` hosts a separate native
window over the WPF tree, so a sibling element is invisible behind the video.

---

## Rank 14 — Pin the timeline's channel-name column

**Project:** LTR.Player.Wpf · **Area:** Usability · **Criticality:** moderate · **Effort:** medium

The timeline puts its header and every row inside one horizontal `ScrollViewer`, so the channel names scroll
out of view with the programme blocks. Names staying put is most of what makes an EPG readable.

Doing it properly means two vertically synchronised lists or a custom panel, which is why it was not done
with M3. It is mostly hidden today because the window is moved with the buttons rather than by scrolling.

---

## Rank 15 — Extract the reconciliation diff

**Project:** LTR.Persistence · **Area:** Maintainability · **Criticality:** minor · **Effort:** medium

Roughly a hundred lines inside `LtrDbContext` compute a diff and have no database concern. A
`CatalogueReconciler` could compute it while the context performs the writes, which keeps §3.3.2 intact and
makes the algorithm testable on its own.

---

## Rank 16 — Command classes in the CLI

**Project:** LTR.Cli · **Area:** Maintainability · **Criticality:** minor · **Effort:** medium

`Program.cs` is mostly six `Build*` functions. Proposal: one class per command exposing `Command Build()`,
leaving `Program` as composition only.

---

## Rank 17 — Do not materialise the whole catalogue

**Project:** LTR.Persistence, LTR.Player.Wpf · **Area:** Performance · **Criticality:** moderate · **Effort:** high

Every source switch loads all channels and wraps each in a view model. It works at 17,156. Worth revisiting
only when it demonstrably hurts — filtering and paging in the store rather than in memory.

---

## Rank 18 — Page the timeline instead of capping it

**Project:** LTR.Player.Wpf · **Area:** Performance · **Criticality:** minor · **Effort:** high

`GuideViewModel.MaximumRows` draws at most 200 channels and says so on screen. The honest fix is to load rows
as they are scrolled into view, which needs the store to page and the timeline to build rows lazily. Related
to rank 17, and worth doing at the same time or not at all.
