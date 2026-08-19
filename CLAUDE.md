# LTR-Player

A Windows IPTV player for Xtream Codes panels and M3U playlists. WPF shell, LibVLC engine, SQLite
catalogue. The user supplies their own subscription; no playlists, credentials or provider discovery
ship with it.

The global conventions in `~/.claude/CLAUDE.md` apply. What follows is only what this project knows
that its code does not state — most of it learned by getting it wrong once.

## Where the project stands

Milestones from the plan, which lives outside the repository. Recorded here because the code shows what
exists and not what was considered finished.

| | | |
|---|---|---|
| **M1** Vertical slice — login, channel list, picture | done | |
| **M2** Sources complete — M3U, several sources, favourites, search, categories | done | |
| **M3** Guide — XMLTV import, now/next, timeline, detail | done | see the caveat below |
| **M4** VOD + series — catalogue, seasons, resume | done | `Docs/vod.md` |
| **M5** Player polish — OSD, fullscreen, keyboard, tracks | done | `Docs/player.md`; the live-caching value still wants measuring |
| **M6** Hardening — error handling, settings, packaging | done | `Docs/packaging.md`; the quarantine had landed early, with M3 |

**All six are merged into `main`.** Version 0.8.0 — bumped from 0.7.0 for pinned categories, on the same
rule 0.7.0 was set by: a schema migration and a visible change in the window. `pwsh build/publish.ps1`
produces a self-contained folder and a zip that runs on a machine with no .NET.

Since 0.7.0, and not from the plan: the on-screen controls can be woken by the pointer again — which they
could not, in fullscreen not at all — and a category can be pinned to the top of its picker. `Docs/player.md`
and `Docs/categories.md` carry both.

Since then, from a bug report: **previous and next follow what is playing.** They were wired straight to channel
zapping, so ⏭ during an episode tuned a live channel. They now move through the series' episodes, across the
season boundary, and are unavailable for a film. `Docs/player.md` has the design and `Docs/vod.md` the ordering.

Both of M3's stated limits are gone: the timeline's channel names used to scroll away with the programme
blocks, and it drew only the first 200 channels. It now pins the names and **pages** along the channel axis,
200 at a time, by command — the same way it has always moved along the time axis, and for the same reason.
See ranks 7 and 12 under Done in `Docs/refactoring-backlog.md`.

### Where to pick up

**`Docs/refactoring-backlog.md` holds one open item** from the review of 19 August 2026, made after previous and
next were fixed. Four were carried from the review of 18 August; six were new, and **nine of the ten were done in
the same sitting** — the tenth is recorded there as deliberately deferred, with its reason. The fourteen ranks
before those are done and kept there as the record of how — **one was dropped rather than built:** rank 11's
store-side paging of the channel list, on a measurement that is written down there so it is not re-derived.
**Ranks quoted in commit messages belong to whichever review was current when they were written**; that file
carries every mapping, and there have been three renumberings, so say which review you mean.

**The ranked work is done.** What is left is one Minor item that was deferred on purpose — splitting
`IVodCatalogue` into a film face and a series face, worth doing only if a third consumer appears. The next thing
here is whatever the plan says, not this file; a fresh review is the other option, and the last two were made
after a change shipped rather than on a schedule.

Read it before proposing a refactor here. Several entries record a *considered and rejected* design, four record
a rank whose own premise turned out to be half wrong once the code was read, and one records two untested
behaviours that a mutation check found while the change was being made.

Two things are outstanding that are *not* refactors, because only the person with the subscription can do
them:

- **`LiveNetworkCachingMilliseconds` (600 ms) is a guess, not a measurement.** The settings pane exposes it
  and `PlaybackSession` logs how long each open took, which is what makes tuning possible — see
  `Docs/verification.md` §4.
- **The checks that need a real panel or a window** are `Docs/verification.md` §§7–10: the player
  controls, a failing stream's reported reason, the packaged build, and the pinned categories.

