using LTR.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace LTR.Catalogue;

/// <summary>
/// Runs one operation against the database and closes the unit of work behind it.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="LtrDbContext"/> is meant to be short-lived (CLAUDE.md §3.3.2), which leaves every
/// long-lived service in this layer needing the same four lines: create a scope, resolve a context, use
/// it, dispose the scope. Stated once here.
/// </para>
/// <para>
/// It matters most for the guide import, which writes a few hundred batches over several minutes. Each
/// batch getting its own context is what keeps a change tracker from accumulating a whole guide's worth
/// of entities behind it.
/// </para>
/// </remarks>
internal sealed class CatalogueUnitOfWork
{
    private readonly IServiceScopeFactory _scopeFactory;

    public CatalogueUnitOfWork(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<T> RunAsync<T>(Func<LtrDbContext, Task<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<LtrDbContext>();

        return await operation(context).ConfigureAwait(false);
    }

    public async Task RunAsync(Func<LtrDbContext, Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<LtrDbContext>();

        await operation(context).ConfigureAwait(false);
    }
}
