using LibVLCSharp.Shared;

namespace LTR.Playback.LibVlc;

/// <summary>
/// Exposes the underlying LibVLC player so a view can attach itself as the video surface.
/// </summary>
/// <remarks>
/// <para>
/// This is the one deliberate leak of the media engine's implementation. LibVLCSharp's
/// <c>VideoView</c> binds to a concrete <see cref="MediaPlayer"/>, and no abstraction can hide that
/// without either reimplementing video output or pretending the engine is swappable when it is not.
/// </para>
/// <para>
/// It is kept out of LTR.Playback.Abstractions on purpose: the engine-neutral contract there stays
/// free of LibVLC, and only the WPF view — the single place that needs a window handle — consumes
/// this interface.
/// </para>
/// </remarks>
public interface IVlcVideoSink
{
    MediaPlayer MediaPlayer { get; }
}