### What M5 settled, and the one thing it did not

`Docs/player.md` has the design. The parts worth knowing before touching them again:

- **The transport lives on `IPlaybackTransport`, not on the engine and not on the coordinator.** Pause, seek,
  volume, tracks and aspect ratio are all there; `IPlaybackSession` keeps only what opens and releases a
  stream, and deliberately does not inherit the transport. One class implements both. The division that
  matters: `PlaybackCoordinator` decides *what* plays and owns the only call to `SwitchToAsync`, while
  `PlayerOverlayViewModel` takes the transport alone and therefore *cannot* open a connection. An overlay
  holding the engine is how the one-connection guarantee gets bypassed by the next thing that needs "just
  one" call.
- **Nothing in the overlay subscribes to the session's events.** An engine raises them on its own threads;
  WPF marshals a property change for a plain binding but *not* for a collection, so a track list rebuilt from
  an engine callback takes the window down. Everything is read in `Sample()`, from the window's timer.
- **The same timer runs at two rates** — five seconds for the resume recorder, half a second while the
  controls are visible. Not two timers: both jobs read the same figures from the same place.
- **A film reaching its own end is flagged, not acted on.** `PlaybackCoordinator` sets a flag from the engine
  thread and `SampleAsync` closes the stream off on the next tick, because what follows is a database write
  and three lists rereading themselves.
- **Previous and next mean "the next thing of the kind that is playing".** `PlaybackCoordinator.NowPlayingItem`
  records what the last request was for, assigned where the stream is opened and never read back from the engine
  — an engine asked what is playing answers with the *previous* item until the next stream arrives, so three
  quick presses would all land on the same episode. It lives beside `WatchProgressRecorder.Track`, which records
  the same event in the shape the catalogue layer needs; the two types stay separate because merging them would
  put a WPF type in `LTR.Catalogue`, but they are assigned in one place so a new play path cannot update one and
  forget the other. It is **not** cleared when an open fails: nothing is playing, but the viewer is still in the
  middle of that episode. The shell's two commands guard on it across an object boundary, so the forward is in
  `RegisterNotificationForwards` — and it has to notify *both*, since a film closes the buttons and a stop
  reopens them. The neighbour is looked up in the store
  (`GetSeriesForEpisodeAsync` → `EpisodeSequence.Neighbour`) and not in the episode rows on screen, because
  those hold one season of one series and only while it is open. That is not a nicety: resuming from Continue
  has an episode identifier and nothing else, and is where the bug was reported from. The lookup is scoped to
  the selected source, because switching subscription does *not* stop what is playing — unscoped, the next
  episode's identifier would be built into an address against the other account's credentials.
- **Keys are resolved by `PlayerKeyMap` and carried out by `PlayerActions`.** Not `KeyBinding`s in markup:
  an input binding is offered the key before the focused element sees it, so declaring one for `A` would mean
  the search box could never contain that letter. `MainWindow` checks what has focus, which is the whole
  reason it cannot be declarative. Arrow keys are deliberately *not* mapped to zapping — they belong to the
  channel list. `PlayerActions` splits where the design already does: four actions come back as delegates
  because they decide *what* plays or what the window shows — two of them from `PlaybackCommands` and two from
  the shell — and the rest go to the overlay because they act on an open stream.
- **`MainViewModel` regrows, every milestone, by the same mechanism** — it is the only place that can reach
  everything, so anything needing two of them lands there. 395 lines at the M4 merge, 483 after M5, 439 after
  extracting the key dispatch, 466 by the end of M6, 438 after the notification forwarding moved into a table,
  476 after previous and next were made to follow what plays, 469 once what they act on moved to the
  coordinator, **280 once `PlaybackCommands` took the ten play commands.** (Code lines: comments and blanks
  excluded, which is why `wc -l` reads nearly twice that.) Expect a fourth regrowth. Two lessons from the
  ones so far: a declarative registration costs nearly what the handler it replaces did, so **size is the
  wrong reason** to reach for one — and what worked was asking of each method whether it needs the *window*
  (a section, the panes and the lifetime token at once) or only a section and playback.
