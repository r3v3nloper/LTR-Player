using LTR.Core.Content;
using LTR.Core.Sources;
using LTR.Providers.Xtream.Dtos;
using Microsoft.Extensions.Logging;

namespace LTR.Providers.Xtream;

/// <summary>
/// Turns Xtream panel responses into domain entities.
/// </summary>
internal sealed class XtreamContentProvider : IContentProvider
{
    private const string UnnamedChannelFallback = "(unnamed channel)";

    private readonly XtreamSource _source;
    private readonly XtreamApiClient _client;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<XtreamContentProvider> _logger;

    public XtreamContentProvider(
        XtreamSource source,
        XtreamApiClient client,
        TimeProvider timeProvider,
        ILogger<XtreamContentProvider> logger)
    {
        _source = source;
        _client = client;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public PlaylistSource Source => _source;

    public async Task<ProviderAccount> AuthenticateAsync(CancellationToken cancellationToken)
    {
        var response = await _client.AuthenticateAsync(_source, cancellationToken).ConfigureAwait(false);
        return MapAccount(response.UserInfo, _timeProvider.GetUtcNow());
    }

    public async Task<IReadOnlyList<Category>> FetchCategoriesAsync(
        ContentKind kind,
        CancellationToken cancellationToken)
    {
        var dtos = await _client.GetCategoriesAsync(_source, kind, cancellationToken).ConfigureAwait(false);

        var categories = new List<Category>(dtos.Count);
        var sortOrder = 0;

        foreach (var dto in dtos)
        {
            if (string.IsNullOrWhiteSpace(dto.CategoryId))
            {
                continue;
            }

            categories.Add(new Category
            {
                SourceId = _source.Id,
                ExternalId = dto.CategoryId,
                Name = string.IsNullOrWhiteSpace(dto.CategoryName) ? dto.CategoryId : dto.CategoryName,
                Kind = kind,
                SortOrder = sortOrder++,
            });
        }

        return categories;
    }

    public async Task<IReadOnlyList<Channel>> FetchLiveChannelsAsync(CancellationToken cancellationToken)
    {
        var dtos = await _client.GetLiveStreamsAsync(_source, cancellationToken).ConfigureAwait(false);

        var channels = new List<Channel>(dtos.Count);
        var skipped = 0;
        var sortOrder = 0;

        foreach (var dto in dtos)
        {
            // Without a stream identifier no playable URL can be built, so the entry is useless.
            if (string.IsNullOrWhiteSpace(dto.StreamId))
            {
                skipped++;
                continue;
            }

            channels.Add(MapChannel(dto, sortOrder++));
        }

        if (skipped > 0)
        {
            XtreamLog.SkippedChannelsWithoutStreamId(_logger, skipped, _source.Name);
        }

        return channels;
    }

    /// <summary>
    /// Interprets a <c>user_info</c> block, reconciling the reported status against the expiry date.
    /// </summary>
    /// <remarks>
    /// The two disagree in practice: panels keep reporting <c>Active</c> past the expiry timestamp.
    /// A lapsed expiry wins, because presenting a working account that then fails on every stream is
    /// worse than saying plainly that the subscription has run out.
    /// </remarks>
    internal static ProviderAccount MapAccount(XtreamUserInfoDto? userInfo, DateTimeOffset now)
    {
        if (userInfo is null || userInfo.Auth == 0)
        {
            return ProviderAccount.Unauthenticated;
        }

        var expiresAt = userInfo.ExpiryUnixSeconds.HasValue
            ? DateTimeOffset.FromUnixTimeSeconds(userInfo.ExpiryUnixSeconds.Value)
            : (DateTimeOffset?)null;

        var status = ParseStatus(userInfo.Status);

        if (status == AccountStatus.Active && expiresAt.HasValue && expiresAt.Value <= now)
        {
            status = AccountStatus.Expired;
        }

        return new ProviderAccount(
            status,
            expiresAt,
            userInfo.IsTrial,
            userInfo.MaxConnections,
            userInfo.ActiveConnections,
            MapAllowedFormats(userInfo.AllowedOutputFormats));
    }

    private static AccountStatus ParseStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return AccountStatus.Unknown;
        }

        return status.Trim().ToLowerInvariant() switch
        {
            "active" => AccountStatus.Active,
            "expired" => AccountStatus.Expired,
            "banned" or "disabled" => AccountStatus.Banned,
            _ => AccountStatus.Unknown,
        };
    }

    private static List<StreamFormat> MapAllowedFormats(List<string>? allowedOutputFormats)
    {
        if (allowedOutputFormats is null || allowedOutputFormats.Count == 0)
        {
            return [];
        }

        var formats = new List<StreamFormat>(allowedOutputFormats.Count);

        foreach (var name in allowedOutputFormats)
        {
            var format = StreamFormatExtensions.FromProviderFormatName(name);

            if (format.HasValue && !formats.Contains(format.Value))
            {
                formats.Add(format.Value);
            }
        }

        return formats;
    }

    private Channel MapChannel(XtreamLiveStreamDto dto, int sortOrder)
    {
        return new Channel
        {
            SourceId = _source.Id,
            ExternalId = dto.StreamId!,
            Name = string.IsNullOrWhiteSpace(dto.Name) ? UnnamedChannelFallback : dto.Name.Trim(),
            LogoUrl = NormalizeLogoUrl(dto.StreamIcon),
            EpgChannelId = string.IsNullOrWhiteSpace(dto.EpgChannelId) ? null : dto.EpgChannelId.Trim(),
            CategoryExternalId = string.IsNullOrWhiteSpace(dto.CategoryId) ? null : dto.CategoryId,
            Number = dto.Number > 0 ? dto.Number : null,
            HasArchive = dto.HasArchive,
            ArchiveDurationDays = dto.ArchiveDurationDays > 0 ? dto.ArchiveDurationDays : null,
            SortOrder = sortOrder,
        };
    }

    /// <summary>
    /// Keeps only logo values that are absolute HTTP addresses.
    /// </summary>
    /// <remarks>
    /// Panels put all sorts of things in this field — empty strings, local file paths, the literal
    /// text "null". Filtering here means the UI never has to guard its image loading.
    /// </remarks>
    private static string? NormalizeLogoUrl(string? streamIcon)
    {
        if (string.IsNullOrWhiteSpace(streamIcon))
        {
            return null;
        }

        if (!Uri.TryCreate(streamIcon.Trim(), UriKind.Absolute, out var uri))
        {
            return null;
        }

        return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps
            ? uri.AbsoluteUri
            : null;
    }
}
