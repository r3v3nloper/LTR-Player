using LTR.Catalogue;
using LTR.Core.Sources;

namespace LTR.Player.Wpf;

/// <summary>
/// Stands in for the import service, and records what it was asked to do.
/// </summary>
internal sealed class FakeSourceImportService : ISourceImportService
{
    private int _nextSourceId = 1;

    public List<PlaylistSource> Imported { get; } = [];

    public List<PlaylistSource> Refreshed { get; } = [];

    /// <summary>
    /// The account the import reports. Set to an unusable one to exercise the rejection path.
    /// </summary>
    public ProviderAccount Account { get; set; } = new(
        AccountStatus.Active,
        ExpiresAtUtc: null,
        IsTrial: false,
        MaxConnections: 1,
        ActiveConnections: 0,
        AllowedFormats: []);

    public Task<SourceImportResult> ImportAsync(
        PlaylistSource source,
        IProgress<SourceImportStage>? progress,
        CancellationToken cancellationToken)
    {
        Imported.Add(source);

        if (!Account.IsUsable)
        {
            return Task.FromResult(SourceImportResult.Rejected(Account));
        }

        // Stands in for the store assigning an identity, which the view model relies on.
        source.Id = _nextSourceId++;

        return Task.FromResult(new SourceImportResult(Account, source.Id, ChannelCount: 0, CategoryCount: 0));
    }

    public Task<SourceImportResult> RefreshAsync(
        PlaylistSource source,
        IProgress<SourceImportStage>? progress,
        CancellationToken cancellationToken)
    {
        Refreshed.Add(source);

        return Task.FromResult(Account.IsUsable
            ? new SourceImportResult(Account, source.Id, ChannelCount: 0, CategoryCount: 0)
            : SourceImportResult.Rejected(Account));
    }
}