- **`LiveNetworkCachingMilliseconds` defaults to 600 ms and is a guess, not a measurement.** It is the only
  part of a zap that can be shortened; the stop that precedes it is required by the connection limit. Raise
  it if channels stutter in their first seconds — that symptom is this value being too low. `PlaybackSession`
  now logs how long each open took, which is what makes tuning it possible at all. M6 put it in the settings
  pane, so it no longer needs a rebuild to try a figure — but it does need a restart.

### What M6 settled

`Docs/packaging.md` has the shipping story. The rest:

- **A failed stream asks the provider why.** `StreamFailureReason` classifies in Core,
  `IStreamFailureExplainer` does the asking in LTR.Catalogue, and each front end words it — the split
  `SourceImportStage` established, with a test **in each front end** that every reason has wording of its own.
  That was true of the window from M6 and of the CLI only from 19 August 2026; add a reason and both fail,
  which is the point of the guard and is worth re-checking by mutation rather than trusting. A playlist source is
  never asked (no account, and asking re-downloads the document), and a superseded open is not a failure, so
  zapping does not interrogate the panel once per key press.
- **Settings are `settings.json` beside the database, not a table in it.** The catalogue is a cache that gets
  quarantined; settings inside it would go with it. The file is also editable by hand, which is the point when
  a bad value is what stops the window opening. Reading and writing both refuse to throw, and a bad file is
  left where it is rather than replaced.
- **Playback tuning applies on restart, and the pane says so.** Both figures reach LibVLC when the engine is
  constructed. Volume, mute and aspect ratio apply immediately and are written on the way out of the window.
- **EF Core is capped at Warning in the log.** A startup used to write ~450 lines, nearly all SQL; it now
  writes three. Warning rather than Error deliberately — the split-query complaint that found a real
  cartesian product arrives there. Lower the override, or use the CLI's `--verbose`, to get statements back.

## Layout

```
LTR.Core[.Abstractions]        Domain. Platform-neutral, no dependencies. Keep it that way —
                              a web frontend is planned and would reuse it.
LTR.Providers[.Abstractions]   IContentProvider, probes, resolvers, URL sanitisers + the registry that
                              selects them. One implementation of each per protocol; ask the registry,
                              never inject one singly
LTR.Providers.Xtream           player_api.php client
LTR.Providers.M3u              M3U-Plus parser and provider
LTR.Catalogue[.Abstractions]   Application layer: import orchestration and catalogue access. The store
                              is five interfaces over one class — ISourceStore, ILiveCatalogue,
                              IGuideCatalogue, IVodCatalogue, IWatchProgressStore — so a consumer
                              declares the face it uses. Take the narrowest that fits
LTR.Epg.Xmltv                  XMLTV reader. No dependencies at all — not even on Core
LTR.Persistence                LtrDbContext, one partial per subject: Live, Guide, Vod, WatchProgress. All
                               database logic lives here (§3.3.2). The WatchProgress partial exists because
                               a position is the one thing in the catalogue a refresh must never touch
LTR.Playback[.Abstractions]    Engine-neutral playback policy
LTR.Playback.LibVlc            LibVLC engine
LTR.Security.Dpapi             Windows credential protection, kept out of Core on purpose
LTR.Cli                        Headless verification of everything below the UI (§2.12). One class per
                               command under Commands/ states its options and action; Program is
                               composition only. A command touching the database goes through
                               CatalogueCommandRunner, so probing a panel creates no database file. The two
                               collaborators whose *output is the result* — ResolvedAddressReport, which
                               decides whether credentials are printed, and ConnectionReleaseCheck, which
                               decides whether teardown was clean — take an injected TextWriter and are
                               tested; the listing handlers still write to Console directly
LTR.Player.Wpf                 The only project that references WPF. Three classes hold what one used to:
                               PlaybackCoordinator opens a stream and remembers where it got to,
                               PlaybackCommands turns a selection into a playback request (the markup binds
                               its commands through MainViewModel.PlaybackCommands), and MainViewModel
                               composes the four catalogue sections, the guide, the panes and the on-screen
                               controls, and is the sections' ISourceCoordinator; the sections never
                               reference each other. Views/ holds one UserControl per section and per
                               overlay, so MainWindow.xaml is composition only. CategoryPickerView is the
                               one view used by three sections at once — it names none of them and binds to
                               the CategoryPickerViewModel each of them holds
```

