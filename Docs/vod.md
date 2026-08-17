# Films and series

What M4 built, and the reasoning that is not obvious from the code.

## The film catalogue is not a bigger channel list

The subscription this was built against lists **17,156 channels, 66,447 films and 10,967 series**. That
ratio is what shaped the section: the channel list holds every channel in memory and filters it there,
which works at seventeen thousand and would not at sixty-six.

So the film and series sections do not hold the catalogue. They ask the store for a page:

```
SearchMoviesAsync(sourceId, filter, limit: 200) → CataloguePage<VodItem>(Items, TotalMatching)
```

and say on screen how much they are not showing. Nobody browses sixty-six thousand films by scrolling; the
section is a search box with results, which is how a catalogue that size is actually used.

The name criterion is `LIKE` with escaped wildcards, which SQLite applies case-insensitively to ASCII. That
is a near match for the in-memory `CatalogueFilter` and not an exact one — accented letters compare
case-sensitively in the database and not in memory. The near match is deliberate: applying the rule exactly
would mean reading the whole table.

## Two passes, not one

A series' seasons are **not** part of an import. One `get_series_info` call per series against eleven
thousand series would take hours and hammer the panel, so an import stores a shallow listing and the
seasons are fetched when a series is opened.

That leaves the question of when a cached copy will no longer do, and the answer is not a clock:

```
Series.LastModifiedUtc    what the provider currently reports  (adopted by every refresh)
Series.DetailModifiedUtc  what it reported when the seasons were read
Series.HasCurrentDetail   the two agree
```

A series nobody has changed is never fetched twice however old the stored copy is; one that gained an
episode last night is fetched again the next time it is opened. `IVodDetailService` owns that decision,
because the store knows what it holds and the provider knows what the panel has and neither can decide
alone.

A film's detail works the same way, on two fields rather than one. `VodItem.HasDetail` records that a detail
arrived; `DetailAttemptedUtc` records that the panel was asked. A panel answering with nothing sets only the
second, so the film is not asked about again for a day — an empty answer today is not proof of an empty answer
next week, but it is proof enough for one viewing.

**The distinction that had to be built for it** is inside `VodDetailService`: a panel with nothing to say and a
panel that could not be reached produce the same `null`, and remembering the second as an answer would
suppress the retry over a momentary outage. `TryFetchAsync` therefore reports whether the provider answered at
all. `vod show` prints which of the three states a film is in.

**Both degrade rather than fail.** A panel that cannot be reached leaves the stored copy on screen, because
last week's episode list beats an error where the episodes should be.

## Episode listings arrive in three shapes

The documented shape is an object keyed by season number. Several forks send an array of season arrays
instead, and a few send one flat array of episodes:

```json
{"episodes": {"1": [...], "2": [...]}}        // documented
{"episodes": [[], [...], [...]]}              // array of season arrays
{"episodes": [{...}, {...}]}                  // one flat array
{"episodes": {"Season 3": [...]}}             // keyed by name, so the key means nothing
```

`XtreamSeriesInfoResponseDto.Episodes` is therefore raw `JsonElement` and the mapper inspects it. A typed
property would deserialise exactly one of these and throw on the rest, losing a whole series over its
container.

Each episode's own `season` field is trusted over the key it was filed under, because the panels that key
by name are exactly the ones where the key is useless. Seasons are then **derived from the episodes**: a
great many panels send an empty `seasons` array for a series with eight of them, and a declared season with
no episodes is not shown because there would be nothing in it to play.

## PHP makes an empty object indistinguishable from an empty list

An empty associative array and an empty list are the same value in PHP, so a panel with nothing to say
about a film answers `"info": []` where an object belongs. `TolerantObjectConverter<T>` reads that as
absent. A shape even that does not cover leaves the detail call answering "no detail" rather than throwing:
the film plays perfectly well without a synopsis, and opening its page must not become an error.

## Resuming

Three thresholds, all in `ResumePolicy` in the core so both applications and the planned web frontend agree:

| | |
|---|---|
| under 1 minute watched | nothing is remembered — opening something and changing your mind is not watching it |
| within 2 minutes of the end, or past 98% | finished; the position is cleared and it leaves the list |
| anything else | resumable, and resuming starts 10 seconds earlier for context |

Two details are not obvious and both were arrived at by measurement:

