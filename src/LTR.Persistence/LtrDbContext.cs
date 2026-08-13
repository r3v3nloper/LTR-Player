using LTR.Core;
using LTR.Core.Content;
using LTR.Core.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace LTR.Persistence;

/// <summary>
/// Unit of work over the local catalogue database.
/// </summary>
/// <remarks>
/// <para>
/// All database knowledge lives here: the schema, the queries and the reconciliation of a provider
/// refresh against what is already stored. Nothing outside this class writes SQL or composes queries,
/// so the storage model can change without rippling outwards.
/// </para>
/// <para>
/// Credentials are protected and unprotected by the explicit methods on this class rather than by an
/// EF value converter. A converter would have to capture the protector instance, and EF caches the
/// model per context type — so a captured dependency becomes a trap the first time a second
/// protector is introduced. Doing it in one place here keeps that impossible.
/// </para>
/// </remarks>
public sealed class LtrDbContext : DbContext
{
    private readonly ICredentialProtector _credentialProtector;

    public LtrDbContext(DbContextOptions<LtrDbContext> options, ICredentialProtector credentialProtector)
        : base(options)
    {
        ArgumentNullException.ThrowIfNull(credentialProtector);
        _credentialProtector = credentialProtector;
    }

    public DbSet<PlaylistSource> Sources => Set<PlaylistSource>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Channel> Channels => Set<Channel>();

    /// <summary>
    /// Stores a newly configured source, protecting its credentials on the way in.
    /// </summary>
    public async Task<int> AddSourceAsync(PlaylistSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source is XtreamSource xtreamSource)
        {
            xtreamSource.Password = _credentialProtector.Protect(xtreamSource.Password);
        }

        Sources.Add(source);
        await SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Detached before the plaintext is restored on the instance. Left tracked, the reveal would
        // mark the entity modified and the next SaveChangesAsync would write the password back in
        // clear text — defeating the protection entirely.
        Entry(source).State = EntityState.Detached;
        RevealCredentials(source);