Dependency direction: apps → Catalogue/Providers/Playback → *.Abstractions → Core. Core knows nobody.
`LTR.Player.Wpf` does **not** reference `LTR.Persistence`, and should not start.

## Constraints that shaped the design

**A subscription permits very few concurrent connections — one, for the provider this was built
against.** A stream left open locks the account out for minutes. Therefore: all playback goes through
`IPlaybackSession`, which stops fully before starting and never abandons the stop, not on caller
cancellation and not on supersession. Rapid channel changes resolve by generation so intermediate
requests are dropped rather than each opened in turn. `PlaybackSessionTests` is the most important test
file in the repository; its fake engine throws if two streams are ever open at once.

**Xtream panels are divergent forks with no specification.** Probe capabilities per source, never
assume an endpoint exists. The same scalar arrives as `5`, `"5"`, `""` or `null` depending on the
panel, which is why `LTR.Providers.Xtream/Json` exists. Panels also serve HTML error pages at HTTP 200,
reject unfamiliar user agents, redirect to streaming nodes, and — being PHP files — sometimes emit a
byte-order mark ahead of their JSON. A response body is **streamed**, not read into a string: the first 512
bytes are peeked through a `PipeReader` without being consumed, which is what lets the empty, HTML and mark
checks run before `JsonDocument` reads the document whole. Streaming puts the body outside the resilience
pipeline, so `XtreamTimeouts` holds the deadline that replaces it along with the pipeline's own.

**Credentials travel inside the address, on both protocols, so nothing prints one unsanitised.** Anything
about to log, print or store an address asks `IProviderRegistry.GetUrlSanitizer` — there were three
hand-rolled copies of the masking before this existed, one of them typed to `XtreamSource` in the CLI. The
rules differ by kind and not just by input: Xtream knows its secrets, so it removes them **where the protocol
puts them** — a query value or a path segment that *is* the credential — and leaves the host and `action=…`
readable, because those are why the address is being logged. It replaces a secret wherever it occurs only as a
fallback, when the structural pass found it nowhere, so an unfamiliar shape from a panel still cannot leak one;
replacing by value unconditionally was the original rule and a two-character username redacted the host out of
every logged address. A playlist source, by contrast, holds no credentials *of its own* — whatever the provider
issued is already in the address under a parameter name nothing here knows — so in the address being
sanitised *every* query value goes and only the names stay. Its **path** is redacted by value instead, from
the one place the credentials are on record: the query of the source's own playlist and guide addresses. Only
where such a value is a **whole path segment**, because `output=ts` would otherwise take the extension off
every channel. What is left uncovered is a playlist held as a file that declares no `x-tvg-url` either — then
nothing reveals them, and the CLI says so rather than claiming a masking it did not perform; that self-report,
and the `--reveal` gate it sits under, are held by `ResolvedAddressReportTests` since 19 August 2026 and were
mutation-checked. `user:password@host` is removed for every protocol by `SensitiveUrlSanitizer<TSource>`,
which is the only form that needs no protocol knowledge. A failure that wants to carry an address throws
`ProviderRequestException` (`XtreamApiException` derives from it) and puts the *sanitised* address on
`SanitizedUrl` — the CLI prints it, and catching a protocol's own type there is what left playlist failures
with no address for as long as it did.

