# Refactoring backlog

Reviewed after M3. Ranks 1–8 were cleared then, and **ranks 10 and 13 were cleared by M4** — not as tidying
but because they blocked it: the shell view model could not take three more sections at 403 lines, and
`MainWindow.xaml` could not take three more lists at 470.

What follows is what remains, in the order it was prioritised — most valuable per unit of effort first.

Ranking rule: criticality against effort. Renumbered at the M3 review, so a rank quoted in an older commit
message will not line up. Ranks are **not** renumbered again here, so that the numbers in M4's commits still
mean something.

**Rank 9 is the only item here with an effect while the player is running.** Everything else is structure,
a missing guard, or a limit that is stated on screen. Deferred deliberately after the M3 review rather than
overlooked: it wants measuring first.

M4 added ranks 19 and 20 at the end. Rank 17 got worse and is restated where it stands.

---

## Ranks 1–8 · done

Cleared in one pass after M3. What each settled, and what is worth knowing before touching it again:

- **1 · One test harness for the shell.** `MainViewModelTests` and `GuideViewModelTests` each had their own
  `TestContextBuilder` assembling the same eight-argument view model. `MainViewModelHarness` replaces both,
  and owns the fixed instant the fake clock stands at.
- **2 · A probe result is persisted on every import.** `UpdateCapabilitiesAsync` existed, was tested, and was
  called by nothing: a refresh probed the panel and discarded the answer. It is now
  `UpdateProbeResultAsync`, takes the probed source whole so that deciding which fields a probe owns stays
  in the persistence layer, and also adopts the guide address an M3U playlist declares — which is what stops
  the guide import re-downloading several megabytes of playlist to rediscover it.
- **3 · `ConsoleText`.** `Truncate` had reached three copies and timestamp formatting two. `sources list` now
  labels its timestamps UTC, which they always were.
- **4 · Guide pruning is scoped to its source.** It deleted across every source from one source's import.
  Same effect, wrong reach.
- **5 · One `TestClock`,** linked into the test projects that need one from `src/TestSupport`.
- **6 · A locked companion no longer undoes a quarantine.** The database is already moved by then, so the
  free path the caller needs exists; throwing turned a recoverable startup back into a failed one.
- **7 · The filter reads the row, not the entity.** `ChannelItemViewModel` no longer mirrors its favourite
  flag into the `Channel` it wraps. `ChannelFilter` gained an overload over the three values it looks at,
  which is also what a server-side filter would want.
- **8 · A shell lifetime token.** Cancelled when the window closes and linked into the catalogue load and the
  guide import, so closing mid-load does not wait for seventeen thousand channels.

Two things came out of doing them, both worth remembering:

- Making the catalogue load cancellable **made an unobserved task exception reachable**. Source management
  starts that load without awaiting it, so `ShowCatalogueAsync` now swallows cancellation as well —
  the comment that used to reason "nothing here cancels" was correct until rank 8 and is no longer.
- `Progress<T>` delivers through a synchronisation context, so in a test with none a stage message can land
  *after* the result message an assertion is reading. The guide-import fake reports progress only when asked.

---

## Rank 9 — Now-and-next transfers far more than it shows

**Project:** LTR.Persistence, LTR.Player.Wpf · **Area:** Performance · **Criticality:** moderate · **Effort:** medium

`GetNowAndNextAsync` returns whole `EpgEntry` rows — `Description` included, up to 4,000 characters — for
every matched channel. Against a real subscription that is roughly 9,000 rows **every minute**, to show two
titles per row. The timer also runs when nothing is matched at all.

Proposal: project onto a narrow read model (title, start, stop) and skip the refresh entirely while
`HasGuide` is false. Unmeasured; derived from the figures a 17,000-channel subscription produces.

---

## Rank 10 — MainViewModel does four things · done in M4

Lifted into `GuideImportCoordinator`: starting an import, refusing a second, wording every outcome, and
draining it on shutdown. What happens *after* a successful import stayed with the shell, as a continuation
it supplies — reloading the channel list and the timeline needs both of those, and reaching for them from
the coordinator would have put it back in the business of knowing the whole window.

The shell lifetime token did **not** move with it. It is passed in, because the catalogue load and the film
detail fetches are linked into the same token and it belongs to whoever owns the window's lifetime.

---

## Rank 11 — Protocol-neutral URL sanitisation

**Project:** LTR.Providers.* · **Area:** Security · **Criticality:** moderate · **Effort:** medium

`UrlSanitizer` is internal to `LTR.Providers.Xtream` and only understands `XtreamSource`. M3U playlist and
guide URLs also carry credentials in their query string and have no sanitiser. Nothing logs one today, so
this is a missing guard rather than an active leak.

Proposal: `ISensitiveUrlSanitizer` in `LTR.Providers.Abstractions`, one implementation per protocol,
resolved through `IProviderRegistry`.

---

## Rank 12 — Stream the Xtream response instead of buffering it

**Project:** LTR.Providers.Xtream · **Area:** Performance · **Criticality:** moderate · **Effort:** medium

