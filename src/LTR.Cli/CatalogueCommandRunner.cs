using LTR.Catalogue;
using Microsoft.Extensions.DependencyInjection;

namespace LTR.Cli;

/// <summary>
/// Prepares the catalogue, then runs a command against it.
/// </summary>
/// <remarks>
/// <para>
/// Preparation happens here rather than at startup because only some commands touch the database:
/// probing a panel or testing playback should not create a database file as a side effect. Both
/// applications share one, and either may run first, so whichever does has to migrate it and protect
/// any credential still held in plain text.
/// </para>
/// <para>
/// The handler is resolved when the command runs and not before. Resolving one eagerly would construct
/// LibVLC to list a source, and the command tree is built for every invocation.
/// </para>
/// </remarks>
internal sealed class CatalogueCommandRunner
{
    private readonly IServiceProvider _services;

    public CatalogueCommandRunner(IServiceProvider services)
    {
        _services = services;
    }

    public async Task<int> RunAsync<THandler>(Func<THandler, Task<int>> action)
        where THandler : notnull
    {
        ArgumentNullException.ThrowIfNull(action);

        var preparation = await _services.PrepareCatalogueAsync(CancellationToken.None).ConfigureAwait(false);

        if (preparation.UpgradedCredentials > 0)
        {
            Console.WriteLine(
                $"Protected {preparation.UpgradedCredentials} stored credential(s) that were held in plain text.");
        }

        if (preparation.QuarantinedDatabasePath is { } quarantined)
        {
            Console.Error.WriteLine(
                "The catalogue could not be read and was set aside; starting from an empty one. Sources will "
                + "have to be added again.");
            Console.Error.WriteLine($"Unreadable file kept at: {quarantined}");
        }

        return await action(_services.GetRequiredService<THandler>()).ConfigureAwait(false);
    }
}
