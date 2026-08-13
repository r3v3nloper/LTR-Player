using System.Globalization;

namespace LTR.Providers.M3u;

/// <summary>
/// Reads M3U-Plus playlists.
/// </summary>
/// <remarks>
/// <para>
/// M3U-Plus has no specification. It is a convention that grew around VLC's extended M3U, and every
/// provider bends it slightly, so this parser is written to keep going rather than to be strict: an
/// entry it cannot use is counted and skipped, never thrown over. A single malformed line must not
/// cost the user a playlist of twenty thousand channels.
/// </para>
/// <para>
/// The file is read line by line so an arbitrarily large playlist never lands in memory as text. The
/// resulting entries are materialised, which for even the largest real subscriptions is a few
/// megabytes of small records.
/// </para>
/// </remarks>
public static class M3uPlusParser
{
    private const string HeaderPrefix = "#EXTM3U";
    private const string EntryPrefix = "#EXTINF:";
    private const string GroupPrefix = "#EXTGRP:";
    private const string EpgUrlAttribute = "x-tvg-url";
    private const char ByteOrderMark = '﻿';

    public static async Task<M3uPlaylist> ParseAsync(TextReader reader, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var entries = new List<M3uEntry>();
        var skipped = 0;
        Uri? epgUrl = null;

        PendingEntry? pending = null;
        var isFirstLine = true;

        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } rawLine)
        {
            var line = isFirstLine ? rawLine.TrimStart(ByteOrderMark).Trim() : rawLine.Trim();
            isFirstLine = false;

            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith(HeaderPrefix, StringComparison.OrdinalIgnoreCase))
            {
                epgUrl = ReadEpgUrl(line);
                continue;
            }

            if (line.StartsWith(EntryPrefix, StringComparison.OrdinalIgnoreCase))
            {
                // A new declaration abandons a previous one whose URL never arrived.
                if (pending is not null)
                {
                    skipped++;
                }

                pending = ReadEntryDeclaration(line);

                if (pending is null)
                {
                    skipped++;
                }

                continue;
            }

            if (line.StartsWith(GroupPrefix, StringComparison.OrdinalIgnoreCase))
            {
                // The older way of stating a group. Only honoured when group-title did not already
                // supply one, since the inline attribute is the more specific declaration.
                if (pending is not null && string.IsNullOrWhiteSpace(pending.GroupTitle))
                {
                    pending.GroupTitle = line[GroupPrefix.Length..].Trim();
                }

                continue;
            }

            // Other directives, such as #EXTVLCOPT, carry playback hints this player does not apply.
            if (line.StartsWith('#'))
            {
                continue;
            }

            // Anything else is the address for the declaration in hand.
            if (pending is null)
            {
                skipped++;
                continue;
            }

            if (Uri.TryCreate(line, UriKind.Absolute, out var url))
            {
                entries.Add(pending.ToEntry(url));
            }
            else
            {
                skipped++;
            }

            pending = null;
        }

        // A trailing declaration with no address is unusable.
        if (pending is not null)
        {
            skipped++;
        }

        return new M3uPlaylist(entries, epgUrl, skipped);
    }

    private static Uri? ReadEpgUrl(string headerLine)
    {
        var attributes = ReadAttributes(headerLine.AsSpan(HeaderPrefix.Length));

        if (!attributes.TryGetValue(EpgUrlAttribute, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        // Some playlists list several comma-separated guide URLs; the first usable one is taken.
        foreach (var candidate in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (Uri.TryCreate(candidate, UriKind.Absolute, out var url))
            {
                return url;
            }
        }

        return null;
    }

    /// <summary>
    /// Splits an <c>#EXTINF</c> line into its attributes and its display name.
    /// </summary>
    /// <remarks>
    /// The separator is the first comma that is not inside a quoted value. Scanning for it rather than
    /// splitting on the first or last comma is what makes
    /// <c>#EXTINF:-1 group-title="Sport, News",FR: TF1</c> read correctly — a naive split puts either
    /// the group or the name in the wrong place.
    /// </remarks>
    private static PendingEntry? ReadEntryDeclaration(string line)
    {
        var body = line.AsSpan(EntryPrefix.Length);
        var separatorIndex = FindDisplayNameSeparator(body);

        if (separatorIndex < 0)
        {
            return null;
        }

        var displayName = body[(separatorIndex + 1)..].Trim().ToString();

        if (displayName.Length == 0)
        {
            return null;
        }

        var attributes = ReadAttributes(body[..separatorIndex]);

        return new PendingEntry
        {
            DisplayName = displayName,
            TvgId = Normalize(attributes, "tvg-id"),
            TvgName = Normalize(attributes, "tvg-name"),
            LogoUrl = Normalize(attributes, "tvg-logo"),
            GroupTitle = Normalize(attributes, "group-title"),
            ChannelNumber = ParseChannelNumber(Normalize(attributes, "tvg-chno")),
        };
    }

    private static int FindDisplayNameSeparator(ReadOnlySpan<char> body)
    {
        var isInsideQuotes = false;

        for (var index = 0; index < body.Length; index++)
        {
            var character = body[index];

            if (character == '"')
            {
                isInsideQuotes = !isInsideQuotes;
                continue;
            }

            if (character == ',' && !isInsideQuotes)
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// Reads <c>key="value"</c> pairs, also accepting unquoted values.
    /// </summary>
    /// <remarks>
    /// Unquoted values occur in hand-written playlists and in output from several panel exports. They
    /// are terminated by whitespace, which is why a quoted value is needed for anything containing a
    /// space — and why the quote state has to be tracked rather than assumed.
    /// </remarks>
    private static Dictionary<string, string> ReadAttributes(ReadOnlySpan<char> metadata)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;

        while (index < metadata.Length)
        {
            var equalsIndex = metadata[index..].IndexOf('=');

            if (equalsIndex < 0)
            {
                break;
            }

            equalsIndex += index;

            // The key runs back from the equals sign to the preceding whitespace.
            var keyStart = equalsIndex;

            while (keyStart > index && !char.IsWhiteSpace(metadata[keyStart - 1]))
            {
                keyStart--;
            }

            var key = metadata[keyStart..equalsIndex].Trim().ToString();
            var valueStart = equalsIndex + 1;

            if (valueStart >= metadata.Length)
            {
                break;
            }

            string value;

            if (metadata[valueStart] == '"')
            {
                var closingIndex = metadata[(valueStart + 1)..].IndexOf('"');

                if (closingIndex < 0)
                {
                    // An unterminated quote means the rest of the line is the value.
                    value = metadata[(valueStart + 1)..].ToString();
                    index = metadata.Length;
                }
                else
                {
                    closingIndex += valueStart + 1;
                    value = metadata[(valueStart + 1)..closingIndex].ToString();
                    index = closingIndex + 1;
                }
            }
            else
            {
                var end = valueStart;

                while (end < metadata.Length && !char.IsWhiteSpace(metadata[end]))
                {
                    end++;
                }

                value = metadata[valueStart..end].ToString();
                index = end;
            }

            if (key.Length > 0)
            {
                attributes[key] = value;
            }
        }

        return attributes;
    }

    private static string? Normalize(Dictionary<string, string> attributes, string key)
    {
        if (!attributes.TryGetValue(key, out var value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static int? ParseChannelNumber(string? value)
    {
        if (value is null)
        {
            return null;
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            && number > 0
                ? number
                : null;
    }

    /// <summary>
    /// A declaration awaiting its address, since the two arrive on separate lines.
    /// </summary>
    private sealed class PendingEntry
    {
        public required string DisplayName { get; init; }

        public string? TvgId { get; init; }

        public string? TvgName { get; init; }

        public string? LogoUrl { get; init; }

        /// <summary>Settable, because a following <c>#EXTGRP</c> line may still supply it.</summary>
        public string? GroupTitle { get; set; }

        public int? ChannelNumber { get; init; }

        public M3uEntry ToEntry(Uri url)
        {
            return new M3uEntry(DisplayName, url, TvgId, TvgName, LogoUrl, GroupTitle, ChannelNumber);
        }
    }
}