**Stream URLs are never probed.** Opening one occupies a connection slot. A probe that locks the user
out of their own subscription is worse than defaulting to the prefixed `/live/` form and correcting on
a 404.

**A film's container extension is part of its address, and its seasons are a second call.** `get_vod_streams`
and `get_series` are cheap; `get_series_info` is one call per series against eleven thousand of them, so
seasons are fetched when a series is opened and cached against the panel's own `last_modified`. An episode
listing arrives in three different shapes, which is why `XtreamSeriesInfoResponseDto.Episodes` stays raw
JSON. `Docs/vod.md` has the rest.

**Real data is messier than fixtures.** A 17,000-channel subscription contains decorative separator
rows carrying valid stream ids (`ChannelNaming.IsSeparatorLabel`), and 72% of its channels have no
`tvg-id` — so guide matching by name is the primary path, not a fallback. Normalisation must
strip `FR: ` and `HD`/`FHD`/`4K` but keep `+1`, which is a different channel. `ChannelNaming` therefore
has two normalisers that must not be confused: `ToIdentityKey` keeps every distinction the provider makes,
`ToGuideMatchKey` discards the cosmetic ones. `Docs/epg.md` has the rest of the guide's design.

## Persistence traps

- **SQLite cannot compare a `DateTimeOffset`.** EF writes it as text with the offset appended, which sorts
  wrongly across offsets, so the provider refuses to translate `<`, `>` or `Max` over such a column. The
  guide's instants therefore go through a converter to UTC `DateTime`. Any new column that a query filters
  or orders by needs the same treatment.
- **A database that cannot be read is set aside, not repaired.** Migration is the first thing either
  application does, so corruption used to be fatal before any window opened. `PrepareCatalogueAsync`
  renames the file and its `-wal`/`-shm` to `catalogue.db.corrupt-<stamp>` and starts over — the catalogue
  is a cache, and starting over beats an application that will not open. The files are kept: what
  corrupted one is worth knowing, and deleting the evidence is how the cause stays unknown. Quarantining
  needs `SqliteConnection.ClearAllPools()` first, or Windows refuses to rename the still-open file.
- **`MigrationTests` migrates an empty database and proves only that the schema builds.**
  `MigrationUpgradeTests` is the one that matters for shipped installations: SQLite cannot alter a
  constraint in place, so EF implements one by rebuilding the table, and a rebuild is what silently empties
  it. Add a case there for every migration that alters an existing table.
- **A category carries the viewer's pin, and the store is what sorts by it.** `Category.IsFavorite` is user
  data exactly as a favourite channel is, so `AdoptProviderFields` leaves it alone. `GetCategoriesAsync`
  orders pinned first — stated there because the pickers and the CLI read it, and restated once in
  `CategoryPickerViewModel` because a pin has to move an entry without refilling the bound collection.
  `Docs/categories.md` has the rest.
- **A panel numbers its category identifiers per section,** so `58` is a live category and a film category
  at once. Category reconciliation is therefore scoped to the *kinds* an import covers, not to the source —
  scoped to the source, a live refresh deletes every film category — and its lookup is keyed by
  `(ExternalId, Kind)`, because a dictionary keyed by the identifier alone throws on the duplicate.
- **A reconciliation is three things, and they live in three places.** `CatalogueReconciler.Match` does the
  matching for all four entity types and decides nothing about fields; **what a provider owns is stated on the
  entity** (`Channel.AdoptProviderFields`, `VodItem.AdoptListingFields` and the two others) because that is a
  fact about a channel and not about a table; and `SeriesReconciliation` in Core holds the season algorithm,
  which performs no I/O. Add a field to an entity and the adopt method is where you decide whether a refresh
  may overwrite it — the context will not tell you.
- **An empty answer from a provider is an answer; an unreachable provider is not.** `VodItem.HasDetail`
  records that a detail arrived, `DetailAttemptedUtc` records that the panel was asked, and only the second
  is written when the answer was nothing — for a day, after which it is asked again, because panels do fill
  their detail in. `VodDetailService.TryFetchAsync` reports whether the provider answered at all for exactly
  this reason; treating a failed request as an empty answer would suppress the retry over a momentary outage.
