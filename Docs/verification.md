# Verifying against a real provider

The core is reachable without the UI, so everything below WPF can be checked against a live panel
from the command line. Run these before trusting a change to the provider or playback layers.

## Prerequisites

- .NET 10 SDK
- Your own Xtream subscription. Nothing here ships credentials or discovers providers.

```bash
dotnet build LTR-Player.slnx
```

## 1. Does the panel work, and what does it support?

```bash
dotnet run --project src/LTR.Cli -- probe --url http://HOST:PORT --user USER --pass PASS
```

Reports the subscription status, expiry, connection limit and the probed feature set. Two lines
deserve attention:

- **`Connections N of M in use`** — if `N` is non-zero while nothing is playing, an earlier session
  leaked a connection. That is the failure mode the whole playback design guards against.
- **Capabilities** — panels differ. A `no` next to `series` or `xmltv guide` is a fact about that
  panel, not a defect.

## 2. Is the catalogue read correctly?

```bash
dotnet run --project src/LTR.Cli -- channels --url http://HOST:PORT --user USER --pass PASS --filter sport
```

Prints stream ids, names, categories and catch-up availability. The closing note counts channels with
no guide id — that number predicts how much of the EPG will need name-based matching.

## 3. Is the stream address built correctly?

```bash
dotnet run --project src/LTR.Cli -- resolve --url http://HOST:PORT --user USER --pass PASS --stream-id 1234 --probe
```

Credentials are masked unless `--reveal` is passed. With `--reveal`, the address can be pasted into
VLC to separate "our URL is wrong" from "our player is wrong". Add `--probe` so the container format
matches what the panel actually serves rather than the configured preference.

## 4. Does playback work — and is the connection handed back?

```bash
dotnet run --project src/LTR.Cli -- play-test --url http://HOST:PORT --user USER --pass PASS --stream-id 1234
```

Opens the stream headlessly, prints every state transition, lists the tracks it discovered, then
releases and asks the panel how many connections it still counts.

**The last line is the real test.** Playing a stream is easy; handing the connection back reliably is
the part that decides whether the player is usable day to day. A subscription typically permits one
or two concurrent connections, and a provider counts one as open until the client closes it — so a
leak locks the user out of their own subscription for minutes.

Expect this sequence, and note that the first `Stopped` is deliberate: the session always releases
before it opens, unconditionally.

```
state: Idle -> Stopped
state: Stopped -> Opening
state: Opening -> Buffering
state: Buffering -> Playing
```

Add `--verbose` to any command to see the requests being made. Logged addresses always have their
credentials stripped, because diagnostic output is what people paste into forums.

## 5. Is the programme guide usable?

The guide works against a stored source, because matching it needs that source's channel list. Add the
source first (in the window, or with `sources add-playlist`), then:

```bash
dotnet run --project src/LTR.Cli -- guide import --source-id 1
```

A second run reports the stored guide as still fresh; `--force` fetches it regardless. `guide show`
reports the same figures without downloading anything.

**The line that matters is `Matched`.** A guide can download, parse and store perfectly and still be
useless, because the guide and the channel list are published by different parties and their channel
names need not resemble each other. Expect well under 100%: a real subscription lists regional and
duplicate channels no guide covers. Below roughly 30%, look at the unmatched sample the command prints —
if those channels all carry a guide id, the guide is for a different line-up.

`On air at ...` is the check that the times were read with the right offset. A guide two hours out looks
perfectly healthy in every count above and wrong in that one list.

## 6. Are the films and series usable?

Everything here works against a stored source, because it reads and writes the stored catalogue. Import
one first — `sources refresh` is the only way to fetch an Xtream catalogue without the window:

```bash
dotnet run --project src/LTR.Cli -- sources refresh 1
```

The closing line counts channels, films and series. **Zero films on a subscription that sells them means
the capability probe said no**, which `probe` reports — not that the import failed.

```bash
dotnet run --project src/LTR.Cli -- vod list --source-id 1 --filter matrix
dotnet run --project src/LTR.Cli -- vod series --source-id 1 --filter breaking
```

Both print local ids, which everything below takes. The `cont` column is the container extension the film
is stored in; it is part of the address, so a film listed without one will be requested as `mp4` and may
404.

```bash
dotnet run --project src/LTR.Cli -- vod episodes --source-id 1 --series-id 1738
```

**This is the command worth running against every new panel.** Three shapes of episode listing are in
circulation — an object keyed by season number, an array of season arrays, and one flat array — and a panel
using an unreadable one produces a series with no episodes rather than an error. A second run prints the
same thing without asking the panel: the seasons are cached until the panel reports the series changed.

```bash
dotnet run --project src/LTR.Cli -- vod play-test --source-id 1 --movie-id 18848 --start-at 2400
```

Opens a stored film or episode (`--episode-id`), holds it, then releases it and asks the panel about its
connections, exactly as the live `play-test` does.

**`Position` is the line to read.** With `--start-at 2400` it should report roughly `00:40:00`, which is the
proof the resume seek took — a film that silently restarts from the beginning looks perfectly healthy from
every other angle.

`unknown` is *not* proof of failure, and this was learned the hard way: a deep seek over HTTP can take
longer than the command holds the stream, and the same file that reported `00:05:04` at `--start-at 300`
reported `unknown` at `--start-at 600`. Hold it longer (`--seconds 30`) before concluding anything. Both the
window and `--remember` fall back to the position playback was asked to start at for exactly this reason, so
a slow seek costs a viewer nothing.

`Duration` matters for the other half of resuming — an item whose length is unknown can never be recognised
as finished, so it stays on the continue-watching list for good.

```bash
dotnet run --project src/LTR.Cli -- vod play-test --source-id 1 --episode-id 63 --start-at 600 --remember
dotnet run --project src/LTR.Cli -- vod continue --source-id 1
dotnet run --project src/LTR.Cli -- vod forget   --source-id 1 --episode-id 63
```

`--remember` records the position as the window does, which is what makes the whole resume loop checkable
without the UI: play, see it listed, take it off again. `forget` is the command-line counterpart of the
list's own remove button — it clears the position and deliberately does **not** mark the item watched,
because nobody watched it.

> **Run these one at a time.** A subscription permitting a single connection answers the next stream with
> HTTP 200 and an empty body while it still counts the previous one, and nothing about that reads as a
> limit — it looks exactly like a broken film. The command waits for the release and says so; give it the
> chance to finish.

```bash
dotnet run --project src/LTR.Cli -- vod continue --source-id 1
```

Lists what is part-watched after a play-test that got far enough in. Nothing appears for less than a
minute watched, or for an item stopped within two minutes of its end — both are the resume policy working.

## Automated tests

```bash
dotnet test LTR-Player.slnx
```

No test contacts a real provider. Panel behaviour is served by an in-process Kestrel host, playback
is exercised against a fake engine that fails if two streams are ever open at once, and the database
tests run on real SQLite so unique indexes and cascade rules are genuinely covered.
