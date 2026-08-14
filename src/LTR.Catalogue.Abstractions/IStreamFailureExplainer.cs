using LTR.Core.Playback;
using LTR.Core.Sources;

namespace LTR.Catalogue;

/// <summary>
/// Works out why a stream would not open, by asking the provider about the subscription.
/// </summary>
/// <remarks>
/// Only ever called after a failure. The question costs a request, and on the happy path there is nothing
/// to explain — but on the unhappy one the provider is the only thing that knows whether the account has
/// expired, whether every connection is in use, or whether the channel alone is at fault.
/// </remarks>
public interface IStreamFailureExplainer
{
    /// <summary>
    /// Classifies the failure, and never throws: an explanation that fails has to degrade to
    /// <see cref="StreamFailureReason.Unknown"/> rather than replace the original failure with its own.
    /// </summary>
    Task<StreamFailureReason> ExplainAsync(PlaylistSource source, CancellationToken cancellationToken);
}