**The position is sampled on a timer while playback runs, not read when it stops.** By the time a stream is
closed the engine has no position to report, so a recorder that only looked when asked to save would always
save nothing. The window samples every five seconds and takes one more sample on the way out, which bounds
what a viewer loses to five seconds.

**Resuming seeks after the stream opens rather than using LibVLC's `start-time`.** Handed `start-time`, the
Matroska demuxer issues its seek before it has read the file's cues and answers it by prerolling from byte
1036 — reading the whole film forward to reach the requested moment. Against a remote film that never
arrives:

```
LibVLC mkv: seek request to i_pos = 2400000000
LibVLC mkv: seek: preroll{ req: 2400000001, start-pts: 1, start-fpos: 1036}
```

The stream reports itself as playing while its position stays unknown for minutes. Issued after the first
`Playing` event, the same seek uses the loaded index and lands immediately. The cost is a fraction of a
second of the opening playing first, which is a great deal better than a resume that never completes.

An unknown duration is treated as resumable rather than finished. Remembering a position is recoverable;
wrongly declaring something finished loses the viewer's place.

**A deep seek can take longer than the first few seconds of playback**, during which the engine reports no
position at all. That is why the recorder is seeded with the position playback was *asked* to start at: a
viewer who resumes at forty minutes and closes the player before the first sample arrives would otherwise
have their place reset to the beginning. The same file was observed to reach `00:05:04` within eight seconds
when asked for five minutes, and to report nothing at all after ten seconds when asked for ten.

## Leaving the list

An entry can be taken off the continue-watching list, because a film that did not hold the viewer's
attention would otherwise sit there for good — and the list's whole value is that everything on it is worth
carrying on with.

Removal is **not** a `WatchOutcome`, and that is the point of it having its own store operation
(`ForgetMovieProgressAsync`). It was expressed as `WatchOutcome.Discard` at first, which fits mechanically —
the position goes and nothing is marked watched — but every outcome also states a moment, so removing an entry
stamped `LastWatchedUtc` with the moment of removal and the film came back as the most recently watched thing
in the catalogue. Forgetting where you got to is not watching.

So it clears the position, leaves `IsWatched` alone — nobody watched it, and a film labelled "Watched" that
nobody has seen is a worse lie than a stale resume point — and records no instant at all. Nothing is confirmed
first: it forgets a position, not the film, and starting it again is one click away.

Two details the shell has to get right. It stops *following* the item first, or stopping playback afterwards
would write the position straight back; and it refreshes all three places a position is displayed — the film
row, the episode row and the list — because forgetting it in one and leaving it in the others reads as the
removal not having worked.

## What a refresh may and may not overwrite

One rule runs through every write in `LtrDbContext.Vod`:

> A listing may overwrite what a listing owns, and must never blank out what a detail call supplied.

Panels state a synopsis in the detail response and not in the listing, so assigning the listing's fields
unconditionally would erase every synopsis the player had fetched. `Plot`, `Genre`, `Cast`, `Director`,
`Year`, `Rating`, `DurationSeconds` and `ContainerExtension` are therefore assigned only when the incoming
value is present.

The viewer's own data — `ResumePositionSeconds`, `LastWatchedUtc`, `IsWatched` — is never touched by a
refresh at all, exactly as `Channel.IsFavorite` is not. An entry the provider has withdrawn is removed and
takes its position with it, which is the same trade the live catalogue makes with favourites.

Episodes are matched **by identifier across the whole series** rather than within one season, so an episode
the provider refiles into another season keeps the position the viewer reached in it instead of coming back
unwatched.

## Categories are numbered per section

A panel numbers its category identifiers per section, so `58` is a live category and a film category at the
same time. Two consequences:

- The unique index is on `(SourceId, ExternalId, Kind)`, and the reconciliation's lookup is keyed by kind as
  well — a dictionary keyed by identifier alone throws on the duplicate.
- Category reconciliation is scoped to the kinds an import covers. Scoped to the source, as it was while
  live was the only kind, a live refresh would delete every film category.

## What M4 deliberately leaves out

- **Seeking within a film.** Resuming works; a seek bar belongs with the OSD in M5.
- **Favourites for films and series.** The marker exists on channels only. `CatalogueFilter` carries the
  criterion, so adding it is a column and a command rather than a redesign.
- **Trailers, cast pictures, backdrops.** Panels serve them; nothing displays them.
- **A poster grid.** The sections are lists with thumbnails. A grid wants the width the video has.
