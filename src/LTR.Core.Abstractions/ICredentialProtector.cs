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

    /// <summary>
    /// Converts a stored representation back into its plaintext secret.
    /// </summary>
    /// <remarks>
    /// Must accept a value written by an earlier, weaker implementation and return it unchanged rather
    /// than failing. Otherwise introducing protection makes every already-configured source unusable.
    /// Returns an empty string when a value cannot be recovered at all — which happens with
    /// machine-bound or user-bound protection after a reinstall — so that a single unreadable
    /// credential does not prevent the application from starting.
    /// </remarks>
    string Unprotect(string protectedValue);

    /// <summary>
    /// Whether a stored value is already in this implementation's protected form.
    /// </summary>
    /// <remarks>
    /// Exists so stored credentials can be upgraded in place. Without it there is no way to tell a
    /// value that still needs protecting from one that has already been protected, and an upgrade pass
    /// would either skip everything or double-encrypt.
    /// </remarks>
    bool IsProtected(string storedValue);
}
