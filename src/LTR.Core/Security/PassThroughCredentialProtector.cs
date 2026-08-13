namespace LTR.Core.Security;

/// <summary>
/// Stores credentials verbatim. Default implementation until DPAPI protection is wired up.
/// </summary>
public sealed class PassThroughCredentialProtector : ICredentialProtector
{
    public string Protect(string plaintext)
    {
        return plaintext;
    }

    public string Unprotect(string protectedValue)
    {
        return protectedValue;
    }

    /// <summary>
    /// Always true, because this implementation's protected form is the plaintext itself.
    /// </summary>
    /// <remarks>
    /// Reporting false would make an upgrade pass rewrite every credential on every start, achieving
    /// nothing.
    /// </remarks>
    public bool IsProtected(string storedValue)
    {
        return true;
    }
}