        return source.Id;
    }

    /// <summary>
    /// Loads every configured source with credentials ready for use.
    /// </summary>
    /// <remarks>
    /// Returned untracked on purpose. The instances are handed back with their passwords in clear
    /// text, and a tracked entity in that state would persist the plaintext on the next save.
    /// </remarks>
    public async Task<IReadOnlyList<PlaylistSource>> GetSourcesAsync(CancellationToken cancellationToken)
    {
        var sources = await Sources
            .AsNoTracking()
            .OrderBy(source => source.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var source in sources)
        {
            RevealCredentials(source);
        }

        return sources;
    }

    /// <summary>
    /// Persists the capability probe result for a source.
    /// </summary>
    public async Task UpdateCapabilitiesAsync(
        int sourceId,
        ProviderCapabilities capabilities,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(capabilities);

        var source = await Sources.FirstOrDefaultAsync(entity => entity.Id == sourceId, cancellationToken)
            .ConfigureAwait(false);

        if (source is null)
        {
            return;
        }

        source.Capabilities = capabilities;
        await SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reconciles a freshly fetched live catalogue against what is stored.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Written as a reconciliation rather than a delete-and-reinsert for one reason: a channel's
    /// favourite flag is the user's own data and must survive a refresh, whereas the provider owns
    /// everything else about it. Wiping the table would silently discard the favourites.
    /// </para>
    /// <para>
    /// Matching is by the provider's own identifier within the source. Entries the provider no longer
    /// offers are removed, so a shrinking subscription does not leave unplayable channels behind.
    /// </para>
    /// </remarks>
    public async Task ReconcileLiveCatalogueAsync(
        int sourceId,
        IReadOnlyList<Category> categories,
        IReadOnlyList<Channel> channels,
        DateTimeOffset refreshedAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(channels);

        var categoryIdsByExternalId = await ReconcileCategoriesAsync(sourceId, categories, cancellationToken)
            .ConfigureAwait(false);

        await ReconcileChannelsAsync(sourceId, channels, categoryIdsByExternalId, cancellationToken)
            .ConfigureAwait(false);

        var source = await Sources.FirstOrDefaultAsync(entity => entity.Id == sourceId, cancellationToken)
            .ConfigureAwait(false);

        if (source is not null)
        {
            source.LastRefreshedUtc = refreshedAtUtc;
        }

        await SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Loads a source's live channels, ordered the way the provider intended.
    /// </summary>
    public async Task<IReadOnlyList<Channel>> GetLiveChannelsAsync(
        int sourceId,
        CancellationToken cancellationToken)
    {
        return await Channels
            .AsNoTracking()
            .Where(channel => channel.SourceId == sourceId)
            .OrderBy(channel => channel.SortOrder)
            .ThenBy(channel => channel.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Loads a source's live categories, ordered the way the provider intended.
    /// </summary>
    public async Task<IReadOnlyList<Category>> GetLiveCategoriesAsync(
        int sourceId,
        CancellationToken cancellationToken)
    {
        return await Categories
            .AsNoTracking()
            .Where(category => category.SourceId == sourceId && category.Kind == ContentKind.Live)
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Rewrites any credential still held in an unprotected form, and reports how many were changed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Run once at startup. Introducing credential protection does not retroactively protect what is
    /// already stored, and nothing else ever rewrites a password — a source is written when it is added
    /// and not again — so without this pass an existing installation would keep its plaintext for good.
    /// </para>
    /// <para>
    /// Deliberately driven by <see cref="ICredentialProtector.IsProtected"/> rather than by a schema
    /// version, so it stays correct when a protector is swapped for a stronger one later: values in the
    /// old form are simply rewritten in the new one.
    /// </para>
    /// </remarks>
    public async Task<int> UpgradeStoredCredentialsAsync(CancellationToken cancellationToken)
    {
        var sources = await Sources
            .OfType<XtreamSource>()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var upgraded = 0;

        foreach (var source in sources)
        {
            if (_credentialProtector.IsProtected(source.Password))
            {
                continue;
            }

            source.Password = _credentialProtector.Protect(source.Password);
            upgraded++;
        }

        if (upgraded > 0)
        {
            await SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return upgraded;
    }

    /// <summary>
    /// Removes a source together with its catalogue.
    /// </summary>
    /// <remarks>
    /// The categories and channels go with it through the cascade configured on their relationships,
    /// so no explicit cleanup is needed and none can be forgotten.
    /// </remarks>
    public async Task DeleteSourceAsync(int sourceId, CancellationToken cancellationToken)
    {
        await Sources
            .Where(source => source.Id == sourceId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Marks or unmarks a channel as a favourite.
    /// </summary>
    public async Task SetFavoriteAsync(int channelId, bool isFavorite, CancellationToken cancellationToken)
    {
        await Channels
            .Where(channel => channel.Id == channelId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(channel => channel.IsFavorite, isFavorite),
                cancellationToken)
            .ConfigureAwait(false);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        ConfigureSources(modelBuilder);
        ConfigureCategories(modelBuilder);
        ConfigureChannels(modelBuilder);

        base.OnModelCreating(modelBuilder);
    }

    private static void ConfigureSources(ModelBuilder modelBuilder)
    {
        var source = modelBuilder.Entity<PlaylistSource>();

        source.ToTable("Sources");
        source.HasKey(entity => entity.Id);
        source.Property(entity => entity.Name).IsRequired().HasMaxLength(200);
        source.Property(entity => entity.UserAgent).IsRequired().HasMaxLength(400);

        // Derived from the concrete subtype's own address, so there is nothing to store.
        source.Ignore(entity => entity.Endpoint);

        // A single table with a readable discriminator, so a new protocol is a new subclass rather
        // than a schema change.
        source.HasDiscriminator<string>("Protocol")
            .HasValue<XtreamSource>("xtream")
            .HasValue<M3uSource>("m3u");

        source.OwnsOne(entity => entity.Capabilities, capabilities =>
        {
            capabilities.Ignore(entity => entity.HasBeenProbed);
        });

        modelBuilder.Entity<XtreamSource>(xtream =>
        {
            xtream.Property(entity => entity.BaseUrl).IsRequired().HasConversion<UriToStringConverter>();
            xtream.Property(entity => entity.Username).IsRequired().HasMaxLength(200);
            xtream.Property(entity => entity.Password).IsRequired().HasMaxLength(1000);
        });

        modelBuilder.Entity<M3uSource>(m3u =>
        {
            m3u.Property(entity => entity.PlaylistUrl).IsRequired().HasConversion<UriToStringConverter>();
            m3u.Property(entity => entity.EpgUrl).HasConversion<UriToStringConverter>();
        });
    }

    private static void ConfigureCategories(ModelBuilder modelBuilder)
    {
        var category = modelBuilder.Entity<Category>();

        category.ToTable("Categories");
        category.HasKey(entity => entity.Id);
        category.Property(entity => entity.ExternalId).IsRequired().HasMaxLength(200);
        category.Property(entity => entity.Name).IsRequired().HasMaxLength(400);

        // The provider's identifier is unique only within its own source, which is what makes this a
        // composite index rather than a key.
        category.HasIndex(entity => new { entity.SourceId, entity.ExternalId, entity.Kind }).IsUnique();

        category.HasOne(entity => entity.Source)
            .WithMany(entity => entity.Categories)
            .HasForeignKey(entity => entity.SourceId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureChannels(ModelBuilder modelBuilder)
    {
        var channel = modelBuilder.Entity<Channel>();

        channel.ToTable("Channels");
        channel.HasKey(entity => entity.Id);
        channel.Property(entity => entity.ExternalId).IsRequired().HasMaxLength(400);
        channel.Property(entity => entity.Name).IsRequired().HasMaxLength(400);
        channel.Property(entity => entity.StreamUrl).HasMaxLength(2000);
        channel.Property(entity => entity.LogoUrl).HasMaxLength(2000);
        channel.Property(entity => entity.EpgChannelId).HasMaxLength(400);
        channel.Property(entity => entity.CategoryExternalId).HasMaxLength(200);

        channel.HasIndex(entity => new { entity.SourceId, entity.ExternalId }).IsUnique();

        // Joining the guide happens by this identifier for every visible channel at once, so it needs
        // an index of its own.
        channel.HasIndex(entity => entity.EpgChannelId);

        channel.HasOne(entity => entity.Source)
            .WithMany(entity => entity.Channels)
            .HasForeignKey(entity => entity.SourceId)
            .OnDelete(DeleteBehavior.Cascade);

        // A category disappearing provider-side must not take its channels with it; they simply become
        // uncategorised until the next refresh.
        channel.HasOne(entity => entity.Category)
            .WithMany(entity => entity.Channels)
            .HasForeignKey(entity => entity.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);
    }

    private async Task<Dictionary<string, int>> ReconcileCategoriesAsync(
        int sourceId,
        IReadOnlyList<Category> incoming,
        CancellationToken cancellationToken)
    {
        var existing = await Categories
            .Where(category => category.SourceId == sourceId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var existingByKey = existing.ToDictionary(category => (category.ExternalId, category.Kind));
        var seen = new HashSet<(string ExternalId, ContentKind Kind)>();

        foreach (var category in incoming)
        {
            seen.Add((category.ExternalId, category.Kind));

            if (existingByKey.TryGetValue((category.ExternalId, category.Kind), out var stored))
            {
                stored.Name = category.Name;
                stored.SortOrder = category.SortOrder;
                continue;
            }

            category.SourceId = sourceId;
            Categories.Add(category);
        }

        Categories.RemoveRange(
            existing.Where(category => !seen.Contains((category.ExternalId, category.Kind))));

        // Saved before channels are reconciled, because the channels need the generated category keys.
        await SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return await Categories
            .Where(category => category.SourceId == sourceId)
            .ToDictionaryAsync(
                category => category.ExternalId,
                category => category.Id,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task ReconcileChannelsAsync(
        int sourceId,
        IReadOnlyList<Channel> incoming,
        Dictionary<string, int> categoryIdsByExternalId,
        CancellationToken cancellationToken)
    {
        var existing = await Channels
            .Where(channel => channel.SourceId == sourceId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var existingByExternalId = existing.ToDictionary(
            channel => channel.ExternalId,
            StringComparer.Ordinal);

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var channel in incoming)
        {
            seen.Add(channel.ExternalId);
            channel.CategoryId = ResolveCategoryId(channel.CategoryExternalId, categoryIdsByExternalId);

            if (existingByExternalId.TryGetValue(channel.ExternalId, out var stored))
            {
                // Everything the provider owns is overwritten; IsFavorite is the user's and is not
                // touched here.
                stored.Name = channel.Name;
                stored.StreamUrl = channel.StreamUrl;
                stored.LogoUrl = channel.LogoUrl;
                stored.EpgChannelId = channel.EpgChannelId;
                stored.CategoryExternalId = channel.CategoryExternalId;
                stored.CategoryId = channel.CategoryId;
                stored.Number = channel.Number;
                stored.HasArchive = channel.HasArchive;
                stored.ArchiveDurationDays = channel.ArchiveDurationDays;
                stored.SortOrder = channel.SortOrder;
                continue;
            }

            channel.SourceId = sourceId;
            Channels.Add(channel);
        }

        Channels.RemoveRange(existing.Where(channel => !seen.Contains(channel.ExternalId)));
    }

    private static int? ResolveCategoryId(
        string? categoryExternalId,
        Dictionary<string, int> categoryIdsByExternalId)
    {
        if (string.IsNullOrEmpty(categoryExternalId))
        {
            return null;
        }

        return categoryIdsByExternalId.TryGetValue(categoryExternalId, out var id) ? id : null;
    }

    private void RevealCredentials(PlaylistSource source)
    {
        if (source is XtreamSource xtreamSource)
        {
            xtreamSource.Password = _credentialProtector.Unprotect(xtreamSource.Password);
        }
    }
}
