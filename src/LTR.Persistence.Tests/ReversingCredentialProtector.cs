using LTR.Core;

namespace LTR.Persistence;

/// <summary>
/// Reverses the string it is given.
/// </summary>
/// <remarks>
/// Not encryption, and not meant to be. Reversal is trivially invertible yet visibly different from
/// the input, which lets a test prove that what reaches the database is the protected form and what
/// leaves the context is the plaintext. A pass-through protector could not distinguish the two.
/// </remarks>
internal sealed class ReversingCredentialProtector : ICredentialProtector
{
    public string Protect(string plaintext)
    {
        return Reverse(plaintext);
    }

    public string Unprotect(string protectedValue)
    {
        return Reverse(protectedValue);
    }

    private static string Reverse(string value)
    {
        var characters = value.ToCharArray();
        Array.Reverse(characters);
        return new string(characters);
    }
}
