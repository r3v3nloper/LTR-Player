# Release notes

One entry per tag, newest first. What a release *is* rather than what changed in it: the tag message carries the
same text, so `git show v0.8.1` needs no other file open.

Every release is a self-contained folder and a zip — see `Docs/packaging.md`. There is no installer: unpacking is
the installation, deleting the folder is the uninstall, and the player writes only to
`%LOCALAPPDATA%\LTR-Player`. The builds are unsigned, so Windows will warn on first run.

## 0.8.1

**A bug fix, and the review it prompted.** Upgrade by unpacking over the old folder — nothing about the
catalogue's schema changed, so the stored channels, films, guide and resume positions are kept.

### Fixed

- **Previous and next now follow what is playing.** Starting an episode — from Continue or from the series list
  — and pressing ⏭ switched the left pane to Live and tuned a live channel. The two buttons, and Page Up and
  Page Down with them, were wired straight to channel zapping regardless of what was on screen.

  They now mean the next thing of the kind that is playing:

  | Playing | Previous / next |
  |---|---|
  | A live channel, or nothing yet | the previous / next channel, as before |
  | An episode | the previous / next episode of that series, across the season boundary |
  | A film | unavailable — a film has no neighbour, so the buttons grey out |

  The end of a series says so and changes nothing rather than wrapping round to the first episode. Episodes are
  found in the stored catalogue rather than in the list on screen, so this works for an episode resumed from
  Continue, where no series is open — which is the case it was reported from.

- **An episode's on-screen name now leads with its series.** Resuming from Continue previously showed
  `S01E02 · Cat in the Bag` with no indication of which series it belonged to.

### Not visible, and the larger half of the release

A review over the whole tree, recorded in `Docs/refactoring-backlog.md`; nine of its ten items are in this
release. Nothing here changes behaviour, and the test count is the evidence: 739 tests before, 781 after, with
the additions all *new* coverage rather than adjusted assertions.

Two are worth naming because they were about the checks themselves rather than the code:

- The CLI had no test project at all, so the front end that exists *for diagnosing a subscription* had no guard
  on its failure wording — a reason added later would have read as "the panel gave no usable answer". And the
  rule deciding whether a subscription's credentials are printed to the console could not be tested. Both are
  held now, and both were mutation-checked rather than assumed.
- Choosing a category was untested in the channel list *and* in the film section. The channel list's existing
  test passed for the wrong reason. Found by deleting the wiring and watching every test still pass.

### Known, and unchanged

- `LiveNetworkCachingMilliseconds` still defaults to 600 ms and is still a guess rather than a measurement.
  Raise it in the settings pane if channels stutter in their first seconds; it takes effect on restart.
- A playlist held as a local file that declares no guide address has no credentials on record, so an address
  printed for it cannot be masked. The CLI says so rather than claiming a masking it did not perform.

## 0.8.0

Pinned categories, and on-screen controls that the pointer can wake again — including in fullscreen, where
they previously could not be reached at all. First public release.
