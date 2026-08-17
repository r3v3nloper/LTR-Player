# The programme guide

What M3 built, and the reasoning that is not obvious from the code.

## The guide is not the channel list

An XMLTV guide is a separate document, usually from a separate party, describing channels by its own
identifiers and its own names. It is therefore modelled as its own thing — `GuideChannel` — rather than as
programmes hanging off a `Channel`, with a nullable `Channel.GuideChannelId` recording which guide channel
a channel was matched to.

That indirection is what makes matching possible at all. On the 17,000-channel subscription this was built
against, **72% of channels carry no `tvg-id`**, so matching by name is the primary path and not a
fallback. A design that joined programmes to channels by identifier would leave three quarters of the list
blank.

```
PlaylistSource ─┬─ Channel ──────────┐
                │                    ├─ GuideChannelId (nullable, written only by the guide import)
                └─ GuideChannel ─┬───┘
                                 └─ EpgEntry (programmes)
```

## Matching rules

`GuideChannelMatcher` tries three things in order of how much each assumes:

1. the guide id the channel states (`epg_channel_id` / `tvg-id`),
2. the name as written,
3. the name with region tags and quality markers stripped — `ChannelNaming.ToGuideMatchKey`.

Two rules matter more than the order:

- **An ambiguous name is left unmatched.** Half a guide attached to the wrong channel is
  indistinguishable from a broken player; a channel with no listing is merely a channel with no listing.
- **`+1` survives normalisation.** A timeshift channel is showing something else, so collapsing `TF1 +1`
  into `TF1` would be wrong for every hour of the day. `HD`, `FHD`, `4K`, `HEVC` and a leading `FR: ` are
  all discarded; `+` is not.

`ChannelNaming` therefore has two normalisers pulling in opposite directions, and they are not
interchangeable: `ToIdentityKey` must keep every distinction the provider makes so two channels never
collapse into one, and `ToGuideMatchKey` must discard the cosmetic ones so two spellings of one channel
meet.

## Why the import is a stream and a decorator

A guide is 50–200 MB. `XmltvStreamReader` walks it with `XmlReader` and pushes each element to an
`IXmltvSink`; `GuideProgrammeWriter` batches two thousand programmes at a time into the database and
forgets them. Cost is flat in guide size.

Two things sit between reader and writer, both because XMLTV permits what a query cannot handle:

- **`XmltvStopTimeFiller`** gives every programme an end time. `stop` is optional in the format and plenty
  of guides omit it, on the understanding that a programme runs until the next one starts. Applying that
  once during import is what keeps "or until the next programme" out of every now-and-next query. A gap of
  more than six hours is treated as absence of information rather than as a six-hour programme.
- **`XmltvStreamOpener`** decides from the first two bytes whether the download is gzipped. The address
  does not reliably say: `xmltv.php` serves gzip from some panels and plain XML from others, and an
  `.xml.gz` URL is sometimes decompressed by an intermediary.

A reimport replaces **one guide channel at a time** — the first batch touching a channel deletes what that
channel held, inside the same transaction as the insert. At no point is there a player with no guide in it,
which a truncate followed by a slow reinsert would produce.

Programmes that ended more than 6 hours ago and those starting beyond 21 days out are discarded on the way
in, and pruned afterwards. Guides carry days of history no view here shows, and without pruning the table
grows on every import and never shrinks.

## When it runs

- **The download button** fetches unconditionally.
- **Adding or refreshing a source** starts a background import, but only if the stored guide is missing or
  older than `IGuideImportService.StaleAfter` (12 hours). Merely *selecting* a configured source fetches
  nothing — picking from a list is not an invitation to download a hundred megabytes.

The import is not awaited by the command that triggers it, so the window stays usable, including for
playback. `MainViewModel.GuideImportCompletion` exposes the task, because a background task nothing can
observe is also one nothing can shut down.

## Traps met on the way

- **SQLite cannot compare a `DateTimeOffset`.** EF's default mapping writes it as text with the offset
  appended, which sorts wrongly across offsets — so the provider refuses to translate any comparison or
  `Max` over such a column, and every guide query is a comparison over time. `LtrDbContext` converts the
  guide's instants to UTC `DateTime` on the way in. Without that, the whole guide is filtered in memory
  and nothing says so; the persistence tests caught it only because they run on real SQLite.
- **Now-and-next is one query for the whole source**, translated to a single
  `ROW_NUMBER() OVER (PARTITION BY GuideChannelId)`. Asking per row would be thousands of queries driven
  by a scroll bar.
- **A timeline is a grid, not a list.** 17,000 channels over four hours is several hundred thousand
  elements, so the timeline draws **one page of 200 channels** and states which page it is on. Both axes are
  moved by command: 30 minutes at a time along the time axis, 200 channels at a time along the other. Neither
  is a scrollbar, because each move is a fetch and a button says so where a scrollbar would hide it.
- **The channel-name column stays pinned while the blocks scroll.** One scroller around the header alone owns
  the horizontal offset; each row's blocks and the now-marker follow it through a translation, so a heading
  cannot drift off its own blocks. `GuideOverlayViewTests` measures exactly that — it is the only test in the
  repository that builds a visual tree, and it exists because nothing else can state a layout property.
- **`Channel.GuideChannelId` is written only by the guide import.** A catalogue refresh must leave it
  alone, for the same reason it leaves the favourite flag alone: it is not something the provider states.
