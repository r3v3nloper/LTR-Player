// Aliased because the unqualified name Core resolves to the LTR.Core namespace here.
using VlcCore = LibVLCSharp.Shared.Core;

namespace LTR.Playback.LibVlc;

/// <summary>
/// Loads the native LibVLC libraries, exactly once per process.
/// </summary>
/// <remarks>
/// <c>Core.Initialize</c> resolves and loads native modules and must not run twice. Guarding it here
/// means callers need not care whether an engine has been constructed before.
/// </remarks>
internal static class LibVlcRuntime
{
    private static readonly Lock InitializationLock = new();
    private static bool _isInitialized;

    public static void EnsureInitialized(string? nativeLibraryDirectory)
    {
        if (_isInitialized)
        {
            return;
        }

        lock (InitializationLock)
        {
            if (_isInitialized)
            {
                return;
            }

            // A null path makes LibVLCSharp search next to the executable, which is where the
            // VideoLAN.LibVLC.Windows package places libvlc.dll and its plugins folder.
            VlcCore.Initialize(nativeLibraryDirectory!);
            _isInitialized = true;
        }
    }
}
