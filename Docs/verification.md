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

## Automated tests

```bash
dotnet test LTR-Player.slnx
```

No test contacts a real provider. Panel behaviour is served by an in-process Kestrel host, playback
is exercised against a fake engine that fails if two streams are ever open at once, and the database
tests run on real SQLite so unique indexes and cascade rules are genuinely covered.
