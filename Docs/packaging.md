# Shipping the player

What M6's packaging step produces, and the traps that make it worth a document.

## The command

```bash
pwsh build/publish.ps1
```

Runs the tests, publishes, checks the result and zips it. `-SkipTests` skips the first part for a local trial
run and should not be used for anything given to anyone. Output:

```
artifacts/publish/                        the folder that runs
artifacts/LTR-Player-0.6.0-win-x64.zip    110 MB
```

The version comes from `Directory.Build.props` and nothing else states it. Bump it there.

## What it is, and what it is not

**Self-contained, so no .NET is needed on the target machine.** For a single-user desktop tool that is the
right trade: 252 MB unpacked against a prerequisite the user would otherwise have to install and keep current.

**A folder, not an installer.** There is nothing to register, no service and no registry key — the player
writes only to `%LOCALAPPDATA%\LTR-Player`. Unpacking the zip is the installation and deleting the folder is
the uninstall. What stays behind is deliberate: the catalogue database, `settings.json` and the logs, none of
which anybody wants removed by an upgrade.

**Not single-file.** LibVLC's natives are loaded by name from a directory tree, so bundling them into one
executable only means extracting them again on every start.

**Not trimmed, and it must stay that way.** WPF resolves bindings by reflection and LibVLCSharp marshals into
native code, so a trimmer removes what neither appears to use — and the failure is at runtime, in a window
that comes up looking fine and doing nothing.

## The trap that cost this milestone an hour

`VideoLAN.LibVLC.Windows` decides which set of natives to copy by comparing `$(Platform)` against `x64`,
`x86`, `ARM64` and **`AnyCPU`**. The publish profile originally said `Any CPU` — the spelling a solution file
uses — which matches none of them.

The result was a publish containing every managed assembly, no LibVLC at all, and no warning whatsoever. It
starts, opens its window, loads the catalogue and plays nothing.

So `build/publish.ps1` checks for `libvlc\win-x64\libvlc.dll` and its `plugins` tree by name and refuses to
zip without them. That check is the only reason the mistake was caught rather than shipped.

Two things follow from it:

- The natives live in **`libvlc\win-x64\`**, not beside the executable. That is where the package puts them
  and where LibVLCSharp's own probing looks, which is why `LibVlcRuntime.EnsureInitialized` passes no path.
- The profile ships **one architecture**. The package copies x86 and arm64 as well unless told not to, which
  is roughly a hundred megabytes this build cannot load. The script fails if either appears.

## Verifying a publish

Running the folder is not enough on its own — run it **with a working directory somewhere else**:

```powershell
Start-Process artifacts\publish\LTR-Player.exe -WorkingDirectory C:\
```

That is what distinguishes natives resolved relative to the application from natives found by accident because
the shell happened to start in the right folder. The log at
`%LOCALAPPDATA%\LTR-Player\logs` should show the database path and the source counts, and playing a channel is
the actual proof that LibVLC loaded.

## Signing, and what the user sees without it

The build is unsigned, so SmartScreen warns on first run and the publisher shows as unknown. Nothing in this
document changes that; a certificate is a purchase and a decision, not a build step. Until then, the honest
answer to "Windows says this is dangerous" is that it is unsigned, and the zip's checksum is the only thing
that distinguishes a real copy from someone else's.

## LibVLC and the LGPL

LibVLC is LGPL-2.1-or-later. It is used **unmodified and dynamically linked**, which is what keeps its terms
off this application's own code — the `Copyright` in `Directory.Build.props` covers only what is written here.

Two obligations come with shipping it, and neither is met by accident:

- **The libraries stay replaceable.** They are separate files under `libvlc\win-x64\`, which a user could
  swap for their own build of the same version. Bundling them into a single executable would end that, which
  is a second reason `PublishSingleFile` stays off.
- **The notice ships with them.** `THIRD-PARTY-NOTICES.txt` sits beside the executable and names the licence,
  the upstream source and where to get it. The NuGet package carries no licence file of its own, so this file
  is written here rather than copied from anywhere — and `publish.ps1` refuses to zip without it, because a
  notice that quietly stopped being copied is exactly the kind of omission nobody notices.
