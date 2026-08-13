using LTR.Core;

namespace LTR.Persistence;

/// <summary>
/// Reverses the string it is given, behind a marker prefix.
/// </summary>
/// <remarks>
/// Not encryption, and not meant to be. Reversal is trivially invertible yet visibly different from the
/// input, which lets a test prove that what reaches the database is the protected form and what leaves
/// the context is the plaintext. A pass-through protector could not distinguish the two.
///
/// The prefix mirrors the real implementation, and for the same reason: without a marker there is no
/// way to tell an already-protected value from one written before protection existed, and the upgrade
/// pass would either skip everything or protect it twice.
/// </remarks>
internal sealed class ReversingCredentialProtector : ICredentialProtector
{
    private const string Prefix = "rev:";

    public string Protect(string plaintext)
    {
        return plaintext.Length == 0 ? plaintext : Prefix + Reverse(plaintext);
    }

    public string Unprotect(string protectedValue)
    {
        return IsProtected(protectedValue)
            ? Reverse(protectedValue[Prefix.Length..])
            : protectedValue;
    }

    public bool IsProtected(string storedValue)
    {
        return storedValue.StartsWith(Prefix, StringComparison.Ordinal);
    }

    private static string Reverse(string value)
    {
        var characters = value.ToCharArray();
        Array.Reverse(characters);
        return new string(characters);
    }
}
