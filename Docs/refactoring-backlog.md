# Refactoring backlog

Reviewed after M4. **Renumbered at this review**, as at the last one — a rank quoted in an older commit
message will not line up.

Ranking rule: criticality against effort, most valuable per unit of effort first.

Cleared so far:

- **After M3:** eight items, listed at the bottom for the reasoning that came out of them.
- **By M4 itself:** the shell view model's guide-import lifecycle and the single-file window. Neither was
  tidying — both blocked the milestone, because the view model could not take three more sections at 400
  lines and `MainWindow.xaml` could not take three more lists at 470.
- **After M4:** ranks 1–6 below.

**Rank 7 is the only item here with an effect while the player is running.** Everything else is structure, a
missing guard, or a limit that is stated on screen.

---

## Ranks 1–6 · done after M4

- **1 · Every import stage has wording.** `SourceImportStage.FetchingVod` arrived with the film catalogue and
  the CLI was taught to print it while the window's switch was not, so the longest step of an import on a
  subscription of sixty thousand films read "Working...". The wording is now driven off the enum by a test,
  so the next stage added fails there rather than reading as the fallback.
- **2 · One watch-progress recorder.** `WatchProgressRecorder` moved from the window to LTR.Catalogue.
  Nothing in it touched WPF, and the headless play-test had grown a second copy of the classification — so
  "how much counts as watched" could have come out differently on screen than from the command line.
- **3 · The tests no longer spin.** `MainViewModel` follows the work it starts from a property change through
  `PendingWork`, so a test can ask whether the shell has finished reacting instead of yielding eight times
  and hoping. It still keeps nothing in the application sense: each reload handles its own failures and
  nothing waits on one.
- **4 · Playback has one owner.** `PlaybackCoordinator` holds building an address, opening a stream, wording
  the failure, following the position and writing it down. The shell view model went from 483 to 391
  non-comment lines and no longer touches `IPlaybackSession`. That single ownership is the point rather than
  a size argument: a subscription permits very few concurrent connections, and two places able to start a
  stream is how one gets left open.
- **5 · One catalogue section, twice.** `CatalogueSectionViewModel<TRow>` holds the category picker, the
  search, the bounded page and the count wording; the film and series sections supply only what differs.
  They had the whole shape twice — including the selection-ordering rule whose absence produced the same
  blank-picker defect in both, because the code was in both.
- **6 · The series write path no longer fetches a cartesian product.** `SaveSeriesDetailAsync` includes two
  collection navigations and now splits them, as the read path already did. Verified against the real panel:
  the EF warning that appeared on every series fetch is gone. The usual caveat about split queries observing
  data changed between statements does not apply here — single-writer application, and the guide import does
  not touch series.

Worth knowing before touching these again:

- The section base's `ShowAsync` detaches its source **before** clearing the criteria and selects the
  catch-all category **after** refilling the picker. Both orderings are load-bearing and both are commented;
  getting either wrong is invisible in the list and visible in the picker.
- `PlaybackCoordinator.ProgressRecorded` is a continuation the shell supplies, as
  `GuideImportCoordinator.Start` takes one. A stored position appears in three lists, and knowing about all
  three is the shell's business, not playback's.

---

## Rank 7 — Now-and-next transfers far more than it shows

**Project:** LTR.Persistence, LTR.Player.Wpf · **Area:** Performance · **Criticality:** moderate · **Effort:** medium

`GetNowAndNextAsync` returns whole `EpgEntry` rows — `Description` included, up to 4,000 characters — for
every matched channel. Against a real subscription that is roughly 9,000 rows **every minute**, to show two
titles per row. The timer also runs when nothing is matched at all.

Proposal: project onto a narrow read model (title, start, stop) and skip the refresh entirely while
`HasGuide` is false. Unmeasured; derived from the figures a 17,000-channel subscription produces.

## Rank 8 — Protocol-neutral URL sanitisation

**Project:** LTR.Providers.* · **Area:** Security · **Criticality:** moderate · **Effort:** medium

`UrlSanitizer` is internal to `LTR.Providers.Xtream` and only understands `XtreamSource`. M3U playlist and
guide URLs also carry credentials in their query string and have no sanitiser. Nothing logs one today, so
this is a missing guard rather than an active leak.

Proposal: `ISensitiveUrlSanitizer` in `LTR.Providers.Abstractions`, one implementation per protocol,
resolved through `IProviderRegistry`.

## Rank 9 — Stream the Xtream response instead of buffering it

**Project:** LTR.Providers.Xtream · **Area:** Performance · **Criticality:** moderate · **Effort:** medium

`XtreamApiClient.GetStringAsync` reads the whole response into a `string` and then parses a `JsonDocument`
from it — two multi-megabyte copies. Worse since M4: the film listing is larger than the channel listing.

Proposal: `JsonDocument.ParseAsync` over the content stream. The HTML-detection guard has to move to the
first bytes of the stream, which is the only fiddly part: panels answer with an HTML error page at HTTP 200
and that must still be recognised.

## Rank 10 — `ICatalogueStore` has eighteen members

**Project:** LTR.Catalogue.Abstractions · **Area:** Maintainability · **Criticality:** moderate · **Effort:** medium

Sources, live channels, the guide, films, series and watch progress in one interface. Every fake has to
implement all of it (§2.5), which is why the window's test double is the largest file in its project.

Proposal: split into `ISourceStore` / `ILiveCatalogue` / `IGuideCatalogue` / `IVodCatalogue`, composed where
more than one is needed.

## Rank 11 — Pin the timeline's channel-name column

**Project:** LTR.Player.Wpf · **Area:** Usability · **Criticality:** moderate · **Effort:** medium