- **Taking something off the continue-watching list is not a `WatchOutcome`.** Every outcome states a moment,
  so expressing "the viewer removed this" as `Discard` also wrote `LastWatchedUtc` and the entry came back as
  the most recently watched thing in the catalogue. `ForgetMovieProgressAsync` clears the position, leaves
  `IsWatched` alone and records no instant at all. Anything new that means "undo" rather than "played" needs
  the same treatment.
- **A listing may overwrite what a listing owns, and must never blank out what a detail call supplied.**
  Panels state a synopsis in `get_vod_info` and not in `get_vod_streams`, so a refresh that assigned the
  listing's fields unconditionally would erase every synopsis the player had fetched.

## WPF traps, each of which shipped a bug once

- **Overlays belong inside `VideoView.Content`**, not beside it. `VideoView` hosts a separate native
  window over the WPF tree; a sibling element is invisible behind the video. That hosting has a second
  consequence found in M5: input over the overlay does not reach the shell window's handlers either, which is
  why the overlay handles its own pointer events while the keyboard stays with the window. `PicturePointer` does
  it, **on the window the content is hosted in** — found on `Loaded`, since that window is `VideoView`'s to
  create — and not on the control. It also owns what a double-click was aimed at, because that fact comes from
  one of the same subscriptions. `PlayerOverlayViewTests` states both.
- **`MouseDoubleClick` reaches a handler a button has already dealt with, and does not say what was
  clicked.** WPF raises it from a class handler registered for handled events too, and it is a *direct*
  event, so its source is the element the handler sits on. Two quick clicks on the overlay's skip button
  went fullscreen. What was aimed at is recorded from the tunnelling `PreviewMouseLeftButtonDown` and
  checked there — `PicturePointer.OnDoubleClicked`.
- **`Background="Transparent"` over the video is not hit at all.** That window is *layered*, and Windows
  hit-tests a layered window by its alpha: `#00FFFFFF` passes the pointer through to the video underneath, so
  the controls saw clicks on their own opaque bar and nothing whatever over the picture — the bar could not be
  brought back by the pointer, and in fullscreen not at all. Any surface over the video that has to be touched
  takes `PointerCatchBrush` (`#01000000`), which is invisible and hit. Measured, not inferred: over a fully
  transparent layered window `WindowFromPoint` returns the window below it.
- **Every command guard needs `[NotifyCanExecuteChangedFor]` on every property it reads.** Three
  defects came from omitting it. Note that `CanExecute` invokes the guard directly and therefore passes
  even with the bug — tests must assert the *notification*. The attribute cannot cross an object
  boundary, and the nine forwards that therefore have to be made by hand are registered in one table,
  `MainViewModel.RegisterNotificationForwards`, over `CrossObjectNotifications`. Add to that table; do not
  add a handler. Two things about it are load-bearing: **an empty or null property name means every
  property** (the rule the table exists to state once), and the forwards must be registered *before* the
  shell's own reaction handlers subscribe, so a command is notified before work that may change its guard
  begins.
- **`InvariantGlobalization` must stay off.** WPF's binding engine throws from
  `XmlLanguage.GetSpecificCulture` without culture data, and every binding in the window fails while it
  still looks fine.
- **A retemplated `ComboBox` must read `ItemTemplate`,** not `SelectionBoxItemTemplate`, which does not
  resolve with `DisplayMemberPath` and leaves the control rendering `ToString()`.
- **`Progress<T>` and `ICollectionView.Refresh` both matter:** a refresh resets the collection and the
  list box drops its selection, so it has to be restored.
