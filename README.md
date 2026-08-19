# LTR-Player

A Windows IPTV player for Xtream Codes panels and M3U playlists — live TV, a programme guide, films and
series, in one window that keeps working when the provider does not.

**You supply your own subscription.** No playlists, no credentials and no provider discovery ship with this
application, and it neither finds nor hosts any content.

> Version 0.8.1. Self-contained builds run on a machine with no .NET installed. The build is unsigned, so
> SmartScreen warns on first run.

---

## What it does

**Live TV.** Import a source, browse or search seventeen thousand channels, filter by category, star the
ones you watch. Decorative separator rows that some panels put in their channel lists are recognised and
skipped rather than offered as channels.

**A programme guide.** XMLTV import, now/next on the channel list, and a scrolling timeline overlay with
pinned channel names. Guide channels are matched to yours by identifier *and* by name — on a real
subscription 72% of channels carry no `tvg-id`, so name matching is the primary path rather than a
fallback.

**Films and series.** A searchable catalogue (66,000 films and 11,000 series on the subscription this was
built against), seasons and episodes fetched when a series is opened, synopsis and artwork, and a
continue-watching list that remembers where you stopped.

**Player controls.** On-screen transport that wakes on the pointer and hides itself again, seeking, volume,
audio and subtitle track selection, aspect-ratio cycling, fullscreen, and keyboard shortcuts.

**Pinned categories.** A star moves a category to the top of its picker. The provider's own order is kept
underneath, so unpinning puts a category back where it was — and a refresh may rename or reposition it
without losing the pin.

**Several sources at once.** Xtream panels and M3U playlists side by side. Capabilities are probed per
source, because Xtream panels are divergent forks with no specification between them.

### Keyboard

| | |
|---|---|
| `Space` | play / pause |
| `PageUp` / `PageDown` | previous / next episode, or channel |
| `←` / `→` | skip back / forward |
| `+` / `−` / `M` | volume up / down / mute |
| `F` or `F11` / `Esc` | fullscreen / leave fullscreen |
| `G` / `I` / `A` | guide / info / aspect ratio |

The arrow keys skip rather than zap: the channel list needs them to move a selection through seventeen
thousand entries without opening every one on the way. Shortcuts are ignored while you are typing into a
search box.

Previous and next follow what is playing. Watching a series, they move through its episodes and carry on into
the next season; watching live, they change channel. A film has neither, so they are unavailable.

## Install

Unpack the zip anywhere and run `LTR-Player.exe`.

There is nothing to register, no service and no installer. The player writes only to
`%LOCALAPPDATA%\LTR-Player` — the catalogue database, `settings.json` and the logs. Deleting the folder you
unpacked is the uninstall; that data directory stays behind on purpose, so an upgrade does not discard your
favourites, your tuning or the diagnostic trail.

Windows warns on first run because the build is unsigned. A certificate is a purchase, not a build step.

### Adding a source

- **Xtream panel** — host, port, username, password. Credentials are protected with Windows DPAPI and are
  never printed: anything about to log, print or store an address asks a per-protocol sanitiser first,
  because on both protocols the credentials travel *inside* the address.
- **M3U playlist** — a URL or a local file. A playlist source holds no credentials of its own, so its
  addresses are masked by parameter name, and by the values recorded in its own playlist and guide URLs.
- **Guide** — an XMLTV URL, imported per source. `Matched N of M` is the line worth reading; everything
  else about a guide import can succeed while achieving nothing.

## Build from source

Requires the **.NET 10 SDK** and Windows.

```bash
dotnet build LTR-Player.slnx
```

```bash
dotnet test LTR-Player.slnx
```

739 tests. To produce the shipping folder and zip under `artifacts/`:

```powershell
pwsh build/publish.ps1
```

That script runs the tests, publishes self-contained win-x64, and refuses to zip unless LibVLC's natives
and the third-party notices are genuinely in the output. Their absence produces a build that starts, opens
its window, loads the catalogue and plays nothing, with no warning at all — `Docs/packaging.md` explains
how.

Note that **the build fails while the player is running**: MSBuild cannot replace locked DLLs.

## The command line

`LTR.Cli` reaches everything below the UI, which is what makes the provider, catalogue and playback layers
verifiable against a real panel without WPF in the way.

```bash
dotnet run --project src/LTR.Cli -- probe     --url http://HOST:PORT --user U --pass P
dotnet run --project src/LTR.Cli -- channels  --url http://HOST:PORT --user U --pass P --filter sport
dotnet run --project src/LTR.Cli -- resolve   --url http://HOST:PORT --user U --pass P --stream-id 1234
dotnet run --project src/LTR.Cli -- play-test --url http://HOST:PORT --user U --pass P --stream-id 1234
```

