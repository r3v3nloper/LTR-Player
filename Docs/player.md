# The player controls

What M5 built, and the reasoning that is not obvious from the code.

## Where the transport lives, and why not somewhere else

Three places could plausibly own pausing, seeking, volume, tracks and aspect ratio. The one chosen is the
playback session, and the other two were rejected for the same reason.

*(Since the review after M6, "the session" is two interfaces over one class: `IPlaybackSession` for opening and
releasing, `IPlaybackTransport` for everything below. The reasoning here is unchanged — the split only makes
the last paragraph of this section true by construction instead of by discipline.)*

**Not the engine.** `IMediaEngine` implements all of it, but an overlay that held the engine to read a
position would be a second holder of the object whose single ownership is the whole playback design. The
constraint is not hypothetical: a subscription permits one concurrent connection, and the ordering rule that
keeps within it — stop fully, then start — only holds while one thing is doing the starting. A second holder
starts as "just one call" and ends as a bypass.

**Not `PlaybackCoordinator`.** It would have worked, and would have made the coordinator a facade duplicating
the session's API on top of the two things it actually does. The division that reads cleanly instead:

```
PlaybackCoordinator     decides WHAT plays — builds the address, opens the stream, words the failure,
                        follows the position, writes it down. Sole caller of SwitchToAsync.
PlayerOverlayViewModel  acts on a stream ALREADY OPEN — pause, seek, volume, tracks, aspect, fullscreen.
                        Holds IPlaybackTransport only, so it cannot open or release one.
IPlaybackSession        opening and releasing. The coordinator is the only thing that holds it.
IPlaybackTransport      everything doable to a stream already open. One class implements both.
```

Opening stays privileged. Everything the overlay can do is something a viewer can do to a stream that is
already costing them their connection, so none of it can leak one.

## Nothing in the overlay listens to the engine

This is the constraint that shaped the whole class, and it is a WPF fact rather than a design preference.

An engine raises its state events on its own internal threads. WPF marshals a `PropertyChanged` for a plain
binding to the dispatcher on its own — which is why `PlaybackCoordinator` can set a status line from an engine
callback — but it does **not** do that for a collection. A track list rebuilt from an engine thread throws
from the binding engine and takes the window down.

So `PlayerOverlayViewModel` subscribes to nothing. Everything is read in `Sample()`, which the window calls
from a `DispatcherTimer`:

```
Sample()  →  state, position, duration, seekability
          →  push volume and mute at a stream that has just started
          →  sync the track menus
          →  hide the controls if nothing has happened for four seconds
```

`PlaybackCoordinator` does subscribe, and is careful about it: an end-of-stream report sets an `int` flag and
nothing else, because what has to follow — a database write, then three lists rereading themselves — belongs
on the window's thread. `SampleAsync` picks the flag up on the next tick.

## One timer, two rates

`MainWindow` had a five-second timer for the resume recorder. The controls want a position several times a
second while they are visible, so the interval changes with `PlayerOverlayViewModel.IsVisible` — 500 ms up,
five seconds down.

A second timer would have been the obvious move and the wrong one: both jobs read the same figures from the
same place, so two timers means sampling twice as often for no additional information. The slow rate is not a
fallback either; it exists because a resume position has to stay current whether or not anything is on screen.

## The controls wake on the pointer, and the pointer is not the shell's

The bar takes itself away after four seconds and something has to bring it back. That something is the
pointer moving anywhere over the picture — the same thing every player does, rather than a strip at the
bottom that has to be found.

Where it is taken from is the part worth writing down, and it took two attempts because the first answer was
only half of it.

**The picture has to be painted to be touched at all.** `VideoView` draws this content in a *layered* window
of its own over the native video surface, and Windows hit-tests a layered window by its alpha. A fully
transparent pixel — which is exactly what `Background="Transparent"` (`#00FFFFFF`) is — passes the pointer
through to whatever is underneath, which here is the video. That is why the buttons answered a click while
the picture beside them answered nothing: the bar is drawn opaque and the surface around it was not drawn at
all. `PointerCatchBrush` is one part in 255 of black, invisible over a picture and enough to be hit. The
behaviour is measurable rather than folklore: over a fully transparent layered window `WindowFromPoint`
returns the window below it, over this one it returns the overlay's own.

**And the move is taken from the window rather than from the control.** Even hit correctly, this content is
not in the shell's window, so the shell's `PreviewMouseMove` never sees it. The controls attach to **the
window they are hosted in**, found on `Loaded` and let go on `Unloaded` because that window is created and
replaced by `VideoView` rather than by anything here.

Two details follow from that:

- **The move is taken as the tunnelling event, the double-click as the bubbling one.** The window sees every
  move regardless of what lies between, including a slider that marks the moves it consumes as handled. A
  double-click has to be the other way round: buttons and pickers mark their own clicks handled, which is
  what keeps a click aimed at a control from also going fullscreen.
- **A pointer resting on the controls keeps them up.** A pointer that has stopped moving raises nothing
  further, so the idle timer would otherwise take the bar out from under a hand on its way to a button —
  and the click that follows lands on the picture, which goes fullscreen. `IsPointerOnControls` is set from
  the bar's own `MouseEnter`/`MouseLeave`, and leaving it restarts the countdown rather than resuming one
  that had already run down.

The cost of getting this wrong was worst in fullscreen, where there is no side panel to fall back on: the
controls could not be reached at all short of leaving fullscreen with Escape. `PlayerOverlayViewTests` builds
the real controls in a window and moves the pointer over it, because nothing else states which window the
wake-up comes from.

## The seek bar fights the timer, and wins while held

