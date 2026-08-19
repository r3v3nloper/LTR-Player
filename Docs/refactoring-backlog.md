# Refactoring backlog

**Five items open, from the review of 19 August 2026**, which carries four of the previous review's five
forward and adds six. Five of its ten were done in the same sitting. The fourteen ranks before those are done
and are kept below as the record of how — including one that was dropped rather than built, with the
measurement that decided it.

Numbers quoted in commit messages belong to whichever scheme was current when they were written; the older
mappings are under [Done](#done), and each review numbers from 1 again. **This is the third renumbering** —
say which review a rank belongs to when quoting one.

## Review of 19 August 2026

Made after previous-and-next were fixed to follow what is playing. Over the whole tree, not only over that
change. Criticality first, effort second, so rank 1 is the one worth doing on any afternoon.

| Rank | Project | Area | Issue | Proposed refactor | Criticality | Effort | Status |
|---|---|---|---|---|---|---|---|
| 1 | LTR.Cli | Maintainability | `StreamFailureNotes.Describe` has no guard that every reason worth wording has wording of its own. Its window counterpart does, and `CLAUDE.md` records that test as part of what established the split — but the CLI has no test project, so the front end that exists *for diagnosing* is the one where a reason added later silently reads as the fallback. That is the defect the import stages already shipped once ("Working..."). | An `LTR.Cli.Tests` project, and the `EveryReason_HasWordingOfItsOwn` theory from `StreamFailureTextTests` pointed at `StreamFailureNotes`. Needs `InternalsVisibleTo`, or the class made public. | Moderate | Low | **Done** |
| 2 | LTR.Cli | Security | `ResolvedAddressReport` decides whether a paid subscription's credentials reach the console, and `ConnectionReleaseCheck` decides whether teardown is reported clean — the one thing `CLAUDE.md` calls the actual proof. Both write straight to `Console`, so neither decision can be tested. The masking is currently right (read and confirmed: `--reveal` is the only path to a verbatim address anywhere in the tree), and nothing holds it there. | Have both return their lines, or take a writer; the command prints. Then test the gate and the three release verdicts. | Moderate | Low | **Done** |
| 3 | LTR.Player.Wpf | Maintainability | Carried from rank 3 of 18 August, re-verified. Both view models hold the picker's collection, selection, guard and command in identical ~25 lines. `CategoryPicker.Fill` still requires the caller to set the selection *afterwards* — the unwritten protocol whose violation crashed the window on startup. | A `CategoryPickerViewModel` both sections hold, owning state and command together; the markup binds it directly. Supersedes `ICategoryPickerSection`. | Moderate | Medium | Open |
| 4 | LTR.Player.Wpf | Maintainability | Two records of what is playing. `MainViewModel.NowPlayingItem` carries the kind and the episode for the transport; `WatchProgressRecorder` privately carries the kind and the item id for progress. They are *not* the same fact — the recorder deliberately ignores a live channel — but both are maintained by the same four play methods, and a fifth play path that updates only one would leave previous and next acting on the wrong thing, silently. | Move `NowPlayingItem` onto `PlaybackCoordinator`, which already owns the recorder and is the only thing that opens a stream. The guard notification then crosses an object boundary, so it goes in `CrossObjectNotifications` — the mechanism that exists for it. | Moderate | Medium | **Done** |
| 5 | LTR.Player.Wpf | Maintainability | Carried from rank 4 of 18 August, and **worse**: `MainViewModel` is 476 code lines (was 438), with ten commands and eight helpers in the "what plays" group alone. The recurring growth `CLAUDE.md` warns about. | Extract that group beside `PlaybackCoordinator`. Note what the previous estimate did not: the markup binds `DataContext.PlayNextCommand` on the window, and `[NotifyCanExecuteChangedFor]` cannot cross an object, so the notification table moves with it. That is the High. | Moderate | High | Open |
| 6 | LTR.Player.Wpf.Tests | Maintainability | `WaitForIdleAsync` is copied verbatim into three test classes and `Row` into two, all over the same `MainViewModelHarness` every one of them already holds. Rank 2 of 18 August moved the *source object* to `TestSupport` and left these behind. | Put `WaitForIdleAsync` and `Row` on the harness. Leave the per-class `Channel`/`Movie`/`SeriesEntry` factories where they are — they carry a value, which is the distinction that review already drew. | Minor | Low | **Done** |
| 7 | LTR.Persistence | Maintainability | `LtrDbContext.Vod.cs` is 464 code lines holding three subjects: films, series, and watch progress. `IWatchProgressStore` is already its own interface, so the partial does not mirror the seam the abstractions draw — and the progress rules are the ones with the most reasoning per line. | A `LtrDbContext.WatchProgress.cs` partial. Pure move; no behaviour. | Minor | Low | **Done** |
| 8 | LTR.Player.Wpf | Maintainability | Carried from rank 7 of 18 August. `PlayerOverlayView.xaml.cs` is 242 lines doing four things: attaching to the picture's window, recording what was pressed, timing the cursor, reporting a scrub. The first two are one subject. | A `PicturePointer` holding attachment and press bookkeeping; the code-behind forwards. | Minor | Medium | Open |
| 9 | LTR.Player.Wpf.Tests, LTR.Persistence.Tests | Maintainability | Carried from rank 8 of 18 August. `VodSectionTests` (536) and `LtrDbContextVodTests` (819) each mix film and series cases, so the mirroring of SUT files (§3.5.2) does not hold there. The second is now the largest file in the repository. | Split each into a film file and a series file. Rank 7 gives the persistence one its third seam. | Minor | Medium | Open |
| 10 | LTR.Catalogue.Abstractions | Maintainability | `IVodCatalogue` is seven members over two unrelated entities, and both sections take the whole face where `CLAUDE.md`'s rule is to take the narrowest that fits. | Split into a film face and a series face. **Deferred deliberately** — this is the weakest item here, and §2.16 is the reason: the split costs an edit in the store, the container, two fakes and the CLI, and buys a narrower declaration nobody has been misled by. Do it if a third consumer appears. | Minor | Medium | Open |

**No security finding, for the second review running.** The URL sanitisers, `PlaybackSession` and the
reconciliation rules were read and left alone; each carries its reasoning and its tests. Rank 2 above was not a
defect — it was that the one security-adjacent rule in the CLI was correct without being held. It is held now.

Two things this review changed on the spot rather than ranking: rank 6 of 18 August (`CLAUDE.md` not saying how
`MainViewModel`'s size is counted) is **done** — the note now states that comments and blanks are excluded, and
carries the new figure.

### What the five finished ones changed

- **Rank 1 — the CLI has a test project, and both wordings were mutation-checked together.**
  `LTR.Cli.Tests`, over `InternalsVisibleTo` as the other seven test projects do. Eight tests: the
  every-reason theory, and two that assert the sentences the CLI cannot afford to lose — the connection limit
  must name the *previous play-test*, because two play-tests in succession against a one-connection
  subscription is the case `CLAUDE.md` warns reads as a broken film, and the fallback must name `probe`,
  because the whole job of this wording is a next step.

  **The check that mattered was the mutation, not the eight passes.** Adding a `GeographicBlock` reason to
  `StreamFailureReason` and wording it nowhere fails exactly one test in the CLI *and* one in the window —
  which is the property being bought, and it did not exist on one side an hour ago. Reverted afterwards.

  Two things not done, deliberately. `StreamFailureNotes` stayed `internal`; making it public to test it would
  widen the CLI's surface for a test's convenience. And nothing else in the CLI gained a test — the rest is
  `Console.WriteLine`, which is rank 2 above and needs a seam first.

- **Rank 2 — the console is a dependency for the two whose output *is* the result, and both decisions were
  mutation-checked.** `ResolvedAddressReport` and `ConnectionReleaseCheck` take a `TextWriter`: `Console.Out`
  from the container, a `StringWriter` in the tests. Nine tests. Neither class changed what it decides.

  **A writer, not returned lines** — which the rank offered as the alternative and would have been wrong for
  the release check. Its progress lines are what a person watching a twenty-second wait reads; collecting them
  to hand back at the end would have turned a live count into a silence. The cadence is a
  `PollDelay { get; init; }` the tests zero, and deliberately **not** a `TimeProvider`: `TestClock` leaves
  timers to the base class on purpose, so it would have waited for real.

  **The gate is asserted against the real sanitiser**, through a container with both protocol packages, not
  against a fake that returns what it was told to. Three mutations, each failing what it should: printing the
  address verbatim regardless of `--reveal` fails one test; declaring teardown clean on any answer fails three;
  spending only one attempt instead of five — so that a panel's ordinary lag reads as a leak — fails two.

  Also covered: that the documented playlist gap still reports itself. A playlist with no query has nothing on
  record to tell a secret path segment from a route, so sanitising changes nothing, and the note has to say so
  rather than claim a masking. That once printed credentials in clear under a note saying they were masked.

  **Verified against the real subscription too**, because a DI change is invisible to the compiler:
  `live resolve` prints `http://hd-max.org:8080/live/***/***/744257.ts` and the masked-credentials note.

  What is **not** done: the other eleven handlers still call `Console.WriteLine` for their listings. That was
  never this rank — the two here are the ones whose output is a decision rather than a report — but the seam is
  now registered, so a handler that wants it can take a `TextWriter`.

- **Rank 4 — what previous and next act on belongs to the thing that opens streams.** `NowPlayingItem` moved
  from `MainViewModel` to `PlaybackCoordinator`, which was already calling `WatchProgressRecorder.Track` for
  the same event. Five assignments in the shell became three in the coordinator, at the three places a stream
  is actually opened, and the clear happens in its `StopAsync` — through which every full stop already passes:
  the stop button, a source being deleted, a film reaching its own end, and the window closing.

  **The two records still differ in shape, and that is the honest limit of this.** The recorder needs an
  identifier to write a row and lives in the catalogue layer; the transport needs the `Episode` entity and is a
  fact about this window, so merging the types would push a WPF type into `LTR.Catalogue`. What has gone is
  their being *maintained apart* — a sixth play path now cannot update one and forget the other.

  The guard's notification became a cross-object forward, registered in the table `CrossObjectNotifications`
  exists for. **Three mutations, each caught:** dropping the forward fails one test, registering it for next
  but not previous fails one, and omitting the clear on stop fails two. A new case in
  `CrossObjectNotificationTests` asserts *both directions* — starting a film must close the buttons and
  stopping it must reopen them — because a forward wired one way passes every other assertion here.

  `MainViewModel` is 469 code lines, from 476. Barely a return, and worth stating plainly: this was cohesion,
  not size. Rank 5 is still the size one.

- **Rank 6 — `ShellUnderTest`, and the rank's own premise was half wrong.** `WaitForIdleAsync` was indeed
  copied verbatim into three classes and is now one extension method. But **`Row` was defined once, not twice**
  — that count came from a grep matching four patterns per file. The real duplication was the expression those
  helpers wrapped: `viewModel.Channels.ChannelView.Cast<ChannelItemViewModel>()`, **22 times across 7 files**,
  which is `VisibleChannels()` now.

  Extension methods rather than members of `MainViewModelHarness`, which is what the rank proposed. Both read
  the *view model*, not the fakes — and `CategoryPinTests` builds two shells from one harness, to prove a pin
  survives the window reopening, so a harness holding "the" view model would have to pick one of them.

  Two things worth carrying forward. The best version of the comment was on the copy in `VodSectionTests`, not
  on the two identical ones — it is the one that says why the loop terminates, and it is the one that was kept.
  And **the blanket `sed` rewrote the new helper's own body into a self-call**, which the suite caught as a
  stack overflow at 18,993 frames deep. A sweep over `*.cs` includes the file being written.

  `Row(viewModel, index)` stayed as a local one-liner over `VisibleChannels()[index]`: eight call sites read
  better for it, and it carries a value rather than being a pass-through — the distinction 18 August's rank 5
  drew.

- **Rank 7 — `LtrDbContext.WatchProgress.cs`, and the move is provably verbatim.** 464 code lines became 349
  plus 122. All 73 persistence tests pass with **no assertion touched**, which is the same proof rank 10 of
  the older scheme leaned on; beyond that, the moved block diffs byte-identical against what was removed, so
  the only edits are the two file headers. The Vod partial's summary had claimed "and where the viewer left
  off" and no longer does.

  One thing found on the way, and it is the kind that would have shipped: the new header first cited
  `<see cref="IWatchProgressStore"/>`, and `LTR.Persistence` references only `LTR.Core` — the interface lives
  in the application layer above it. A dangling cref, silent because `GenerateDocumentationFile` is false.
  Both mentions now name it in `<c>` and say why they cannot reference it.

## Review of 18 August 2026

Made after pinned categories shipped, over the whole tree rather than over that change. Ranked by
criticality first and effort second, so rank 1 is the one worth doing on any afternoon.

| Rank | Project | Area | Issue | Proposed refactor | Criticality | Effort | Status |
|---|---|---|---|---|---|---|---|
| 1 | LTR.Player.Wpf | Maintainability | `CategoryPickerView` binds `Categories`, `SelectedCategory` and `ToggleCategoryFavoriteCommand` against whatever data context it is given. That the two view models have that shape was known only to the markup, and a failed binding is reported to a trace listener rather than to the log or the compiler — so a rename would break one section silently. | State the shape as an interface both implement. | Moderate | Low | **Done** |
| 2 | Test projects / TestSupport | Maintainability | Eleven test classes wrote an `XtreamSource` out by hand. A field added to a source meant eleven edits, and the forgotten one fails as a test that was never asking about the field. A builder existed, but only inside the Xtream test project. | Move the builder to `TestSupport` and use it everywhere. | Moderate | Low | **Done** |
| 3 | LTR.Player.Wpf | Maintainability | Both view models hold the picker's collection, selection, guard and command in identical ~25 lines. `CategoryPicker.Fill` also requires the caller to set the selection *afterwards* — the unwritten protocol whose violation crashed the window on startup. | A `CategoryPickerViewModel` both sections hold, owning state and command together; the markup binds it directly. Supersedes rank 1. | Moderate | Medium | Carried to 19 Aug rank 3 |
| 4 | LTR.Player.Wpf | Maintainability | `MainViewModel` is 789 lines and 30 private methods — the recurring growth `CLAUDE.md` warns about. It holds four things: what plays, the guide import, the section switch, and the notification table. | Extract the "what plays" group (~150 lines, six commands) beside `PlaybackCoordinator`. | Moderate | High | Carried to 19 Aug rank 5 |
| 5 | LTR.Player.Wpf.Tests | Maintainability | `GuideOverlayViewTests` kept two methods that only forwarded to `VisualTreeHarness`, left behind when the harness was extracted. | Point the call sites at the harness; delete the wrappers. | Minor | Low | **Done** |
| 6 | Docs | Maintainability | `CLAUDE.md` tracks `MainViewModel`'s size across milestones but does not say how the figure is counted. `wc -l` gives 789 where the note says 438, and both are defensible — which makes the rule "check it at the start of a milestone" unusable. | State the measure, or restate the series in `wc -l`. | Minor | Low | **Done** |
| 7 | LTR.Player.Wpf | Maintainability | `PlayerOverlayView.xaml.cs` is 242 lines doing four things: attaching to the picture's window, recording what was pressed, timing the cursor, reporting a scrub. The first two are one subject. | A `PicturePointer` holding attachment and press bookkeeping; the code-behind forwards. | Minor | Medium | Carried to 19 Aug rank 8 |
| 8 | LTR.Player.Wpf.Tests, LTR.Persistence.Tests | Maintainability | `VodSectionTests` (825 lines) and `LtrDbContextVodTests` (987) each mix film and series cases, so the mirroring of SUT files (§3.5.2) no longer holds there. | Split each into a film file and a series file. | Minor | Medium | Carried to 19 Aug rank 9 |

**Nothing was found in the parts most likely to hurt.** URL sanitisation, `PlaybackSession` and the
reconciliation rules were read and left alone: each carries its reasoning and its tests. No security finding.

### What the three finished ones changed

- **Rank 1** — `ICategoryPickerSection`, implemented by `ChannelListViewModel` and
  `CatalogueSectionViewModel<TRow>`. The interface binds the code; the markup still resolves by name, so
  `CategoryPickerViewTests` builds the real picker over each of the three sections and asserts that its
  bindings found anything at all. That test is the half an interface cannot cover.
- **Rank 2** — `TestSupport/XtreamSourceBuilder.cs`, gaining `WithId`, `WithName` and `WithCreatedUtc`, is
  now linked into the Xtream, Catalogue and WPF test projects. Each test class keeps its own factory *method*
  where its fixture differs — a name, a set of capabilities — but the object is written once. Those are not
  the pass-throughs rank 5 removed: they carry a value, not a call.
- **Rank 5** — the two forwarding methods are gone. Note that the estimate for rank 2 was wrong in the
  direction that matters: "low" assumed the copies were interchangeable, and they are not — they differ in
  name and capabilities, and 70 call sites depend on those differences. Replacing the object while keeping
  each factory's signature is what kept the change honest.

## What else is outstanding

Not refactoring, and unchanged by this review:

- **Two checks that need the subscription or a window**, carried in `Docs/verification.md`: the
  `LiveNetworkCachingMilliseconds` figure has never been measured against a real panel, and §§7–10 (the player
  controls, a failing stream's reported reason, the packaged build, the pinned categories) need a person at the screen.
- **Whatever the next milestone turns out to be.** The plan lives outside the repository.

## Before starting anything here

Kept because it applies to any change in this repository, not only to a ranked one:

- `dotnet test LTR-Player.slnx` — 779 tests, all passing. A refactor should not move that number;
  if it does, either the change is not a refactor or a test was measuring the implementation.
- **Close the player first.** MSBuild cannot replace locked DLLs and the error arrives *after* a successful
  compile, so it reads as a broken build. `build/publish.ps1` refuses outright.
- **Start the app after anything that touches dependency injection.** The compiler cannot see a missing
  registration, and the container holds the session whose disposal releases the provider connection.
- Reviews here have repeatedly found the *test double* wrong rather than the code, and twice found a
  *comment* wrong rather than either. When a mutation survives, suspect the tooling, then the fake, then the
  assertion — in that order. One "survival" in this session was a regex that never matched the file.

---

## Done

Ranks quoted in older commit messages resolve here, through two mappings.

**Post-M6 scheme → current scheme:** 2→2, 3→4, 4→6, 5→7, 6→8, 7→10, 8→11, 9→12. Its rank 1 is done and is
the first entry below. Ranks 1, 3, 5 and 9 of the current scheme are new in this review and appear in no
older one.

**Post-M4 scheme → post-M6 scheme:** 8→1, 13→2, 14→3, 9→4, 11→5, 16→6, 12→7, 17 and 20→8, 18→9. Everything
else on that list is below.

### In the same session as the renumbering — all fourteen

Cleared in one sitting. What is worth carrying forward:

- **Rank 12 · the timeline pages along its channels, and it is paged rather than scrolled.** The rank called
  scroll-driven loading "the honest fix"; the title said *page*, and paging is what it got. The reason is the
  one already written at the top of `GuideViewModel`: the time window is moved by command because each move is
  a fetch, and stating that with a button beats hiding it behind a scrollbar. The channel axis is the same
  kind of axis. Scroll-driven loading would also have meant a data-virtualising collection whose rows appear
  empty and fill in afterwards — in a grid where every row is already a mosaic of blocks, that reads as a
  fault rather than as loading.

  `MaximumRows` became `RowsPerPage`, still 200, and the notice changed from "Showing 200 of 4,531 … filter
  the channel list to see the others" to a position: "Channels 201–400 of 4,531 with guide data." The old
  wording had to tell the viewer to go and narrow their channel list, which is the thing the cap made
  necessary; nothing needs saying now beyond where they are.

  **Two guards, and both were mutation-checked.** Attaching a new set of channels resets to the first page —
  page three of a filtered list is not page three of the same list unfiltered — and a reload clamps an offset
  that has outlived the channels it indexed into, which would otherwise draw an empty timeline over a guide
  that has data. Removing either one fails exactly one test.

  **A mutation appeared to survive and had not been applied at all** — a `perl` pattern written with `\n`
  against a CRLF file. Checked the file, saw the code untouched, applied it properly, and it failed as it
  should. Worth remembering: a surviving mutation is a claim about the *tooling* first.

- **Rank 11 · half done and half dropped, on a measurement.** The cheap half was real and is fixed:
  `SelectAdjacent` copied the filtered view into a list on **every zap key press** — up to seventeen thousand
  rows, allocated to find one neighbour. It now asks the view by index. `CollectionView` offers `Count`,
  `IndexOf` and `GetItemAt`; `ICollectionView` does not, and `ListCollectionView` does **not** implement
  `IList`, which is what the first attempt assumed and the zap tests rejected within a minute.

  **The expensive half — a bounded page from the store — was dropped after measuring it.** Loading all 17,283
  channels costs about 50 ms and disappears into the noise of a process start; the window's whole catalogue
  load, view models included, is around 200 ms and happens once per source switch. Against that, paging would
  have cost two things the backlog had not listed:

  - **Zapping would stop at the page boundary.** Today a viewer can zap through every channel they can see.
  - **The search would lose case-insensitivity outside ASCII.** SQLite's `LIKE` is ASCII-only, while
    `CatalogueFilter` is `OrdinalIgnoreCase` — and this subscription's names arrive in Cyrillic, Arabic and
    Greek. The rank said to settle that difference first; settled, it is a reason not to proceed.

  **The film section is not the precedent it looked like.** It needed paging because 66,529 films cannot be
  browsed by scrolling at all — it is search-first by design. A channel list is browse-first: scrolling and
  zapping through it *is* the interaction. Same shape, different reason, and the reason is what transfers.

  Recorded rather than left implicit so the next review does not re-derive it: **the numbers are the argument,
  and they are in this entry.** If a source switch ever becomes slow enough to notice, measure again — the
  figures above are for 17,283 channels on this machine.

- **Rank 10 · the reconciliation diff came out in three pieces, not one.** The rank proposed a
  `CatalogueReconciler` that computes while the context writes. Reading it first showed the ~250 lines were
  three different things, and each wanted a different home:

  - **The matching** — index the stored rows by key, walk the incoming, remember what was seen, remove the
    rest — was written out four times, for categories, channels, films and series. That is
    `CatalogueReconciler.Match` in LTR.Persistence, 39 lines, and it decides nothing about fields.
  - **What a provider owns** is now stated on the entities in Core: `Channel.AdoptProviderFields`,
    `VodItem.AdoptListingFields`, and the two others. It belongs there because it is a fact about a channel
    rather than about a table — a favourite is the user's, a synopsis is the detail call's — and because it is
    the rule a reconciliation exists to keep.
  - **The season algorithm** (~90 lines) moved to `SeriesReconciliation` in Core unchanged in substance. It
    performed no I/O and never had: it works on entities already in hand. Living in the context meant real
    SQLite was the only way to reach it.

  `LtrDbContext.cs` 375 → 366 code lines and `.Vod.cs` 505 → 452, which is a modest return on the
  restructuring — **the point was never the count.** It was that a panel refiling an episode between seasons,
  or listing one twice, is now a six-line unit test instead of a database fixture.

  **The proof that it is the same behaviour is that no test changed.** Seventy persistence tests over real
  SQLite — favourites surviving, synopses not erased, positions travelling with a refiled episode, categories
  scoped per kind — all passed against the restructured code with not one assertion touched. Seventeen unit
  tests were then *added* for what had been unreachable, and a mutation check confirmed both layers see it:
  copying `IsFavorite` in the adopt method fails one Core test and one persistence test.

  Verified against the real subscription too, which is the case that matters: a refresh of 17,283 channels,
  66,537 films and 11,001 series left the one favourite and the stored resume position exactly where they were.

- **Rank 14 · a playlist's path credentials are removed, and the fact came from where it already was.** Not by
  comparing channels, which is what this rank proposed: the segments common to every channel include the route
  (`/live/`) as well as the credentials, and reaching the channel list from a sanitiser would have meant
  storing that comparison on the source — a migration, for a heuristic. **The values were already on the
  source**, in the query of its own playlist and guide addresses, which is where the provider put them.
  Reading them there is a fact rather than a guess, and it needed no new state at all.

  Redacted only where such a value is a **whole path segment**, which is what makes it safe: a playlist
  address also carries `output=ts`, and replacing "ts" wherever it occurred would take the extension off every
  channel — the mistake rank 13 corrected in the Xtream sanitiser, one rank earlier.

  **The uncovered case turned out to be much narrower than expected.** A playlist held as a file still has no
  query of its own — but subscription playlists declare `x-tvg-url`, the import adopts it, and its query
  carries the same credentials. So what remains is a playlist file with no guide address either. That was
  verified rather than assumed, and it reports itself honestly.

  **The end-to-end run is what earned its keep.** The unit tests passed while the real command still printed
  the credentials, which looked like a defect for several minutes — it was a stale build: the CLI had been
  run with `--no-build` against a provider assembly compiled before the change. Worth remembering next time
  the tests and the application disagree.

- **Rank 9 · a stored channel can be addressed, and it found a credential in clear.** Not by extending
  `resolve` as the rank proposed: that command's panel options are `Required`, on instances shared with three
  other commands, and two mutually exclusive ways of naming a source is what System.CommandLine cannot
  express — the same reason `vod play-test` is its own command rather than a flag on `play-test`. So there is
  a `live` group, the stored counterpart of the panel commands exactly as `vod` is: `live list` and
  `live resolve`. The listing had to come with it, because the local channel ids the resolve takes were
  previously not printed anywhere.

  `ResolvedAddressReport` now holds the "masked unless `--reveal`" rule for both resolve commands, which is
  the sort of rule worth having once.

  **What it verified is not what was expected.** The point was to give the M3U sanitiser a check through this
  command, and the first playlist pointed at it printed
  `http://provider.invalid/live/alice/s3cret/101.ts` — the documented path limitation, in clear, under a note
  that claimed credentials were masked. The note now tells the truth: when sanitising changed nothing, it says
  so and says why. The underlying gap is **rank 14**, along with the observation that makes it solvable — the
  credential segments are the ones every channel in the playlist shares.

- **Rank 8 · one class per command, and the handler that was four.** `Program.cs` went from 380 lines to 57:
  build the container, build the tree, invoke. Each command states its own options and action under
  `Commands/`. Two things came out of the move rather than travelling with it — the catalogue preparation
  `WithCatalogue<T>` did is now `CatalogueCommandRunner`, injected into the three commands that touch the
  database, and the listing limit and hold duration are stated once in `CommandDefaults` where four commands
  had their own 40 and two their own 5.

  `VodCommandHandler`'s ten dependencies were the symptom the rank named, and they separated exactly where
  the jobs did: `VodListingCommandHandler` (2 dependencies), `VodDetailCommandHandler` (2),
  `WatchProgressCommandHandler` (2), `VodPlayTestCommandHandler` (6 — it opens a stream). What was genuinely
  shared came out as two collaborators rather than being duplicated: `StoredSourceLookup`, which every
  catalogue command starts with, and `VodText`, because a position reading "at 00:40:00" in one listing and
  "2400" in another is how a check stops being a check.

  **Nothing here was unit-testable — the CLI had no test project, and this rank did not add one.** (Rank 1 of
  19 August 2026 added `LTR.Cli.Tests`; what it covers is still only the failure wording.) Verified by
  running `--help` over the whole tree (13 commands, all exit 0) and then every split handler against the
  real catalogue, including both of the error paths: an unknown source id, and `forget` with neither id.
  `vod play-test` was left alone deliberately, since it opens a stream against a one-connection subscription.

- **Rank 7 · the timeline's channel names are pinned, and the markup is measured now.** Neither of the two
  approaches the rank proposed was taken. Two vertically synchronised lists would have cost the row list its
  virtualisation, and a custom panel was more than the problem needed. Instead there is **one scroller, around
  the header only**, and everything below it follows that scroller's `HorizontalOffset` through a
  `TranslateTransform` — each row's block strip and the now-marker. The names are outside it, so they cannot
  scroll away; the shared offset is what keeps a heading over its own blocks, which is the property the old
  single-scroller layout existed to protect.

  `GuideTimeline.PixelsPerHour` stays fixed at 260, deliberately: its own comment explains that a block keeps
  its size when the panel is resized, so scaling the window to fit the pane — which would have removed
  horizontal scrolling altogether — was rejected as overturning a stated decision by side effect.

  **This is the first test in the project that builds a visual tree**, and it exists because nothing else can
  state a layout property: scroll the timeline, assert the name has not moved and that the blocks and the
  marker have moved by exactly as much. Every WPF defect this repository has shipped was in markup, which was
  the part nothing measured. It runs on an STA thread of its own and loads `Theme.xaml`, so the converters and
  styles resolve as they do in the application.

  It also settles a question the compiler does not answer: an `ElementName` binding reaching out of a
  `DataTemplate` into the file's outer namescope does resolve — which the two bindings already in that
  template depended on without anything proving it.

- **Rank 13 · a credential is removed where the protocol puts it, not wherever it occurs.** A query value or
  a path segment that *is* the credential goes; the host, the path and `action=…` stay. Found by running the
  previous rank's verification with `--user x --pass y`, which logged
  `http://hd-ma***.org:8080/pla***er_api.php` — both diagnosed things redacted, and the credentials no better
  hidden for it.

  **The interesting part is the fallback, because this change moves risk in the unsafe direction.** Replacing
  by value can only over-redact; matching structurally can *under*-redact, if a panel spells a credential
  somewhere the rule does not recognise. So when the structural pass finds a secret nowhere, the old wholesale
  replacement runs for that secret instead — judged per credential, so a properly spelled username does not
  exempt a password hidden elsewhere in the same address. Two tests cover the fallback and both fail without
  it, which was checked by removing it.

  What remains uncovered, and is written down rather than solved: one credential appearing *both* in its
  proper place and buried in something else in the same address. The alternative was redacting every address
  down to uselessness, which is what this replaced.

  `SensitiveUrlSanitizer` gained the selective overload of `RedactQueryValues` and a `RedactPathSegments`, so
  the query-splitting stayed in one place rather than being written a second time for one protocol.

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