```bash
dotnet run --project src/LTR.Cli -- sources list
dotnet run --project src/LTR.Cli -- sources add-playlist path/to.m3u
dotnet run --project src/LTR.Cli -- sources refresh 1
dotnet run --project src/LTR.Cli -- guide import  --source-id 1
dotnet run --project src/LTR.Cli -- live list     --source-id 1 --filter erste
dotnet run --project src/LTR.Cli -- vod list      --source-id 1 --filter dune
dotnet run --project src/LTR.Cli -- vod episodes  --source-id 1 --series-id 42
dotnet run --project src/LTR.Cli -- vod play-test --source-id 1 --movie-id 42 --start-at 2400
```

Addresses print with credentials removed. `resolve --reveal` is the one exception, so a URL can be pasted
into VLC to separate "our address is wrong" from "our player is wrong". `Docs/verification.md` has the full
sequence and says what each closing line means.

**Run the play-tests one at a time.** A one-connection subscription answers the next stream with HTTP 200
and an empty body while it still counts the previous one, which reads exactly like a broken film.

## How it is built

WPF shell, LibVLC engine, SQLite catalogue, EF Core, .NET 10.

```
LTR.Core[.Abstractions]        Domain. Platform-neutral, no dependencies at all.
LTR.Providers[.Abstractions]   Protocol-neutral contracts plus the registry that selects an
LTR.Providers.Xtream           implementation per source: a player_api.php client, and
LTR.Providers.M3u              an M3U-Plus parser.
LTR.Catalogue[.Abstractions]   Import orchestration and catalogue access. One store behind five narrow
                               interfaces, so a consumer declares only the face it uses.
LTR.Epg.Xmltv                  XMLTV reader. Depends on nothing, not even Core.
LTR.Persistence                LtrDbContext. All database logic lives here.
LTR.Playback[.Abstractions]    Engine-neutral playback policy.
LTR.Playback.LibVlc            LibVLC engine.
LTR.Security.Dpapi             Windows credential protection, kept out of Core deliberately.
LTR.Cli                        Headless verification of everything below the UI.
LTR.Player.Wpf                 The only project that references WPF.
```

Dependencies point one way: apps → Catalogue/Providers/Playback → `*.Abstractions` → Core. Core knows
nobody, and the WPF project does not reference `LTR.Persistence`. A web front end is the reason that line
is held.

### Two constraints that shaped everything

**A subscription permits very few concurrent connections — one, for the provider this was built against.**
A stream left open locks the account out for minutes. So all playback goes through a single
`IPlaybackSession` that stops fully before it starts and never abandons the stop, neither on caller
cancellation nor when a newer request supersedes it. Rapid channel changes resolve by generation, so
intermediate requests are dropped rather than each opened in turn. Stream URLs are never probed either:
opening one occupies a slot, and a probe that locks you out of your own subscription is worse than
guessing a container extension and correcting on the 404.

**Xtream panels are divergent forks with no specification.** Capabilities are probed per source and no
endpoint is assumed to exist. The same scalar arrives as `5`, `"5"`, `""` or `null` depending on the panel;
panels serve HTML error pages at HTTP 200, reject unfamiliar user agents, redirect to streaming nodes, and
— being PHP files — sometimes emit a byte-order mark ahead of their JSON. Response bodies are therefore
streamed, and their first 512 bytes inspected, before anything tries to parse them.

## Documentation

| | |
|---|---|
| [`Docs/player.md`](Docs/player.md) | the on-screen controls, and where the transport lives |
| [`Docs/epg.md`](Docs/epg.md) | guide matching, and why matching by name is the primary path |
| [`Docs/vod.md`](Docs/vod.md) | films and series, paging, and the two-pass season fetch |
| [`Docs/categories.md`](Docs/categories.md) | pinned categories |
| [`Docs/packaging.md`](Docs/packaging.md) | what a publish produces, and the LGPL obligations |
| [`Docs/release-notes.md`](Docs/release-notes.md) | one entry per tag: what changed, and what is known and unchanged |
| [`Docs/verification.md`](Docs/verification.md) | the full check sequence against a live panel |
| [`Docs/refactoring-backlog.md`](Docs/refactoring-backlog.md) | reviewed, ranked work that remains — including what was rejected |
| [`CLAUDE.md`](CLAUDE.md) | conventions, plus every trap this repository shipped a bug over once |

## Licence

This application's own code is [MIT](LICENSE) — Copyright (c) 2026 r3v3nloper.

LibVLC is LGPL-2.1-or-later, used unmodified and dynamically linked, which is what keeps its terms off the
code above. Its libraries stay separate, replaceable files under `libvlc\win-x64\`, and
`THIRD-PARTY-NOTICES.txt` ships beside the executable naming the licence, the upstream source and where to
get it.
