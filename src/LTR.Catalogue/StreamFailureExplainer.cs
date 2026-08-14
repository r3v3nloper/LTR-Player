using LTR.Core.Playback;
using LTR.Core.Sources;
using LTR.Providers;
using Microsoft.Extensions.Logging;

namespace LTR.Catalogue;

/// <summary>
/// Asks the provider why a stream would not open.
/// </summary>
/// <remarks>
/// <para>
/// The engine can only report that the stream did not start. Whether that is the channel, the subscription's
/// connection limit or an expired account is a fact the provider holds, and the three want different things
/// from the viewer — so the one thing this must not do is guess.
/// </para>
/// <para>
/// In the application layer rather than in the window, because the classification is the same for the command
/// line and for a web frontend later; only the sentence differs, and the sentence is each front end's own.
/// </para>
/// </remarks>
internal sealed class StreamFailureExplainer : IStreamFailureExplainer
{
    private readonly IProviderRegistry _providers;
    private readonly ILogger<StreamFailureExplainer> _logger;

    public StreamFailureExplainer(IProviderRegistry providers, ILogger<StreamFailureExplainer> logger)
    {
        _providers = providers;
        _logger = logger;
    }

    public async Task<StreamFailureReason> ExplainAsync(
        PlaylistSource source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        // A playlist has no account to ask about, and asking anyway would re-download the whole document to
        // learn nothing — the channel is all that is left to blame.
        if (!source.ReportsAccountState)
        {
            return StreamFailureReason.ChannelUnavailable;
        }

        try
        {
            var provider = _providers.CreateProvider(source);
            var account = await provider.AuthenticateAsync(cancellationToken).ConfigureAwait(false);

            return Classify(account);
        }
        catch (HttpRequestException exception)
        {
            // As likely to be the local connection as the provider, and the wording says so.
            CatalogueLog.FailureNotExplained(_logger, exception, source.Name);
            return StreamFailureReason.ProviderUnreachable;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // A request timeout, which arrives as a cancellation indistinguishable from the caller giving
            // up except by asking the token. Treating it as the caller's would report nothing at all.
            return StreamFailureReason.ProviderUnreachable;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Includes a panel answering with an HTML error page at HTTP 200, which is a protocol-specific
            // exception this layer cannot name. Unknown is the honest answer: it was asked, and the reply
            // could not be read.
            CatalogueLog.FailureNotExplained(_logger, exception, source.Name);
            return StreamFailureReason.Unknown;
        }
    }

    /// <remarks>
    /// The connection limit is checked only for an account that is otherwise in order, because an expired
    /// subscription reports nonsense connection counts and "all connections in use" would send the viewer
    /// hunting for a second device that is not the problem.
    /// </remarks>
    private static StreamFailureReason Classify(ProviderAccount account)
    {
        return account.Status switch
        {
            AccountStatus.Expired => StreamFailureReason.SubscriptionExpired,
            AccountStatus.Banned => StreamFailureReason.SubscriptionDisabled,
            AccountStatus.AuthenticationFailed => StreamFailureReason.CredentialsRejected,
            AccountStatus.Active when !account.HasFreeConnection =>
                StreamFailureReason.ConnectionLimitReached,
            AccountStatus.Active => StreamFailureReason.ChannelUnavailable,
            _ => StreamFailureReason.Unknown,
        };
    }
}
