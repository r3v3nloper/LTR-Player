using Microsoft.Extensions.Logging;

namespace LTR.Security.Dpapi;

internal static partial class DpapiLog
{
    [LoggerMessage(
        EventId = 4000,
        Level = LogLevel.Warning,
        Message = "A stored credential could not be decrypted. Protection is bound to the Windows user "
            + "account that wrote it, so this happens after a reinstall or when the database was copied "
            + "from another machine. The affected source has to be added again.")]
    public static partial void CredentialUnreadable(ILogger logger, Exception exception);
}
