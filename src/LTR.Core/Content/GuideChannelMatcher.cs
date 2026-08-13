namespace LTR.Core.Content;

/// <summary>
/// Decides which guide channel each channel's programmes come from.
/// </summary>
/// <remarks>
/// <para>
/// The guide id would be the obvious answer, and for the minority of channels that carry one it is the
/// answer used. On a real subscription most channels do not: in the 17,000-channel list this was built
/// against, 72% have no guide id whatsoever. Matching by name is therefore the primary path, not a
/// fallback, and it decides whether the guide appears useful or broken.
/// </para>
/// <para>
/// Pure and free of persistence on purpose. It is the one piece of guide handling with real rules in it,
/// and keeping it here means those rules are tested directly rather than through a database.
/// </para>
/// </remarks>
public static class GuideChannelMatcher
{
    /// <summary>
    /// Maps channel identity to guide channel identity for every channel that could be matched.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Attempted in order of how much is being assumed: the guide id, then the name as written, then the
    /// name with region tags and quality markers stripped. The first that resolves wins, so a channel
    /// that states its guide id is never overruled by a name that happens to look similar.
    /// </para>
    /// <para>
    /// A name that resolves to more than one guide channel is left unmatched. Half the guide attached to
    /// the wrong channel is indistinguishable from a broken player, whereas a channel with no programme
    /// information is merely a channel with no programme information.
    /// </para>
    /// </remarks>
    public static IReadOnlyDictionary<int, int> Match(
        IReadOnlyList<Channel> channels,
        IReadOnlyList<GuideChannel> guideChannels)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(guideChannels);

        var byExternalId = BuildLookup(guideChannels, guide => guide.ExternalId);
        var byName = BuildLookup(guideChannels, guide => Key(guide, ChannelNaming.ToIdentityKey));
        var byRelaxedName = BuildLookup(guideChannels, guide => Key(guide, ChannelNaming.ToGuideMatchKey));

        var links = new Dictionary<int, int>(channels.Count);

        foreach (var channel in channels)
        {
            if (Resolve(channel, byExternalId, byName, byRelaxedName) is { } guideChannelId)
            {
                links[channel.Id] = guideChannelId;
            }
        }

        return links;
    }

    private static int? Resolve(
        Channel channel,
        Dictionary<string, int?> byExternalId,
        Dictionary<string, int?> byName,
        Dictionary<string, int?> byRelaxedName)
    {
        if (!string.IsNullOrWhiteSpace(channel.EpgChannelId)
            && byExternalId.TryGetValue(channel.EpgChannelId.Trim(), out var byId)
            && byId is not null)
        {
            return byId;
        }

        if (byName.TryGetValue(ChannelNaming.ToIdentityKey(channel.Name), out var byExactName)
            && byExactName is not null)
        {
            return byExactName;
        }

        return byRelaxedName.TryGetValue(ChannelNaming.ToGuideMatchKey(channel.Name), out var relaxed)
            ? relaxed
            : null;
    }

    /// <summary>
    /// Indexes the guide channels by one key, mapping a key several of them share to
    /// <see langword="null"/> so an ambiguous match is recognisable rather than arbitrary.
    /// </summary>
    private static Dictionary<string, int?> BuildLookup(
        IReadOnlyList<GuideChannel> guideChannels,
        Func<GuideChannel, string?> keySelector)
    {
        var lookup = new Dictionary<string, int?>(guideChannels.Count, StringComparer.OrdinalIgnoreCase);

        foreach (var guideChannel in guideChannels)
        {
            var key = keySelector(guideChannel);

            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            lookup[key] = lookup.ContainsKey(key) ? null : guideChannel.Id;
        }

        return lookup;
    }

    private static string? Key(GuideChannel guideChannel, Func<string, string> normalize)
    {
        return string.IsNullOrWhiteSpace(guideChannel.DisplayName)
            ? null
            : normalize(guideChannel.DisplayName);
    }
}
