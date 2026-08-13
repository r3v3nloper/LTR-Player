using System.Text;
using System.Text.RegularExpressions;

namespace LTR.Core.Content;

/// <summary>
/// Recognises patterns in the channel names providers actually publish.
/// </summary>
public static partial class ChannelNaming
{
    /// <summary>
    /// Whether a name is a visual separator rather than a channel.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Providers insert decorative rows into their channel lists to group them — entries such as
    /// <c>FR: ----- FRANCE -----</c>. These carry a valid stream id, so nothing about the API marks
    /// them as unplayable; only the name gives them away. Presenting them as channels means the user
    /// selects one and gets a failure.
    /// </para>
    /// <para>
    /// The threshold is four repetitions rather than three, so an ellipsis in a genuine name is not
    /// mistaken for decoration.
    /// </para>
    /// </remarks>
    public static bool IsSeparatorLabel(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return true;
        }

        var trimmed = name.Trim();

        // A name with nothing alphanumeric in it cannot identify a channel.
        if (!ContainsAlphanumeric(trimmed))
        {
            return true;
        }

        return DecorativeRunPattern().IsMatch(trimmed);
    }

    /// <summary>
    /// Derives a stable identity key from a channel name.
    /// </summary>
    /// <remarks>
    /// For sources that supply no identifier of their own, the name is all there is to recognise a
    /// channel by across refreshes. Case, punctuation and spacing are discarded because providers
    /// change those freely; nothing else is, so two genuinely different channels do not collapse into
    /// one. Quality markers such as HD are deliberately kept — <c>TF1 HD</c> and <c>TF1 FHD</c> are
    /// separate entries with separate URLs.
    /// </remarks>
    public static string ToIdentityKey(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var key = new StringBuilder(name.Length);

        foreach (var character in name)
        {
            if (char.IsLetterOrDigit(character))
            {
                key.Append(char.ToLowerInvariant(character));
            }
        }

        return key.ToString();
    }

    /// <summary>
    /// Derives a key for matching a channel against a programme guide by name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Weaker than <see cref="ToIdentityKey"/> on purpose, and used for a different job. Identity has to
    /// keep every distinction a provider makes, because two channels must never collapse into one. Guide
    /// matching has the opposite problem: the guide and the channel list are published by different
    /// parties, so <c>FR: TF1 FHD</c> and <c>TF1</c> are the same channel written twice and have to reach
    /// the same key.
    /// </para>
    /// <para>
    /// Three things are removed: a leading country or language tag, quality and packaging markers, and
    /// all punctuation. <c>+</c> survives, because <c>TF1 +1</c> is a timeshift channel showing something
    /// else entirely — collapsing it into <c>TF1</c> would attach the wrong programme to it, which is
    /// worse than attaching none.
    /// </para>
    /// </remarks>
    public static string ToGuideMatchKey(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var trimmed = name.Trim();
        var withoutPrefix = StripRegionPrefix(trimmed);
        var withoutMarkers = StripQualityMarkers(withoutPrefix);
        var key = KeepMatchableCharacters(withoutMarkers);

        // Stripping is only ever an improvement while something is left to match on. A name made
        // entirely of markers — "HD", say — keeps its unstripped form rather than becoming empty and
        // matching every other stripped-empty name in the guide.
        return key.Length > 0 ? key : KeepMatchableCharacters(trimmed);
    }

    /// <summary>
    /// Removes a leading region tag such as <c>FR: </c>, <c>[DE] </c> or <c>UK | </c>.
    /// </summary>
    /// <remarks>
    /// Two or three letters only, and a separator is required. That is narrow enough to leave
    /// <c>Eurosport 1</c> alone while catching what providers actually prefix, and where it does misfire
    /// on a genuine name the matcher's ambiguity rule is what stops a wrong guide being attached.
    /// </remarks>
    private static string StripRegionPrefix(string name)
    {
        var match = RegionPrefixPattern().Match(name);

        if (!match.Success)
        {
            return name;
        }

        var remainder = name[match.Length..].Trim();
        return ContainsAlphanumeric(remainder) ? remainder : name;
    }

    private static string StripQualityMarkers(string name)
    {
        var stripped = QualityMarkerPattern().Replace(name, " ");
        return ContainsAlphanumeric(stripped) ? stripped : name;
    }

    private static string KeepMatchableCharacters(string name)
    {
        var key = new StringBuilder(name.Length);

        foreach (var character in name)
        {
            if (char.IsLetterOrDigit(character))
            {
                key.Append(char.ToLowerInvariant(character));
                continue;
            }

            if (character == '+')
            {
                key.Append(character);
            }
        }

        return key.ToString();
    }

    /// <summary>
    /// Matches four or more repetitions of the same non-alphanumeric character, which is how these
    /// decorative rows are drawn.
    /// </summary>
    [GeneratedRegex(@"([^\p{L}\p{N}\s])\1{3,}", RegexOptions.CultureInvariant)]
    private static partial Regex DecorativeRunPattern();

    /// <summary>
    /// Two forms occur and they delimit differently: a bracketed tag needs no separator after it
    /// (<c>[DE] Sat.1</c>), whereas a bare one does (<c>FR: TF1</c>, <c>UK | Sky</c>, <c>DE - ARD</c>).
    /// </summary>
    [GeneratedRegex(
        @"^(?:[\[\(]\s*\p{L}{2,3}\s*[\]\)]|\|?\s*\p{L}{2,3}\s*[:\|\-])\s*",
        RegexOptions.CultureInvariant)]
    private static partial Regex RegionPrefixPattern();

    /// <summary>
    /// Matches the quality and packaging markers providers append. Whole tokens only, so the <c>HD</c>
    /// of <c>HDTV Kanal</c> is left where it is.
    /// </summary>
    [GeneratedRegex(
        @"(?<![\p{L}\p{N}])(?:U?HD|FHD|SD|HQ|LQ|[48]K|H\.?26[45]|HEVC|RAW|VIP|MULTI|BACKUP|B/?U)"
        + @"(?![\p{L}\p{N}])",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex QualityMarkerPattern();

    private static bool ContainsAlphanumeric(string value)
    {
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                return true;
            }
        }

        return false;
    }
}