- **Fill a bound collection before selecting in it.** Emptying one makes a `ComboBox` write a null selection
  back through the binding, so a selection assigned first is discarded. Both pickers rendered blank while their
  lists looked perfectly correct, because the filter read the same null as "every category". The same null is
  why **`SelectedCategory` is nullable**: a reader declared against a non-null property compiles and then
  dereferences null during that instant — which is how a command guard added for pinned categories crashed the
  window on startup, long after the trap had been written down here. **For the category pickers this is now
  structural** — `CategoryPickerViewModel.ShowAsync` fills and selects as one operation, so no caller states the
  order. No test can see it either way: a test has no ComboBox to write the null. Anything *new* that binds a
  collection still owns the rule.
- **Dispose the DI container asynchronously.** It holds `IAsyncDisposable` singletons; the synchronous
  `Dispose` throws and `PlaybackSession` never releases its stream.
- **A view model that reads the clock must be given a `TimeProvider`.** `DateTimeOffset.UtcNow` in
  `ToggleGuideAsync` opened the timeline on a window that could not contain the guide's own "now" marker;
  the test caught it only because the test clock differs from the real one.
- **`ShowCatalogueAsync` swallows cancellation as well as failure.** Source management starts it without
  awaiting when the selection changes, so anything escaping becomes an unobserved task exception. That only
  became reachable once the shell gained a lifetime token to cancel.
- **A resume position has to be sampled while playback runs.** By the time a stream is closed the engine
  has no position to report, so `WatchProgressRecorder` keeps the last sample and a five-second timer feeds
  it. Anything that reads a position only at the moment of saving saves nothing, and the test that catches
  that is the one where nothing samples between playing and stopping.
- **`Progress<T>` delivers through a synchronisation context.** In a test with none, a stage message can land
  after the result message an assertion is reading. The guide-import fake reports progress only when asked.
- **The guide's markup is measured, so a layout property can be stated.** `GuideOverlayViewTests` builds the
  real view on an STA thread of its own, loads `Theme.xaml` and asserts that scrolling the timeline leaves the
  channel names where they are while the blocks and the now-marker move by exactly as much. It is the only test
  here that touches a visual tree, and it exists because every WPF defect this repository shipped was in the
  part nothing measured. It also proves an `ElementName` binding resolves out of a `DataTemplate` into the
  file's outer namescope, which three bindings in that file rely on.
- **A retemplated `Slider` needs its grid explicitly hit-testable.** Without a background on the template's
  root the bar can only be grabbed on the four pixels of the track itself. Both sliders are also
  `Focusable="False"`, because a focused one answers the arrow keys and those are the skip keys.
- **`FakePlaybackSession` releases before it opens, as the real session does.** It did not, and that gap let
  a mutation through: with no intermediate `Stopped`, no test could tell a channel change from the end of a
  film. A fake that skips an invariant hides exactly the bug the invariant exists for.

## Verifying

`Docs/verification.md` has the full sequence. In short:

```bash
dotnet run --project src/LTR.Cli -- probe    --url http://HOST:PORT --user U --pass P
dotnet run --project src/LTR.Cli -- channels --url http://HOST:PORT --user U --pass P
dotnet run --project src/LTR.Cli -- play-test --url http://HOST:PORT --user U --pass P --stream-id ID
dotnet run --project src/LTR.Cli -- guide import --source-id ID
dotnet run --project src/LTR.Cli -- sources refresh ID
dotnet run --project src/LTR.Cli -- vod episodes --source-id ID --series-id LOCAL_ID
dotnet run --project src/LTR.Cli -- vod play-test --source-id ID --movie-id LOCAL_ID --start-at 2400
dotnet run --project src/LTR.Cli -- vod play-test --source-id ID --movie-id LOCAL_ID --seek-to 1800 --seconds 20
```

`--seek-to` is the only headless check of the seek bar's own call, which is a different path from `--start-at`:
that one is honoured while the stream opens, this one against a stream already playing.

`play-test`'s last line is the real test: it polls the panel until it reports the connection released.
`guide import`'s `Matched N of M` is the equivalent for the guide — everything else about an import can
succeed while it achieves nothing. For films it is `vod play-test`'s `Position`: asked to start forty
minutes in, a film that reports `unknown` silently restarted from the beginning and looks healthy from
every other angle.

