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