`XtreamApiClient.GetStringAsync` reads the whole response into a `string` and then parses a `JsonDocument`
from it — two multi-megabyte copies for a 17,000-channel catalogue.

Proposal: `JsonDocument.ParseAsync` over the content stream. The HTML-detection guard has to move to the
first bytes of the stream, which is the only fiddly part: panels answer with an HTML error page at HTTP 200
and that must still be recognised.

---

## Rank 13 — Split MainWindow.xaml · done in M4

`Views/` now holds `LiveChannelsView`, `MovieCatalogueView`, `SeriesCatalogueView`, `ContinueWatchingView`,
`AddSourceView` and `GuideOverlayView`; the window is composition.

Two things learned in the doing:

- The guide's header, rows and now-marker reference each other by `ElementName`, so they had to stay in one
  file — a XAML namescope is per file, and splitting them further would break the bindings silently.
- The guide overlay is still placed *by the window* inside `VideoView.Content`. Hosting it is the window's
  business, for the reason below; being a separate file changes nothing about that.

The overlay and the timeline must stay inside `VideoView.Content`. `VideoView` hosts a separate native
window over the WPF tree, so a sibling element is invisible behind the video.

---

## Rank 14 — Pin the timeline's channel-name column

**Project:** LTR.Player.Wpf · **Area:** Usability · **Criticality:** moderate · **Effort:** medium

The timeline puts its header and every row inside one horizontal `ScrollViewer`, so the channel names scroll
out of view with the programme blocks. Names staying put is most of what makes an EPG readable.

Doing it properly means two vertically synchronised lists or a custom panel, which is why it was not done
with M3. It is mostly hidden today because the window is moved with the buttons rather than by scrolling.

---

## Rank 15 — Extract the reconciliation diff

**Project:** LTR.Persistence · **Area:** Maintainability · **Criticality:** minor · **Effort:** medium

Roughly a hundred lines inside `LtrDbContext` compute a diff and have no database concern. A
`CatalogueReconciler` could compute it while the context performs the writes, which keeps §3.3.2 intact and
makes the algorithm testable on its own.

---

## Rank 16 — Command classes in the CLI

**Project:** LTR.Cli · **Area:** Maintainability · **Criticality:** minor · **Effort:** medium

`Program.cs` is mostly six `Build*` functions. Proposal: one class per command exposing `Command Build()`,
leaving `Program` as composition only.

---

## Rank 17 — Do not materialise the whole channel list

**Project:** LTR.Persistence, LTR.Player.Wpf · **Area:** Performance · **Criticality:** moderate · **Effort:** high

Every source switch loads all channels and wraps each in a view model. It works at 17,156. Worth revisiting
only when it demonstrably hurts — filtering and paging in the store rather than in memory.

**Restated after M4.** The film section did not inherit this: at 66,447 films the approach was not viable, so
`SearchMoviesAsync` filters and counts in the database and the section shows a bounded page. That is the
shape this rank proposes, now built and in use — so the remaining work is applying it to the channel list,
where the numbers are four times smaller and the guide's now/next join makes it harder. Note the one
behavioural difference to settle first: the film search matches with SQLite's `LIKE`, which is
case-insensitive for ASCII only, where `CatalogueFilter` in memory is fully case-insensitive.

---

## Rank 18 — Page the timeline instead of capping it

**Project:** LTR.Player.Wpf · **Area:** Performance · **Criticality:** minor · **Effort:** high

`GuideViewModel.MaximumRows` draws at most 200 channels and says so on screen. The honest fix is to load rows
as they are scrolled into view, which needs the store to page and the timeline to build rows lazily. Related
to rank 17, and worth doing at the same time or not at all.

---

## Rank 19 — A film's detail is fetched again on every viewing when the panel has none

**Project:** LTR.Catalogue · **Area:** Performance · **Criticality:** minor · **Effort:** low

`VodItem.HasDetail` is set only when a detail response arrives. A panel that answers with nothing therefore
leaves it unset, and selecting that film asks again every time — one `get_vod_info` call per selection, on a
catalogue where selecting films is the normal way to browse.

The current behaviour is deliberate as far as it goes: an empty answer today is not proof of an empty answer
next week, and it costs one metadata call. But nothing distinguishes "never asked" from "asked, and there is
nothing", which is the distinction worth having.

Proposal: record when the detail was last attempted and do not retry within a day. The field exists in
spirit already — `Series` has `DetailFetchedUtc` for the same purpose.

## Rank 20 — Nothing records progress when a film reaches its own end

**Project:** LTR.Player.Wpf · **Area:** Correctness · **Criticality:** minor · **Effort:** low

Progress is written when playback is switched, stopped or the window closes. A film that plays to its end and
sits there is none of those, so it stays on the continue-watching list until the next of them happens —
usually the window closing, which then records it correctly as finished.

So nothing is lost; the list is briefly wrong. Fixing it means recording when the session reports `Stopped`
after having been `Playing`, and the reason it was not done is that the same transition occurs in the middle
of every channel change, where progress has already been recorded a moment earlier. Distinguishing the two
needs the session to say *why* it stopped, which is a change to `IPlaybackSession` and wants doing with M5's
OSD work rather than on its own.