The timeline puts its header and every row inside one horizontal `ScrollViewer`, so the channel names scroll
out of view with the programme blocks. Names staying put is most of what makes an EPG readable.

Doing it properly means two vertically synchronised lists or a custom panel. It is mostly hidden today
because the window is moved with the buttons rather than by scrolling.

## Rank 12 — Extract the reconciliation diff

**Project:** LTR.Persistence · **Area:** Maintainability · **Criticality:** moderate · **Effort:** high

`LtrDbContext.cs` and `LtrDbContext.Vod.cs` hold roughly 250 non-comment lines that compute a diff and have
no database concern — `ReconcileSeasons` alone is about 90. A `CatalogueReconciler` could compute it while
the context performs the writes, which keeps §3.3.2 intact and makes the algorithm testable on its own.

## Rank 13 — Small duplications and loose ends

**Project:** various · **Area:** Maintainability · **Criticality:** minor · **Effort:** low

Individually not worth a rank; worth clearing in one pass:

- Both CLI play-tests duplicate open → hold → release → report. One `StreamHoldTest` collaborator.
- `MovieItemViewModel` and `SeriesItemViewModel` duplicate the year · rating · genre assembly.
- `StreamFormat.ProgressiveFile` throws from `ToUrlExtension`. No caller passes it, so it is a latent trap.
- `vod play-test` prints no state transitions; the live one does, and those lines are how a stalled open is
  told from a refused one.
- Taking an entry off the continue-watching list stamps `LastWatchedUtc` with the moment it was removed.
  Only the CLI shows it.

## Rank 14 — A film's detail is fetched again on every viewing when the panel has none

**Project:** LTR.Catalogue · **Area:** Performance · **Criticality:** minor · **Effort:** low

`VodItem.HasDetail` is set only when a detail response arrives, so a panel that answers with nothing leaves
it unset and selecting that film asks again every time. The current behaviour is deliberate as far as it
goes — an empty answer today is not proof of an empty answer next week — but nothing distinguishes "never
asked" from "asked, and there is nothing".

Proposal: record when the detail was last attempted and do not retry within a day. `Series` already has
`DetailFetchedUtc` for the same purpose.

## Rank 15 — Nothing records progress when a film reaches its own end

**Project:** LTR.Player.Wpf · **Area:** Correctness · **Criticality:** minor · **Effort:** low

Progress is written when playback is switched, stopped or the window closes. A film that plays to its end
and sits there is none of those, so it stays on the continue-watching list until the next of them happens —
usually the window closing, which then records it correctly as finished.

Nothing is lost; the list is briefly wrong. Fixing it means recording when the session reports `Stopped`
after having been `Playing`, and the same transition occurs in the middle of every channel change, where
progress has already been recorded a moment earlier. Distinguishing the two needs the session to say *why*
it stopped, which is a change to `IPlaybackSession` and wants doing with M5's OSD work.

## Rank 16 — Command classes in the CLI

**Project:** LTR.Cli · **Area:** Maintainability · **Criticality:** minor · **Effort:** medium

`Program.cs` is 485 lines of `Build*` functions. Proposal: one class per command exposing `Command Build()`,
leaving `Program` as composition only.

## Rank 17 — Do not materialise the whole channel list

**Project:** LTR.Persistence, LTR.Player.Wpf · **Area:** Performance · **Criticality:** moderate · **Effort:** high

Every source switch loads all 17,156 channels and wraps each in a view model. It works. The film section did
not inherit the approach — at 66,447 films it was not viable, so `SearchMoviesAsync` filters and counts in
the database and the section shows a bounded page. That is the shape this rank proposes, now built and in
use; what remains is applying it here, where the guide's now/next join makes it harder.

Settle one behavioural difference first: the film search matches with SQLite's `LIKE`, case-insensitive for
ASCII only, where `CatalogueFilter` in memory is fully case-insensitive.

## Rank 18 — Page the timeline instead of capping it

**Project:** LTR.Player.Wpf · **Area:** Performance · **Criticality:** minor · **Effort:** high

`GuideViewModel.MaximumRows` draws at most 200 channels and says so on screen. The honest fix is to load rows
as they are scrolled into view, which needs the store to page and the timeline to build rows lazily. Related
to rank 17, and worth doing at the same time or not at all.

---

## Cleared after M3

What each settled, and what is worth knowing before touching it again:

- **One test harness for the shell.** `MainViewModelTests` and `GuideViewModelTests` each had their own
  builder assembling the same view model. `MainViewModelHarness` replaces both, and owns the fixed instant
  the fake clock stands at.
- **A probe result is persisted on every import.** `UpdateCapabilitiesAsync` existed, was tested, and was
  called by nothing: a refresh probed the panel and discarded the answer. It is now `UpdateProbeResultAsync`,
  takes the probed source whole, and also adopts the guide address an M3U playlist declares.
- **`ConsoleText`.** `Truncate` had reached three copies and timestamp formatting two.
- **Guide pruning is scoped to its source.** It deleted across every source from one source's import.
- **One `TestClock`,** linked into the test projects that need one from `src/TestSupport`.
- **A locked companion no longer undoes a quarantine.** Throwing turned a recoverable startup into a failed
  one.
- **The filter reads the row, not the entity.** `ChannelItemViewModel` no longer mirrors its favourite flag
  into the `Channel` it wraps.
- **A shell lifetime token,** cancelled when the window closes and linked into the catalogue load and the
  guide import.

Two things came out of doing them, both still true:

- Making the catalogue load cancellable **made an unobserved task exception reachable**. Source management
  starts that load without awaiting it, so `ShowCatalogueAsync` swallows cancellation as well.
- `Progress<T>` delivers through a synchronisation context, so in a test with none a stage message can land
  *after* the result message an assertion is reading. The guide-import fake reports progress only when asked.