A bound slider whose value is written twice a second cannot be aimed — the thumb moves out from under the
pointer. The view reports the drag (`Thumb.DragStarted`/`DragCompleted` are the only part of this that has to
be in code-behind), and while `IsScrubbing` is set the timer leaves the position alone.

Letting go seeks, unless the bar moved less than two seconds. That tolerance is what stops an idle click on
the bar from making a film re-buffer over HTTP for no reason.

Live television has no seek bar at all, rather than a disabled one. A greyed-out scrubber invites the question
of why it does not work; a channel simply has no position to move to. `IsSeekable` comes from the engine, so
the answer is the stream's rather than a guess from the content kind.

## Two ways to reach a position, and they are not the same call

```
MediaRequest.StartAt        honoured while the stream is opening.  Resuming.
IPlaybackTransport.SeekTo   issued against a stream already playing.  The seek bar.
```

They stay separate because they are honoured at different moments and only the first can be relied upon to
land before the first frame. `LibVlcMediaEngine.ApplyStartPosition` records the measured reason the resume is
not simply a `SeekTo` after opening — handed LibVLC's `start-time` option the Matroska demuxer prerolls the
whole file, and against a remote film that never arrives.

`--seek-to` on `vod play-test` is the only way to check the second one without the window. It holds, seeks,
holds again and reports the position, because a seek over HTTP is answered by a fresh range request and the
engine reports the old position until that arrives.

## Track menus arrive late and are replaced wholesale

MPEG-TS announces its tracks as it encounters them, so a menu is empty at the moment a channel starts, grows
over the next seconds, and is thrown away entirely on a channel change. `TrackSelectionViewModel` handles all
three, and two details in it are load-bearing:

- **It compares identifiers before rebuilding.** `Sync` runs several times a second while the controls are up,
  and a rebuild closes an open drop-down. Same ids, no rebuild.
- **It asks the engine what is playing rather than assuming its own first entry.** A menu that reported entry
  one as selected would tell the engine to switch to it — overriding the default the stream itself declared,
  which is usually right. That is what `GetSelectedTrack` is for, and the suppression flag around adopting its
  answer is what stops the adoption being echoed straight back.

Subtitles get an off entry, audio does not: switching sound off is what mute is for. A menu holding one entry
is hidden rather than shown disabled, so a channel with one audio track and no subtitles shows neither picker.

LibVLC reports a track's name and no language. For these streams the name is whatever the muxer wrote, which
is usually the language already; where it wrote nothing, `MediaTrack.DisplayLabel` falls back to `Track N`.

## Keys are not input bindings

`KeyBinding` entries in the window's markup would have been the declarative answer, and they are unusable
here. An input binding is offered the keystroke before the focused element sees it, and the shortcuts are
single unmodified keys — several of them letters. Declaring one for `A` means the channel search box can never
contain the letter `a`.

So `MainWindow.OnPreviewKeyDown` checks what has focus first, and that check is the reason this cannot be
declarative. Neither half of the decision is in the handler: `PlayerKeyMap` says which key means what, and
`PlayerActions` says what each action does. Both are testable without a window, which is the other reason for
the split.

`PlayerActions` splits again along the line this document opened with. Four actions — stop, the two zaps and
the guide — come back to the shell as delegates, because they decide *what* plays or what the window shows.
Every other action works on a stream already open, so it goes to the overlay.

| Key | Action |
|---|---|
| Space | pause or resume |
| Page Up / Page Down | previous / next channel |
| `+` / `−` | volume |
| `M` | mute |
| Left / Right | back / forward ten seconds |
| `F`, F11, double-click | fullscreen |
| Escape | leave fullscreen |
| `G` | programme guide |
| `I` | show the controls |
| `A` | cycle the aspect ratio |

**The arrow keys are deliberately not zapping.** They are how a person looks down a list of seventeen
thousand channels, and taking them would open every channel on the way past. Page Up and Page Down are taken
from the list's own paging, which is a smaller loss — the list still has arrows, Home, End and a scrollbar.

Zapping stops at the ends of the list rather than wrapping. A wrap is indistinguishable from a zap that did
nothing except by watching the picture, and an unwanted one costs a stream open, which costs the
subscription's one connection.

## Fullscreen is stated by the view model and applied by the window

`PlayerOverlayViewModel.IsFullscreen` is the flag; `MainWindow` reacts to it, because window chrome is not
something a binding can express. Two details there shipped from experience elsewhere:

- The trip through `WindowState.Normal` before maximising is required. A window that is already maximised does
  not re-maximise when its chrome is removed, and sits above the taskbar with a strip of desktop showing.
- The side panel's column width is remembered rather than reset. Collapsing the panel is not enough — a
  collapsed element keeps its column — and a converter returning a fixed pair of widths would throw away
  whatever the viewer had dragged the splitter to.

## Zapping latency: what can and cannot be shortened

A channel change is three things, and only one of them is tunable.

1. **Releasing the previous stream** and waiting for the provider to notice. Required by the connection limit.
   Not negotiable, and shortening it is the leak the whole design exists to prevent.
2. **The panel answering** — DNS, connect, redirect to a streaming node. Not ours.
3. **Filling the buffer before the first frame.** `LiveNetworkCachingMilliseconds`, applied as a per-media
   option so films keep their own longer figure.

The third defaults to 600 ms against the 1000 ms films get. **That number is a starting point, not a
measurement** — the right value is a property of the provider, and a panel that delivers in bursts needs more.
Raise it if channels stutter in their first seconds; that symptom is this value being too low.

What is new and useful regardless is that the figure is now observable. `PlaybackSession` times the open and
logs it:

```
Erste began playing 780 ms after the open was issued.
```

The stop is deliberately outside that figure, so the line measures the part that can be changed. Tuning the
caching value without it would be guessing.
