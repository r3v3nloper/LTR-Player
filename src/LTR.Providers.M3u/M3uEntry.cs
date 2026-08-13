namespace LTR.Providers.M3u;

/// <summary>
/// One channel as declared by an M3U-Plus playlist.
/// </summary>
/// <param name="DisplayName">The text after the comma on the <c>#EXTINF</c> line.</param>
/// <param name="Url">Address the entry plays from.</param>
/// <param name="TvgId">
/// Guide identifier from <c>tvg-id</c>, used to join XMLTV programme data. Absent far more often
/// than present.
/// </param>
/// <param name="TvgName">
/// Guide name from <c>tvg-name</c>. Frequently differs from the display name, and is the better
/// candidate when matching against guide data by name.
/// </param>
/// <param name="LogoUrl">Logo from <c>tvg-logo</c>.</param>
/// <param name="GroupTitle">
/// Group from <c>group-title</c>, or from a following <c>#EXTGRP</c> line. Becomes the category.
/// </param>
/// <param name="ChannelNumber">Channel number from <c>tvg-chno</c>, when the playlist supplies one.</param>
public sealed record M3uEntry(
    string DisplayName,
    Uri Url,
    string? TvgId,
    string? TvgName,
    string? LogoUrl,
    string? GroupTitle,
    int? ChannelNumber);
