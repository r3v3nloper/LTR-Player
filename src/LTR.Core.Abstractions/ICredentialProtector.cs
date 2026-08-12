namespace LTR.Core;

/// <summary>
/// Encrypts and decrypts provider credentials on their way in and out of storage.
/// </summary>
/// <remarks>
/// IPTV subscriptions are paid accounts, so their passwords are worth protecting at rest. The
/// initial implementation is a pass-through; introducing the seam now means swapping in a DPAPI
/// implementation later needs no schema change and no call-site changes.
/// </remarks>
public interface ICredentialProtector
{
    /// <summary>Converts a plaintext secret into its stored representation.</summary>
    string Protect(string plaintext);

    /// <summary>Converts a stored representation back into its plaintext secret.</summary>
    string Unprotect(string protectedValue);
}
