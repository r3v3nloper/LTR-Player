# Refactoring backlog

**Renumbered in the review after the URL-sanitisation rank**, which finished the post-M6 list's rank 1 and
found four items to add. Numbers quoted in commit messages belong to whichever scheme was current when they
were written — the mappings are at the bottom, under [Done](#done).

Renumbered then rather than appended, unlike the removal before it: a *removed* item leaves a gap that costs
less than a new scheme, but four items belonging in the middle of the ranking cannot be appended without the
number ceasing to mean the rank, which is the only thing this list is for.

**Ranks 1–6 are done** — see [Done](#in-the-same-session-as-the-renumbering--ranks-16). **The rest keep their
numbers**, so the list starts at 7, by that same rule — and rank 13, found while verifying rank 6, sits where
it belongs by value rather than by number.

Ranking rule: criticality against effort, most valuable per unit of effort first.

Everything left is structure, a limit that is stated on screen, or a capability that is missing rather than
wrong. **Nothing left has an effect while the player is running**, which has been true since the post-M4
review and is worth re-checking rather than assuming next time.

## Before starting one of these

- `dotnet test LTR-Player.slnx` — 687 tests, all passing on `main`. A refactor should not move that number;
  if it does, either the change is not a refactor or a test was measuring the implementation.
- **Close the player first.** MSBuild cannot replace locked DLLs and the error arrives *after* a successful
  compile, so it reads as a broken build. `build/publish.ps1` refuses outright.
- **Start the app after anything that touches dependency injection.** The compiler cannot see a missing
  registration, and the container holds the session whose disposal releases the provider connection.
- The two reviews before this one both found the *test double* wrong rather than the code. When a mutation
  survives, suspect the fake before the assertion.

---

## Rank 7 — Pin the timeline's channel-name column

**Project:** LTR.Player.Wpf · **Area:** Usability · **Criticality:** moderate · **Effort:** medium

The timeline puts its header and every row inside one horizontal `ScrollViewer`, so the channel names scroll
out of view with the programme blocks. Names staying put is most of what makes an EPG readable.

Doing it properly means two vertically synchronised lists or a custom panel. It is mostly hidden today
because the window is moved with the buttons rather than by scrolling. Stated on screen; not a gap in M3.

## Rank 8 — Command classes in the CLI, and the handler that outgrew one

**Project:** LTR.Cli · **Area:** Maintainability · **Criticality:** moderate · **Effort:** medium

`Program.cs` is 380 lines of `Build*` functions. Proposal: one class per command exposing `Command Build()`,
leaving `Program` as composition only.

**Wider than this rank said when it was written.** The CLI's largest file is not `Program.cs` but
`VodCommandHandler` at 412 code lines, and it takes **ten constructor dependencies** — because it does four
jobs: listing, showing one item, forgetting a stored position, and play-testing. The dependencies already
separate along those lines, which is what makes the split obvious rather than a judgement call. Raised from
minor to moderate for it.

## Rank 9 — `resolve` cannot address a stored playlist source

**Project:** LTR.Cli · **Area:** Usability · **Criticality:** minor · **Effort:** low

`resolve` takes `--url/--user/--pass`, so it only ever addresses an Xtream panel. A playlist source's channel
address cannot be printed headlessly at all, which is also why the M3U sanitiser has no verification through
this command — the one it does have came from provoking a failed playlist fetch.

Proposal: accept `--source-id` and go through the registry, as `vod play-test` does.

**Ranked by usefulness rather than by effort**, and therefore below items that cost more: this adds a
capability rather than restructuring one, so it is the only entry here that is not a refactor.

## Rank 13 — A short Xtream credential redacts the rest of the address

**Project:** LTR.Providers.Xtream · **Area:** Security · **Criticality:** minor · **Effort:** low

`XtreamUrlSanitizer` replaces its secrets wherever they occur, which is what covers the path form. With a
short or common credential it also replaces them where they are not secrets: a username of `x` against
`hd-max.org` logs `http://hd-ma***.org:8080/pla***er_api.php` — host and action both mangled, and those are
the whole reason the address is logged. Seen while verifying rank 6 with dummy credentials, but panels do
issue two-character trial usernames.

Over-redaction is the safe direction, so this is diagnostics rather than a leak. Proposal: replace a
credential where it is a whole query value or a whole path segment, which is where Xtream actually puts it,
rather than as any substring.

**Numbered 13 because it was found mid-session, not because it ranks there** — on value per effort it belongs
between 9 and 10. It will move when this list is next renumbered; the number is a name until then.

## Rank 10 — Extract the reconciliation diff

**Project:** LTR.Persistence · **Area:** Maintainability · **Criticality:** moderate · **Effort:** high

`LtrDbContext.cs` and `LtrDbContext.Vod.cs` hold roughly 250 non-comment lines that compute a diff and have
no database concern — `ReconcileSeasons` alone is about 90. A `CatalogueReconciler` could compute it while
the context performs the writes, which keeps §3.3.2 intact and makes the algorithm testable on its own.

## Rank 11 — Do not materialise the whole channel list

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

## Rank 12 — Page the timeline instead of capping it

**Project:** LTR.Player.Wpf · **Area:** Performance · **Criticality:** minor · **Effort:** high

`GuideViewModel.MaximumRows` draws at most 200 channels and says so on screen. The honest fix is to load rows
as they are scrolled into view, which needs the store to page and the timeline to build rows lazily. Related
to rank 11, and worth doing at the same time or not at all.

---

## Done

Ranks quoted in older commit messages resolve here, through two mappings.

**Post-M6 scheme → current scheme:** 2→2, 3→4, 4→6, 5→7, 6→8, 7→10, 8→11, 9→12. Its rank 1 is done and is
the first entry below. Ranks 1, 3, 5 and 9 of the current scheme are new in this review and appear in no
older one.

**Post-M4 scheme → post-M6 scheme:** 8→1, 13→2, 14→3, 9→4, 11→5, 16→6, 12→7, 17 and 20→8, 18→9. Everything
else on that list is below.

### In the same session as the renumbering — ranks 1–6

Six ranks, cleared in one sitting. What is worth carrying forward:

- **Rank 6 · the Xtream response is streamed, and the fiddly part was not the one the rank named.** The
  HTML-at-HTTP-200 guard moved to the first bytes as expected: a `PipeReader` peeks 512 bytes, nothing is
  advanced past, and `JsonDocument.ParseAsync` then reads the document from its first byte. The
  multi-megabyte `string` is gone — UTF-16, so it was twice the size of the response it held.

  **What the rank did not foresee is the timeout.** Buffering put the body inside the resilience pipeline's
  30-second attempt; streaming takes it outside, and a panel that sends headers and then stalls would have
  held an import until the window closed. The read now carries a deadline of its own, and `XtreamTimeouts`
  exists so that deadline and the pipeline's cannot drift apart — the figures were in the service
  registration, where the client could not see them.

  **A mutation proved a comment wrong, which is why the byte-order-mark handling now says something else.**
  Disabling the mark skip changed nothing: `JsonDocument`'s stream overloads skip one themselves. What the
  skip is actually for is the *inspection* — a mark is not whitespace to .NET, so it sits in front of
  `<html` and an error page would be reported as malformed JSON instead of as a panel serving its
  maintenance page. The test that pins it is a mark in front of an HTML page, and that one does fail without
  the skip.

  Verified against the real panel by a full import: 17,283 channels, 66,529 films, 11,000 series. The film
  listing is the largest response this client ever reads, which is the case the rank was written for.

- **Rank 5 · the shell's notification forwarding, and the premise that turned out to be half wrong.** The
  rank called it "about ninety lines that exist only to forward notifications". Reading it first showed that
  it was three concerns, not one: forwarding a command guard, forwarding a computed property, and *reacting*
  to a change by starting work or revealing the overlay. Extracting "the notification forwarding" wholesale
  would have dragged the reactions along, and the reactions are the part that owns the lifetime token.

  `CrossObjectNotifications` now holds the declarative half as a table — nine forwards, each one line to
  three — and the three genuine reactions stayed as handlers in the shell. The real win is not the table but
  **the one rule the eight handlers each repeated: an empty or null property name means every property.** It
  lives in one place, and it now has tests, which it never had — no section raises a wholesale reset today, so
  it could only be covered by testing the mechanism directly.

  **The size win is small and worth stating plainly:** `MainViewModel` went from 466 to 438 code lines. A
  declarative registration costs nearly what the handler it replaces did. Anyone reaching for this rank again
  expecting a hundred lines back will not find them — the reason to do it was the single rule and the visible
  set, and the honest measure of the outcome is those and not the count.

  **The order the registrations are made in is load-bearing.** The forwards subscribe before the reaction
  handlers, so a command whose guard the work may change is still notified first — the ordering the one
  handler per section used to guarantee implicitly. It is commented at both ends.

  Done in two commits on purpose: **four of the forwards had only their value asserted**, which is the
  assertion that cannot catch this defect class, so the missing notification tests went in first against the
  unchanged code. Two of them needed the fake's gate to be meaningful at all — the film's detail otherwise
  lands inside the selection's own setter, and the guide import's two announcements collapse into one.

- **Rank 4 · an empty answer is an answer; an unreachable panel is not.** `VodItem.DetailAttemptedUtc`
  records the asking, `HasDetail` still records the arriving, and `NeedsDetailFetch(asOf)` takes an empty
  answer at its word for `DetailRetryInterval` — a day. The distinction that had to be built for it is inside
  `VodDetailService`: `TryFetchAsync` returned the same `null` for "the panel has nothing" and "the panel
  could not be reached", and recording the second as an answer would suppress the retry for a day over a
  momentary outage. It now reports whether the provider answered at all.

  `NeedsDetailFetch` is a **method** rather than a computed property, which also keeps it off the schema
  without the explicit `Ignore` every computed property in this model needs — `movie.Ignore(Duration)`,
  `series.Ignore(HasCurrentDetail)` and three more.

  **The migration is the first one to alter a table holding user data.** It generated a plain `ADD COLUMN`
  rather than a rebuild, and `MigrationUpgradeTests` carries a case anyway with a resume position in the row,
  because "it only adds a column" is a claim about the generated migration and not about the model. Applied
  to the real 44 MB catalogue and all 66,447 films read back.

  `vod show` now prints `not available (asked <when>)`, which is what makes the rule observable at all: the
  timestamp must not move on a second viewing.

- **Rank 1 · a refused guide download names its address.** Rather than a second exception type with the same
  `SanitizedUrl` property, `ProviderRequestException` states it once in LTR.Providers.Abstractions and
  `XtreamApiException` derives from it. That is also what let the CLI's error handling stop naming a protocol:
  it caught `XtreamApiException` specifically, which is *why* a playlist's failures arrived with no address.
  LTR.Providers.M3u.Tests gained a Kestrel host of its own — the M3U package had no test that went over HTTP
  at all, which is how the guide path came to be the one place the sanitiser was not used.
- **Rank 2 · the four loose ends.** `StreamHoldTest` holds the play-test sequence once and takes the film's
  seek, position and progress recording as a callback; both commands now print the state transitions, the
  tracks *and* the provider's reason, where each had about half. `CatalogueDetailLine` holds the row summary
  and finally has tests — it is bound in three views and nothing covered it. `ToUrlExtension` states
  `ProgressiveFile` as its own case instead of calling it unknown.
- **Rank 2 also settled what "forget" means.** Both front ends expressed taking an entry off the
  continue-watching list as `WatchOutcome.Discard`, which fits mechanically and also writes
  `LastWatchedUtc` — so a removed entry came back as the most recently watched thing in the catalogue, which
  `vod continue` prints. It is now its own store operation that records no moment, **because a watch outcome
  cannot express "nothing was watched": every one of them states a when.** That distinction is the reusable
  part.
- **Rank 3 · `NotSupportedProviderRegistry`** in `src/TestSupport`, linked as `TestClock` is. The trade is
  that per-member explanations went; three of the four on the window's stub were one fact in three phrasings
  and now sit on the class.

**One test got sharper rather than looser**, which is the sign the forget split was real: the WPF test
guarding against a stopping stream writing the position back used to assert "one progress write, and it is
Discard" — conflating the forget with the write-back it was watching for. The two are now separate
collections, so a write-back cannot hide inside the count.

### After M6 — protocol-neutral URL sanitisation

**Post-M6 rank 1.** `ISensitiveUrlSanitizer` in LTR.Providers.Abstractions, one implementation per protocol,
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
