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
}
