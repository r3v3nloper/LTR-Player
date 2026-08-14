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
| **M5** Player polish — OSD, fullscreen, keyboard, tracks | not started | next; the seek bar belongs here |
| **M6** Hardening — error handling, settings, packaging | not started | the corrupt-database quarantine landed early, with M3 |

M3's timeline scrolls its channel names out of view with the programme blocks, and draws at most 200 rows.
Both are stated on screen and carried as ranks 11 and 18 in `Docs/refactoring-backlog.md`; neither is
considered a gap in the milestone.

M4 resumes but does not seek: a viewer can carry on where they left off, and cannot scrub. That is M5's OSD
work, not an omission from M4. M4 is merged into `main`; ranks 1–7 of the post-M4 review are cleared, and
nothing left in the backlog has an effect while the player is running.

### Starting M5

The plan calls for the OSD inside `VideoView.Content`, fullscreen, keyboard control (zap, volume, fullscreen,
info), audio and subtitle track selection, aspect ratio, and lower zapping latency. What M4 leaves in place
for it:

- **`PlaybackCoordinator` is where playback lives.** It owns the session, the position sampling and the
  progress recorder, and it is the only thing that opens a stream. An OSD needs the position, the duration
  and a seek — the first two are already there; a seek is not, and `IMediaEngine` has no `SeekTo`.
- **Seeking is unbuilt on purpose.** `MediaRequest.StartAt` covers resuming and is honoured by seeking right
  after the first `Playing` event, for a measured reason recorded in `LibVlcMediaEngine.ApplyStartPosition`.
  A seek bar wants the same call exposed through `IPlaybackSession`.
- **The position timer already exists** — `MainWindow` samples every five seconds for the resume recorder.
  An OSD wants something faster while it is visible, and that is a second interval rather than a new timer.
- **Backlog rank 15 belongs to M5.** Recording progress when a film reaches its own end needs
  `IPlaybackSession` to say *why* it stopped, which is the same change a seek bar wants.
- **`MediaTrack` and `IMediaEngine.GetTracks`/`SelectTrack` exist and are unused by the window.** The CLI's
  `play-test` prints them, which is the only thing exercising them today.
- **Overlays go inside `VideoView.Content`.** `Views/GuideOverlayView.xaml` is the worked example; the window
  hosts it, and the reason is the first WPF trap below.

## Layout

```
LTR.Core[.Abstractions]        Domain. Platform-neutral, no dependencies. Keep it that way —
                              a web frontend is planned and would reuse it.
LTR.Providers[.Abstractions]   IContentProvider, probes, resolvers + the registry that selects them
LTR.Providers.Xtream           player_api.php client
LTR.Providers.M3u              M3U-Plus parser and provider
LTR.Catalogue[.Abstractions]   Application layer: import orchestration and catalogue access
LTR.Epg.Xmltv                  XMLTV reader. No dependencies at all — not even on Core
LTR.Persistence                LtrDbContext. All database logic lives here (§3.3.2)
LTR.Playback[.Abstractions]    Engine-neutral playback policy
LTR.Playback.LibVlc            LibVLC engine
LTR.Security.Dpapi             Windows credential protection, kept out of Core on purpose
LTR.Cli                        Headless verification of everything below the UI (§2.12)
LTR.Player.Wpf                 The only project that references WPF. MainViewModel composes the four
                               catalogue sections and the guide and is their ISourceCoordinator; the
                               sections never reference each other. Views/ holds one UserControl per
                               section, so MainWindow.xaml is composition only
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
reject unfamiliar user agents, and redirect to streaming nodes.

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
- **A panel numbers its category identifiers per section,** so `58` is a live category and a film category
  at once. Category reconciliation is therefore scoped to the *kinds* an import covers, not to the source —
  scoped to the source, a live refresh deletes every film category — and its lookup is keyed by
  `(ExternalId, Kind)`, because a dictionary keyed by the identifier alone throws on the duplicate.
- **A listing may overwrite what a listing owns, and must never blank out what a detail call supplied.**
  Panels state a synopsis in `get_vod_info` and not in `get_vod_streams`, so a refresh that assigned the
  listing's fields unconditionally would erase every synopsis the player had fetched.

## WPF traps, each of which shipped a bug once

- **Overlays belong inside `VideoView.Content`**, not beside it. `VideoView` hosts a separate native
  window over the WPF tree; a sibling element is invisible behind the video.
- **Every command guard needs `[NotifyCanExecuteChangedFor]` on every property it reads.** Three
  defects came from omitting it. Note that `CanExecute` invokes the guard directly and therefore passes
  even with the bug — tests must assert the *notification*. The attribute cannot cross an object
  boundary: `PlaySelectedCommand` lives on `MainViewModel` and guards on `ChannelListViewModel`'s
  selection, so that one notification is wired by hand from a `PropertyChanged` subscription.
- **`InvariantGlobalization` must stay off.** WPF's binding engine throws from
  `XmlLanguage.GetSpecificCulture` without culture data, and every binding in the window fails while it
  still looks fine.
- **A retemplated `ComboBox` must read `ItemTemplate`,** not `SelectionBoxItemTemplate`, which does not
  resolve with `DisplayMemberPath` and leaves the control rendering `ToString()`.
- **`Progress<T>` and `ICollectionView.Refresh` both matter:** a refresh resets the collection and the
  list box drops its selection, so it has to be restored.
- **Fill a bound collection before selecting in it.** Emptying one makes a `ComboBox` write a null selection
  back through the binding, so a selection assigned first is discarded. Both new pickers rendered blank while
  their lists looked perfectly correct, because the filter read the same null as "every category".
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
```

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
  `MSB3021`/`MSB3027` and they follow a successful compile. Ask for the app to be closed.
- **Do not delete `%LOCALAPPDATA%\LTR-Player\logs`.** It is the diagnostic trail, and deleting it to
  make error-checking easier destroyed the evidence for a real question once.
- **Do not infer database state from the file.** While a process holds it open, Windows reports stale
  size and `File.ReadAllBytes` reads only that much. Startup logs the database path and the source,
  channel, category and favourite counts — use those, or `sources list`.
- **Restoring a file from a backup can keep its old timestamp,** and MSBuild will then reuse the old
  binary. Touch the file if a test result looks impossible.
- Migrations need explicit approval before being created (§3.3.1). `MigrationTests` fails when the
  model drifts from them, which is how drift gets noticed.

`Docs/refactoring-backlog.md` holds the reviewed, ranked work that remains.
