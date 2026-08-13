using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using LTR.Core;
using Microsoft.Extensions.Logging;

namespace LTR.Security.Dpapi;

/// <summary>
/// Protects stored credentials with the Windows Data Protection API, scoped to the current user.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="DataProtectionScope.CurrentUser"/> means only the Windows account that stored a
/// credential can read it back. That is the right trade for a paid subscription password on a personal
/// machine, and it has a consequence worth stating plainly: after a reinstall, a new user profile, or
/// copying the database to another machine, the credentials are gone and the sources have to be added
/// again. The catalogue itself is only a cache of the provider's data, so nothing else is lost.
/// </para>
/// <para>
/// Values carry a version prefix. That is what lets an already-configured installation keep working:
/// a value without the prefix was written before protection existed and is returned as-is, and the
/// upgrade pass in the persistence layer rewrites it protected.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class DpapiCredentialProtector : ICredentialProtector
{
    /// <summary>
    /// Marks a value as produced by this implementation. Versioned so a future change of scheme can be
    /// recognised and migrated rather than guessed at.
    /// </summary>
    private const string Prefix = "dpapi.v1:";

    /// <summary>
    /// Mixed into the protected value. Not a secret — it binds the ciphertext to this application, so a
    /// blob lifted from elsewhere in the user's profile cannot be decrypted through this code path.
    /// </summary>
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("LTR-Player.credentials.v1");

    private readonly ILogger<DpapiCredentialProtector> _logger;

    public DpapiCredentialProtector(ILogger<DpapiCredentialProtector> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        if (plaintext.Length == 0)
        {
            return plaintext;
        }

        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(plaintext),
            Entropy,
            DataProtectionScope.CurrentUser);

        return Prefix + Convert.ToBase64String(protectedBytes);
    }

    public string Unprotect(string protectedValue)
    {
        ArgumentNullException.ThrowIfNull(protectedValue);

        // Written before protection was introduced, so it is already the plaintext.
        if (!IsProtected(protectedValue))
        {
            return protectedValue;
        }

        try
        {
            var protectedBytes = Convert.FromBase64String(protectedValue[Prefix.Length..]);

            var plaintextBytes = ProtectedData.Unprotect(
                protectedBytes,
                Entropy,
                DataProtectionScope.CurrentUser);

            return Encoding.UTF8.GetString(plaintextBytes);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            // Reached when the database was written by a different Windows account or on a different
            // machine. Empty rather than thrown: one unreadable credential must not stop the
            // application from starting, and the source will report an authentication failure, which is
            // the truth — the details are no longer available.
            DpapiLog.CredentialUnreadable(_logger, exception);
            return string.Empty;
        }
    }

    public bool IsProtected(string storedValue)
    {
        ArgumentNullException.ThrowIfNull(storedValue);
        return storedValue.StartsWith(Prefix, StringComparison.Ordinal);
    }
}
