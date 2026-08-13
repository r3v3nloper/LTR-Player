# LTR-Player

A Windows IPTV player for Xtream Codes panels and M3U playlists. WPF shell, LibVLC engine, SQLite
catalogue. The user supplies their own subscription; no playlists, credentials or provider discovery
ship with it.

The global conventions in `~/.claude/CLAUDE.md` apply. What follows is only what this project knows
that its code does not state — most of it learned by getting it wrong once.

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
LTR.Player.Wpf                 The only project that references WPF. MainViewModel composes
                               SourceManagementViewModel and ChannelListViewModel and is their
                               ISourceCoordinator; the two halves never reference each other
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
- **`MigrationTests` migrates an empty database and proves only that the schema builds.**
  `MigrationUpgradeTests` is the one that matters for shipped installations: SQLite cannot alter a
  constraint in place, so EF implements one by rebuilding the table, and a rebuild is what silently empties
  it. Add a case there for every migration that alters an existing table.

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
- **Dispose the DI container asynchronously.** It holds `IAsyncDisposable` singletons; the synchronous
  `Dispose` throws and `PlaybackSession` never releases its stream.
- **A view model that reads the clock must be given a `TimeProvider`.** `DateTimeOffset.UtcNow` in
  `ToggleGuideAsync` opened the timeline on a window that could not contain the guide's own "now" marker;
  the test caught it only because the test clock differs from the real one.

## Verifying

`Docs/verification.md` has the full sequence. In short:

```bash
dotnet run --project src/LTR.Cli -- probe    --url http://HOST:PORT --user U --pass P
dotnet run --project src/LTR.Cli -- channels --url http://HOST:PORT --user U --pass P
dotnet run --project src/LTR.Cli -- play-test --url http://HOST:PORT --user U --pass P --stream-id ID
dotnet run --project src/LTR.Cli -- guide import --source-id ID
```

`play-test`'s last line is the real test: it polls the panel until it reports the connection released.
`guide import`'s `Matched N of M` is the equivalent for the guide — everything else about an import can
succeed while it achieves nothing.

`sources add-playlist <path-to.m3u>` seeds a source with no credentials, which is how UI behaviour that
needs a configured source gets verified without a subscription.

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
