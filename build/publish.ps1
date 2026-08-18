<#
.SYNOPSIS
    Publishes the player as a self-contained folder and zips it.

.DESCRIPTION
    Everything the milestone's packaging step amounts to. The output runs on a machine with no .NET
    installed, which is what self-containment buys; the size is what LibVLC's native binaries cost, and it
    is reported rather than left as a surprise.

    Not an installer. There is nothing to register, nothing to put in the registry and no service to start —
    the player writes only to %LOCALAPPDATA%\LTR-Player, so unpacking the zip is the installation and
    deleting the folder is the uninstall.

.PARAMETER Output
    Where to put the zip. Defaults to artifacts\ beside the solution.

.PARAMETER SkipTests
    Publish without running the tests first. For a local trial run, not for anything shipped.
#>
[CmdletBinding()]
param(
    [string] $Output,
    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repository = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repository 'LTR-Player.slnx'
$project = Join-Path $repository 'src\LTR.Player.Wpf\LTR.Player.Wpf.csproj'
$publishDirectory = Join-Path $repository 'artifacts\publish'

if (-not $Output) {
    $Output = Join-Path $repository 'artifacts'
}

# Refused rather than worked around: MSBuild cannot replace a locked DLL, and the error it gives for one
# arrives after a successful compile, which reads as a broken build rather than a running application.
if (Get-Process -Name 'LTR-Player', 'LTR.Player.Wpf' -ErrorAction SilentlyContinue) {
    throw 'The player is running. Close it first — its files cannot be replaced while it holds them.'
}

if (-not $SkipTests) {
    Write-Host 'Running the tests...' -ForegroundColor Cyan
    dotnet test $solution --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed; nothing was published.' }
}

# Emptied first, so a file dropped by an earlier build cannot travel in the zip.
if (Test-Path $publishDirectory) {
    Remove-Item $publishDirectory -Recurse -Force
}

Write-Host 'Publishing...' -ForegroundColor Cyan
dotnet publish $project -p:PublishProfile=win-x64 --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

$executable = Join-Path $publishDirectory 'LTR-Player.exe'
if (-not (Test-Path $executable)) { throw "Published, but $executable is not there." }

# The two things worth checking before shipping, and both are silent when wrong: a publish with no natives
# starts perfectly well and plays nothing. This check has already earned its keep once — the profile said
# 'Any CPU' where the package tests for 'AnyCPU', and the whole of LibVLC was simply absent.
#
# libvlc\win-x64 rather than beside the executable: that is where the package puts them and where
# LibVLCSharp's own probing looks.
$natives = Join-Path $publishDirectory 'libvlc\win-x64\libvlc.dll'
$plugins = Join-Path $publishDirectory 'libvlc\win-x64\plugins'

if (-not (Test-Path $natives)) { throw 'libvlc.dll is missing; the build would start and play nothing.' }
if (-not (Test-Path $plugins)) { throw 'The LibVLC plugins directory is missing.' }

$notices = Join-Path $publishDirectory 'THIRD-PARTY-NOTICES.txt'
if (-not (Test-Path $notices)) {
    throw 'THIRD-PARTY-NOTICES.txt is missing; LibVLC is LGPL and its notice has to ship with it.'
}

# Checked for the same reason as the notice above: that notice states the MIT terms are in this file, and a
# claim about a file nobody received is worse than no claim.
$licence = Join-Path $publishDirectory 'LICENSE'
if (-not (Test-Path $licence)) {
    throw 'LICENSE is missing; THIRD-PARTY-NOTICES.txt points at it for the application''s own terms.'
}

foreach ($architecture in 'win-x86', 'win-arm64') {
    $unwanted = Join-Path $publishDirectory "libvlc\$architecture"
    if (Test-Path $unwanted) {
        throw "$architecture natives were published; this build cannot load them and they double the size."
    }
}

# XPath rather than property access: Directory.Build.props has several PropertyGroup elements, and reaching
# through the collection for a property only one of them has fails under Set-StrictMode.
$properties = [xml](Get-Content (Join-Path $repository 'Directory.Build.props'))
$version = $properties.SelectSingleNode('/Project/PropertyGroup/Version').InnerText

if (-not $version) { throw 'No Version in Directory.Build.props; the zip would be named for nothing.' }

New-Item -ItemType Directory -Force -Path $Output | Out-Null
$zip = Join-Path $Output "LTR-Player-$version-win-x64.zip"

if (Test-Path $zip) { Remove-Item $zip -Force }

Write-Host 'Compressing...' -ForegroundColor Cyan
Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $zip

$folderSize = (Get-ChildItem $publishDirectory -Recurse -File | Measure-Object -Sum Length).Sum / 1MB
$zipSize = (Get-Item $zip).Length / 1MB

Write-Host ''
Write-Host "Version   $version"
Write-Host ("Folder    {0:N0} MB  {1}" -f $folderSize, $publishDirectory)
Write-Host ("Zip       {0:N0} MB  {1}" -f $zipSize, $zip)
Write-Host ''
Write-Host 'Unsigned, so Windows will warn on first run. Docs/packaging.md says what to do about that.'
