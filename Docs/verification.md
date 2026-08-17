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

```bash
dotnet run --project src/LTR.Cli -- live list    --source-id 1 --filter erste
dotnet run --project src/LTR.Cli -- live resolve --source-id 1 --channel-id 2037
```

The same thing for a source already imported, and **the only way to reach a playlist's addresses**: a panel's
address is composed from credentials, a playlist's arrives inside the playlist and exists only in the
catalogue. `live list` prints the local channel ids `live resolve` takes.

For a playlist whose credentials sit in the *path* rather than the query, the address comes back unmasked and
says so — nothing on record distinguishes a secret segment from a route. That is rank 14 in
`Docs/refactoring-backlog.md`.

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

**The log line worth reading afterwards** is how long the open took — `Erste began playing 780 ms after the
open was issued`. That is the part of a channel change that can be tuned, and
`LibVlcOptions.LiveNetworkCachingMilliseconds` is what tunes it. Its 600 ms default is a starting point rather
than a measured optimum, because the right figure belongs to the provider: raise it if channels stutter in
their first seconds. The release that precedes an open is *not* in that figure, deliberately — it is required
by the connection limit and cannot be shortened.

Add `--verbose` to any command to see the requests being made. Logged addresses always have their
credentials stripped, because diagnostic output is what people paste into forums. That holds for a playlist
source as well as a panel: each protocol has a sanitiser of its own, and a playlist's address has every
query value removed since nothing here can tell which of them is the credential.

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
dotnet run --project src/LTR.Cli -- vod show --source-id 1 --movie-id 1
```

Fetches the film's detail if it has never been read, and its `Detail` line is where to check that a
detail is asked for **once**: `fetched`, `not available (never asked)`, or `not available (asked <when>)`.
That last form is the one to watch — a second run must show the same timestamp rather than a fresh one, or
every viewing is asking a panel that has nothing to say. The panel this was built against answered with a
synopsis for every film sampled, so provoking the empty case may need a different one.

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
connections, exactly as the live `play-test` does — and now literally so: both run the same hold, so both
print the state transitions, list the tracks they found and ask the provider why a stream would not start.
Before that was shared, each printed roughly half of it.

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
dotnet run --project src/LTR.Cli -- vod play-test --source-id 1 --movie-id 18848 --seek-to 1800 --seconds 20
```

**This is the only headless check of the seek bar**, and it is a different code path from `--start-at`: that
one is honoured while the stream opens, this one against a stream already playing. The command holds, seeks,
holds again and prints `Sought to`. Give it `--seconds 20` or more — a seek over HTTP is answered by a fresh
range request, and read too early the engine still reports the old position, which looks exactly like a seek
that did nothing.

`Seek refused` means the panel serves this film without range support. That is a fact about the panel: the
window hides the seek bar for such a stream rather than offering one that cannot work.

```bash
dotnet run --project src/LTR.Cli -- vod play-test --source-id 1 --episode-id 63 --start-at 600 --remember
dotnet run --project src/LTR.Cli -- vod continue --source-id 1
dotnet run --project src/LTR.Cli -- vod forget   --source-id 1 --episode-id 63
```

`--remember` records the position as the window does, which is what makes the whole resume loop checkable
without the UI: play, see it listed, take it off again. `forget` is the command-line counterpart of the
list's own remove button — it clears the position and deliberately does **not** mark the item watched,
because nobody watched it, nor touch when it was last watched, because removing an entry is not watching it.
`vod continue` prints that timestamp, which is where the old behaviour was visible: an entry taken off the
list came back stamped as the most recently watched thing in the catalogue.

> **Run these one at a time.** A subscription permitting a single connection answers the next stream with
> HTTP 200 and an empty body while it still counts the previous one, and nothing about that reads as a
> limit — it looks exactly like a broken film. The command waits for the release and says so; give it the
> chance to finish.

```bash
dotnet run --project src/LTR.Cli -- vod continue --source-id 1
```

Lists what is part-watched after a play-test that got far enough in. Nothing appears for less than a
minute watched, or for an item stopped within two minutes of its end — both are the resume policy working.

## 7. Do the player controls work?

Nothing below the UI can answer this; it needs the window. `Docs/player.md` has the key map. The checks worth
making, in the order that finds problems fastest:

1. **Play a channel.** The controls appear, say what is playing, and take themselves away after four seconds.
   The pointer moving over the picture brings them back.
2. **Zap with Page Up and Page Down.** Each press changes channel once. Then narrow the list with the search
   box and zap again — it must walk only what the list is showing.
3. **Type in the search box.** `a`, `f`, `g`, `i` and `m` must reach the box rather than the player. This is
   the one that fails if the keyboard is ever moved into markup.
4. **Press F, then Escape.** The panel goes and comes back at the width you left it, not at 360 pixels.
5. **Play a film and drag the seek bar.** The thumb must stay under the pointer while held, and playback move
   on release. Then press Left and Right for ten-second steps.
6. **Open the audio menu on a channel that has two languages.** Switch, and confirm the picker still shows the
   right entry a few seconds later rather than snapping back.
7. **Let a film play to its end.** It must come off the continue-watching list without the window being
   closed first — that is the one this milestone fixed, and the failure mode is silent.

## 8. Does a failing stream say why?

Since M6 a refused stream asks the provider rather than guessing, and the CLI prints the answer:

```
Playback error: Could not play stream 1234. ...
Reason:  ConnectionLimitReached
         The panel counts every permitted connection as in use. ...
```

`ConnectionLimitReached` immediately after another play-test is the expected answer, not a defect — the panel
is still counting the previous connection. `ChannelUnavailable` means the account is healthy and a connection
is free, so the channel itself is off the air. Deliberately, a playlist source is never asked: it has no
account, and asking would re-download the whole document to answer a different question.

## 9. Does the packaged build run?

`Docs/packaging.md` has the detail. The short version:

```bash
pwsh build/publish.ps1
```

Then run the result **with its working directory somewhere else**, which is what distinguishes natives resolved
relative to the application from natives found because the shell started in the right folder:

```powershell
Start-Process artifacts\publish\LTR-Player.exe -WorkingDirectory C:\
```

A window that opens and plays nothing is the failure mode to watch for: it means LibVLC's natives are absent,
which is silent everywhere else. The script checks for them by name and refuses to zip without them.

## Automated tests

```bash
dotnet test LTR-Player.slnx
```

No test contacts a real provider. Panel behaviour is served by an in-process Kestrel host, playback
is exercised against a fake engine that fails if two streams are ever open at once, and the database
tests run on real SQLite so unique indexes and cascade rules are genuinely covered.
