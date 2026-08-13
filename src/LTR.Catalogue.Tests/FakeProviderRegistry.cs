using LTR.Core.Content;
using LTR.Core.Playback;
using LTR.Core.Sources;
using LTR.Providers;

namespace LTR.Catalogue;

/// <summary>
/// Stands in for the provider layer, and records what the import asked it for.
/// </summary>
/// <remarks>
/// The provider boundary is faked rather than the database: the behaviour under test is the order of the
/// import sequence and what it stores, and both are only meaningful against a real store.
/// </remarks>
internal sealed class FakeProviderRegistry : IProviderRegistry, IContentProvider, IProviderCapabilityProbe
{
    private readonly List<string> _calls = [];

    public FakeProviderRegistry(PlaylistSource source)
    {
        Source = source;
    }

    public PlaylistSource Source { get; }

    /// <summary>Calls received, in order, so the sequence itself can be asserted.</summary>
    public IReadOnlyList<string> Calls => _calls;

    public ProviderAccount Account { get; set; } = new(
        AccountStatus.Active,
        ExpiresAtUtc: null,
        IsTrial: false,
        MaxConnections: 1,
        ActiveConnections: 0,
        AllowedFormats: [StreamFormat.MpegTs]);

    public List<Category> Categories { get; } = [];

    public List<Channel> Channels { get; } = [];

    public ProviderCapabilities Capabilities { get; set; } = new() { SupportsLive = true };

    public IContentProvider CreateProvider(PlaylistSource source)
    {
        return this;
    }

    public IProviderCapabilityProbe GetCapabilityProbe(PlaylistSource source)
    {
        return this;
    }

    public IStreamUrlResolver GetStreamUrlResolver(PlaylistSource source)
    {
        throw new NotSupportedException("Importing never resolves a stream address.");
    }

    public Task<ProviderAccount> AuthenticateAsync(CancellationToken cancellationToken)
    {
        _calls.Add("authenticate");
        return Task.FromResult(Account);
    }

    public bool Supports(PlaylistSource source)
    {
        return true;
    }

    public Task<ProviderCapabilities> ProbeAsync(PlaylistSource source, CancellationToken cancellationToken)
    {
        _calls.Add("probe");
        return Task.FromResult(Capabilities);
    }

    public Task<IReadOnlyList<Category>> FetchCategoriesAsync(
        ContentKind kind,
        CancellationToken cancellationToken)
    {
        _calls.Add("categories");
        return Task.FromResult<IReadOnlyList<Category>>(Categories);
    }

    public Task<IReadOnlyList<Channel>> FetchLiveChannelsAsync(CancellationToken cancellationToken)
    {
        _calls.Add("channels");
        return Task.FromResult<IReadOnlyList<Channel>>(Channels);
    }
}