**Run the play-tests one at a time.** A one-connection subscription answers the next stream with HTTP 200
and an empty body while it still counts the previous one, which reads exactly like a broken film.

`sources refresh ID` is the only way to import an Xtream catalogue without the window, and therefore the
only way the film and series import is verifiable headlessly. `sources add-playlist <path-to.m3u>` seeds a
source with no credentials, which is how UI behaviour that needs a configured source gets verified without a
subscription.

## Working in this repository

- **The build fails while the app is running.** MSBuild cannot replace locked DLLs; the errors are
  `MSB3021`/`MSB3027` and they follow a successful compile. Ask for the app to be closed — `build/publish.ps1`
  refuses outright for the same reason.
- **`$(Platform)` must read `AnyCPU`, never `Any CPU`.** `VideoLAN.LibVLC.Windows` compares against the
  former, so the solution's own spelling silently selects no natives at all: the publish then starts, opens,
  loads the catalogue and plays nothing. `publish.ps1` checks for `libvlc\win-x64\libvlc.dll` by name because
  of it. The natives live under `libvlc\win-x64\`, not beside the executable.
- **`SelfContained` cannot go in the csproj.** A self-contained project cannot be referenced by one that is
  not, so it stops the test project compiling; it lives in the publish profile.
- **Do not delete `%LOCALAPPDATA%\LTR-Player\logs`.** It is the diagnostic trail, and deleting it to
  make error-checking easier destroyed the evidence for a real question once.
- **Do not infer database state from the file.** While a process holds it open, Windows reports stale
  size and `File.ReadAllBytes` reads only that much. Startup logs the database path and the source,
  channel, category and favourite counts — use those, or `sources list`.
- **Restoring a file from a backup can keep its old timestamp,** and MSBuild will then reuse the old
  binary. Touch the file if a test result looks impossible.
- **The executable is `LTR-Player.exe`, not `LTR.Player.Wpf.exe`** — M6 set `AssemblyName`. The project
  folder keeps its own name, so the two differ.
- **Start the app after touching dependency injection.** A missing registration is invisible to the compiler,
  and the container holds the session whose disposal releases the provider connection. Launch it and read the
  log; do not synthesise clicks, and close it with a window-close rather than by killing the image name.
- **Read today's log, not the newest-looking one.** Serilog rolls daily, so a session spanning midnight
  writes to a second file and the first one looks like the app stopped logging.
- Migrations need explicit approval before being created (§3.3.1). `MigrationTests` fails when the
  model drifts from them, which is how drift gets noticed.
- **781 tests pass on this branch; 739 on `main`, verified by checking main out and running it.** A refactor
  should not move that number. Counted by summing the per-project figures — the totals quoted in this
  branch's first three commit messages (757, 765, 774) are each four low, an arithmetic slip carried forward;
  the figures here are the measured ones.
- **`LTR.Providers.Tests` composes the real container** — `AddProviderRegistry` plus both protocol packages —
  and is the only test that would catch a component registered for one protocol and forgotten for the other.
  Add a case there when a new per-protocol component appears.
- **The WPF tests reach a shell through `MainViewModelHarness` and `ShellUnderTest`.** The harness builds the
  composed view model over fakes; the two extension methods are `WaitForIdleAsync` (await before asserting on
  anything a selection triggers) and `VisibleChannels` (a snapshot of the filtered list). Do not re-copy either
  into a test class — three copies of the first is what earned them a home.
- **A blanket `sed` over `*.cs` includes the file you are writing.** Sweeping
  `ChannelView.Cast<ChannelItemViewModel>()` into `VisibleChannels()` rewrote that helper's own body into a
  self-call; the suite reported a stack overflow rather than a failure. Exclude the new file, or read the diff.

`Docs/refactoring-backlog.md` holds the reviewed, ranked work that remains.
