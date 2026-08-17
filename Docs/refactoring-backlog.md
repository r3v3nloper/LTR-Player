# Refactoring backlog

**Renumbered after M6.** All six milestones are done and merged; what follows is everything that remains,
ranked 1–9 from scratch. Numbers quoted in commit messages up to and including the M6 merge belong to the
older schemes — the mapping is at the bottom, under [Done](#done).

**Rank 1 is done** (see [Done](#after-m6--protocol-neutral-url-sanitisation)); the eight that remain **keep
the numbers this list gave them**, so it now starts at 2. Deliberately not closed up: a third numbering
scheme would cost more than a gap in the sequence, and every rank quoted anywhere still resolves to the item
it named.

Ranking rule: criticality against effort, most valuable per unit of effort first.

Everything left is structure, a missing guard, or a limit that is stated on screen. **Nothing left has an
effect while the player is running**, which has been true since the post-M4 review and is worth re-checking
rather than assuming next time.

## Before starting one of these

- `dotnet test LTR-Player.slnx` — 648 tests, all passing on `main`. A refactor should not move that number;
  if it does, either the change is not a refactor or a test was measuring the implementation.
- **Close the player first.** MSBuild cannot replace locked DLLs and the error arrives *after* a successful
  compile, so it reads as a broken build. `build/publish.ps1` refuses outright.
- **Start the app after anything that touches dependency injection.** The compiler cannot see a missing
  registration, and the container holds the session whose disposal releases the provider connection.
- The two reviews before this one both found the *test double* wrong rather than the code. When a mutation
  survives, suspect the fake before the assertion.

---

## Rank 2 — Small duplications and loose ends

**Project:** various · **Area:** Maintainability · **Criticality:** minor · **Effort:** low

Individually not worth a rank; worth clearing in one pass:

- Both CLI play-tests duplicate open → hold → release → report. One `StreamHoldTest` collaborator. M5 and M6
  each widened the gap — the live one prints state transitions and cannot seek, the film one seeks and prints
  no transitions.
- `MovieItemViewModel` and `SeriesItemViewModel` duplicate the year · rating · genre assembly.
- `StreamFormat.ProgressiveFile` throws from `ToUrlExtension`. No caller passes it, so it is a latent trap.
- Taking an entry off the continue-watching list stamps `LastWatchedUtc` with the moment it was removed.
  Only the CLI shows it.

## Rank 3 — A film's detail is fetched again on every viewing when the panel has none

**Project:** LTR.Catalogue · **Area:** Performance · **Criticality:** minor · **Effort:** low

`VodItem.HasDetail` is set only when a detail response arrives, so a panel that answers with nothing leaves
it unset and selecting that film asks again every time. The current behaviour is deliberate as far as it
goes — an empty answer today is not proof of an empty answer next week — but nothing distinguishes "never
asked" from "asked, and there is nothing".

Proposal: record when the detail was last attempted and do not retry within a day. `Series` already has
`DetailFetchedUtc` for the same purpose.

## Rank 4 — Stream the Xtream response instead of buffering it

**Project:** LTR.Providers.Xtream · **Area:** Performance · **Criticality:** moderate · **Effort:** medium

`XtreamApiClient.GetStringAsync` reads the whole response into a `string` and then parses a `JsonDocument`
from it — two multi-megabyte copies. Worse since M4: the film listing is larger than the channel listing, and
the subscription this was built against lists 66,447 films.

Proposal: `JsonDocument.ParseAsync` over the content stream. The HTML-detection guard has to move to the
first bytes of the stream, which is the only fiddly part: panels answer with an HTML error page at HTTP 200
and that must still be recognised.

## Rank 5 — Pin the timeline's channel-name column

**Project:** LTR.Player.Wpf · **Area:** Usability · **Criticality:** moderate · **Effort:** medium

The timeline puts its header and every row inside one horizontal `ScrollViewer`, so the channel names scroll
out of view with the programme blocks. Names staying put is most of what makes an EPG readable.

Doing it properly means two vertically synchronised lists or a custom panel. It is mostly hidden today
because the window is moved with the buttons rather than by scrolling. Stated on screen; not a gap in M3.

## Rank 6 — Command classes in the CLI

**Project:** LTR.Cli · **Area:** Maintainability · **Criticality:** minor · **Effort:** medium

`Program.cs` is 380 lines of `Build*` functions. Proposal: one class per command exposing `Command Build()`,
leaving `Program` as composition only.

## Rank 7 — Extract the reconciliation diff

**Project:** LTR.Persistence · **Area:** Maintainability · **Criticality:** moderate · **Effort:** high

`LtrDbContext.cs` and `LtrDbContext.Vod.cs` hold roughly 250 non-comment lines that compute a diff and have
no database concern — `ReconcileSeasons` alone is about 90. A `CatalogueReconciler` could compute it while
the context performs the writes, which keeps §3.3.2 intact and makes the algorithm testable on its own.

## Rank 8 — Do not materialise the whole channel list

**Project:** LTR.Persistence, LTR.Player.Wpf · **Area:** Performance · **Criticality:** moderate · **Effort:** high

Every source switch loads all 17,156 channels and wraps each in a view model. It works. The film section did
not inherit the approach — at 66,447 films it was not viable, so `SearchMoviesAsync` filters and counts in
the database and the section shows a bounded page. That is the shape this rank proposes, now built and in
use; what remains is applying it here, where the guide's now/next join makes it harder.

Settle one behavioural difference first: the film search matches with SQLite's `LIKE`, case-insensitive for
ASCII only, where `CatalogueFilter` in memory is fully case-insensitive.

**Carries a second item with it.** `ChannelListViewModel.SelectAdjacent` enumerates the whole filtered view
to find the current row's neighbour, so one zap key press walks up to 17,156 rows — the same enumeration
`VisibleChannels` already does for the guide. Cheap to fix on its own and deliberately not ranked separately,
because if the list stops being materialised at all it changes shape entirely. Noted so the cost is known
rather than discovered.

## Rank 9 — Page the timeline instead of capping it

**Project:** LTR.Player.Wpf · **Area:** Performance · **Criticality:** minor · **Effort:** high

`GuideViewModel.MaximumRows` draws at most 200 channels and says so on screen. The honest fix is to load rows
as they are scrolled into view, which needs the store to page and the timeline to build rows lazily. Related
to rank 8, and worth doing at the same time or not at all.

---

## Done

Ranks quoted in older commit messages resolve here. **The mapping from the post-M4 scheme to the current
one:** 8→1, 13→2, 14→3, 9→4, 11→5, 16→6, 12→7, 17 and 20→8, 18→9. Everything else on that list is below.

### After M6 — protocol-neutral URL sanitisation

**Rank 1.** `ISensitiveUrlSanitizer` in LTR.Providers.Abstractions, one implementation per protocol,
resolved through `IProviderRegistry` — the proposal as written, with three things settled by doing it:

- **The two protocols need different *kinds* of rule, not the same rule with different inputs.** Xtream
  knows its secrets by value, so it replaces them wherever they occur — including the path segments a stream
  address puts them in — and leaves the rest of the query string readable, because `action=get_vod_info` is
  what makes a logged address worth logging. A playlist source holds no credentials at all: whatever the
  provider issued is already inside the address the user pasted, under a parameter name nothing here knows.
  So that rule is structural — every query value goes, the names stay.
- **A playlist's path is deliberately not redacted, and that is the known limit.** Providers exist that put
  credentials in path segments, but with no credentials to compare against, nothing distinguishes such a
  segment from a route; redacting the path wholesale would leave an address with no diagnostic value at all.
  The userinfo component (`user:password@host`) *is* removed, for every protocol, by the base class — it is
  the one form that is unambiguous without knowing the protocol.
- **The guard did not stay dormant.** The rank said "nothing logs one today", and it was written so the guard
  would exist first. Two callers arrived with it: `M3uContentProvider` now names the sanitised playlist
  address when the document cannot be fetched — the source's own name says nothing about *why* — and the
  CLI's `resolve` command dropped its own copy of the masking, which was the third.

`LTR.Providers.Tests` is new, and is the first test project that composes the container the way the
applications do. The registry's other four resolutions are still untested; what made this one worth a
project is that the mistake it guards against — a component registered for one protocol and forgotten for
the other — is the one the compiler cannot see, and the standing instruction for it was "start the app and
read the log".

### After M6 — the two interface splits

**Post-M4 rank 10 · `ICatalogueStore` had nineteen members.** Split into `ISourceStore` (4),
`ILiveCatalogue` (2), `IGuideCatalogue` (4), `IVodCatalogue` (6) and `IWatchProgressStore` (3), grouped by
what consumes them rather than by the tables behind them.

Two placements are the only ones worth arguing about again:

- **`GetCategoriesAsync` is on `ISourceStore`,** not on either catalogue. It takes its kind as a parameter
  because a panel numbers categories per section, so a method deliberately indifferent to the section belongs
  with the source — the alternative was the same method on two interfaces.
- **Now-and-next is on `IGuideCatalogue`,** because it is guide data. That is why the channel list takes three
  faces: it lists channels, offers categories and decorates rows with what is on, and those are three
  different things imported on three different schedules.

`ICatalogueStore` was **removed** rather than kept as an umbrella inheriting the five, because an umbrella is
what the next consumer would take. The catalogue's own tests resolve `CatalogueStore` directly instead, which
is more honest about what they are: integration tests of this layer over real SQLite rather than consumers of
an abstraction. One class still implements all five and one instance answers all five; every method is the
same two lines over the same unit of work, so splitting the implementation would buy five files (§2.16).

**Post-M5-review rank 9 · `IPlaybackSession` had sixteen members.** Split into the session — `Current`,
`StateChanged`, `SwitchToAsync`, `StopAsync` — and `IPlaybackTransport`, holding the twelve members that act
on a stream already open. The session does not inherit the transport, and that is the whole point:
`PlayerOverlayViewModel` takes the transport alone, so it *cannot* open or release a provider connection.
M5 argued that division in prose, and prose is not enforcement.

**The two splits did not pay off equally, and the difference is the lesson.** The catalogue's consumption was
genuinely disjoint — `WatchProgressRecorder` declared nineteen members to use three. Playback's is not: three
of four consumers need both halves and now take two parameters, because everything that opens a stream also
observes one. And **neither split shrank a test double**, which was half of what the catalogue rank asked for;
the window's `FakeCatalogueStore` still implements everything because the shell harness builds every view
model. Before splitting an interface, check whether consumption is actually disjoint.

Noticed on the way past: **`IPlaybackSession.Current` has no production caller.** Kept deliberately, with a
comment saying so — it states the guarantee the interface exists for, and it is what the session's own tests
assert to prove a stream was let go.

### By M6 — post-M4 rank 19, nothing the player controls is remembered

Volume, mute and aspect ratio now live in `settings.json` beside the database. The overlay writes them into
the shared settings object as the viewer changes them and the shell persists it on the way out — saving on
change would have written a file per pixel of slider drag.

Parked for M6 on purpose, and the wait paid: the file it needed existed by then, rather than being invented
for three values.

### In the review after M5 — four items, all found in M5's own work

- **The shell view model had regrown past the size that split it.** 395 code lines at the M4 merge, **483
  after M5** — the same figure post-M4 rank 4 quoted as its *pre*-refactor size, reached the same way all
  three times: it is the only place that can reach everything, so everything lands in it. The 64-line
  keystroke switch became `PlayerActions`, and the class sits at **439**.

  Not back to 395, and the remainder is deliberate. What is left that M5 added is coordination that genuinely
  needs the list, playback and the section selection at once, which is the class's whole reason to exist. The
  next real candidate is the block of six `PropertyChanged` handlers, about ninety lines that exist only to
  forward notifications across object boundaries — coherent enough to name, and left alone because getting it
  wrong reproduces the defect class this repository has shipped three times. **Measure this class at the start
  of a milestone, not the end.**

- **`PlayerOverlay.Sample()` sat outside the exception guard** in `SamplePlaybackAsync`, reached from an
  `async void` timer tick. It rebuilds the track menus, so anything escaping it became a dialog from the
  dispatcher's unhandled handler rather than a log line — twice a second. One line.

- **Two sources of truth for network caching.** `ToArguments()` emitted a global `--network-caching` while
  `PlayAsync` set a per-media one on every stream, so the startup argument read as the effective setting and
  nothing ever consumed it. The global one is gone; the per-stream values are the only statement.

  **Still worth verifying against a real panel**, because the fallback if a per-input option were ever ignored
  is now LibVLC's own default rather than ours. The open-duration log line is the check — `Docs/verification.md`
  §4.

- **The two playback test doubles had drifted on the seek rule.** `FakeMediaEngine` left its position where it
  was after a seek; `FakePlaybackSession` moved it. Neither was load-bearing yet, which is exactly when to fix
  it. Both now model the same rule, and the one respect in which they differ on purpose — when a released
  stream forgets its position — is commented in both.

  **The shared-collaborator refactor this was proposed as did not survive contact.** Both doubles implement
  interfaces that *require* every one of those members, so only the one-line bodies could be shared, and the
  release semantics genuinely differ by layer. A linked file would have removed six lines of duplication and
  added an indirection plus forty call-site changes (§2.16). Naming the invariant beat extracting it.

### By M5 — post-M4 rank 15, progress at the end of a film

`PlaybackStateChangedEventArgs` carries a `PlaybackStopReason`, and that was the whole difficulty: a film
that plays to its own end and one the viewer stopped reach the identical state, and the identical transition
occurs in the middle of every channel change where progress was recorded a moment earlier. Acting on the wrong
one would overwrite a deliberate position with whatever the engine reported while tearing down.

LibVLC makes the distinction available for free — End Reached fires before Stopped — and the existing
deduplication in `SetState` swallows the second, so the reason set by the first is the one that reaches the
coordinator. `PlaybackCoordinator` flags it from the engine thread and `SampleAsync` closes the stream off on
the next tick, since what follows is a database write and three list reloads.

**Worth knowing:** the mutation check on this found a hole in the test double, not in the code.
`FakePlaybackSession` went straight from idle to playing, so no test could have caught treating every stop as
an end of stream. It now releases before opening, as the real session does unconditionally — a fake that skips
an invariant hides exactly the bug the invariant exists for.

### After M4 — post-M4 ranks 1–7

- **1 · Every import stage has wording.** `SourceImportStage.FetchingVod` arrived with the film catalogue and
  the CLI was taught to print it while the window's switch was not, so the longest step of an import on a
  subscription of sixty thousand films read "Working...". The wording is now driven off the enum by a test,
  so the next stage added fails there rather than reading as the fallback. **This pattern was reused twice
  since** — by `SourceImportStage` itself and by M6's `StreamFailureReason`.
- **2 · One watch-progress recorder.** `WatchProgressRecorder` moved from the window to LTR.Catalogue.
  Nothing in it touched WPF, and the headless play-test had grown a second copy of the classification — so
  "how much counts as watched" could have come out differently on screen than from the command line.
- **3 · The tests no longer spin.** `MainViewModel` follows the work it starts from a property change through
  `PendingWork`, so a test can ask whether the shell has finished reacting instead of yielding eight times
  and hoping.
- **4 · Playback has one owner.** `PlaybackCoordinator` holds building an address, opening a stream, wording
  the failure, following the position and writing it down. That single ownership is the point rather than a
  size argument: a subscription permits very few concurrent connections, and two places able to start a
  stream is how one gets left open.
- **5 · One catalogue section, twice.** `CatalogueSectionViewModel<TRow>` holds the category picker, the
  search, the bounded page and the count wording; the film and series sections supply only what differs.
  They had the whole shape twice — including the selection-ordering rule whose absence produced the same
  blank-picker defect in both, because the code was in both.
- **6 · The series write path no longer fetches a cartesian product.** `SaveSeriesDetailAsync` includes two
  collection navigations and now splits them, as the read path already did. Verified against the real panel:
  the EF warning that appeared on every series fetch is gone.
- **7 · Now-and-next asks for three columns, and only when there is a guide.**

  **Measured, and the prediction was half wrong.** Against a real 42,000-programme guide with 4,531 matched
  channels the query takes the same ~25 ms either way: the database is in-process, so "transferring" a column
  is a memory copy and not a round trip. The projection's saving is allocation — nine thousand objects a
  minute, some carrying four thousand characters nobody reads — which matters to a window left open all
  evening rather than to any single refresh. The skip is the half with a measurable effect: for a subscription
  with no guide imported it goes from 25 ms and nine thousand objects every minute to nothing.

  Only the timer skips. The catalogue load and the post-import reload call the channel list directly, which is
  what lets a guide that has just arrived be picked up at all — there is a test for that specifically.

Worth knowing before touching these again:

- The section base's `ShowAsync` detaches its source **before** clearing the criteria and selects the
  catch-all category **after** refilling the picker. Both orderings are load-bearing and both are commented;
  getting either wrong is invisible in the list and visible in the picker.
- `PlaybackCoordinator.ProgressRecorded` is a continuation the shell supplies, as
  `GuideImportCoordinator.Start` takes one. A stored position appears in three lists, and knowing about all
  three is the shell's business, not playback's.

### By M4 itself

The shell view model's guide-import lifecycle and the single-file window. Neither was tidying — both blocked
the milestone, because the view model could not take three more sections at 400 lines and `MainWindow.xaml`
could not take three more lists at 470.

### After M3 — eight items

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
