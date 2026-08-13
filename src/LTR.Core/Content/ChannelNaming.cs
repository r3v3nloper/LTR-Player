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
    /// Matches four or more repetitions of the same non-alphanumeric character, which is how these
    /// decorative rows are drawn.
    /// </summary>
    [GeneratedRegex(@"([^\p{L}\p{N}\s])\1{3,}", RegexOptions.CultureInvariant)]
    private static partial Regex DecorativeRunPattern();

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
