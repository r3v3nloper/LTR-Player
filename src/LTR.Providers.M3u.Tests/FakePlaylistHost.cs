using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LTR.Providers.M3u;

/// <summary>
/// A throwaway HTTP server standing in for the host a playlist or its guide is fetched from.
/// </summary>
/// <remarks>
/// A real Kestrel host rather than a stubbed <see cref="HttpMessageHandler"/>, for the reason the Xtream
/// project's equivalent gives: status-code handling and request headers are part of what is under test, and
/// a handler stub would fake them rather than exercise them.
/// </remarks>
internal sealed class FakePlaylistHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    private FakePlaylistHost(WebApplication app, Uri baseUrl)
    {
        _app = app;
        BaseUrl = baseUrl;
    }

    public Uri BaseUrl { get; }

    public static async Task<FakePlaylistHost> StartAsync(RequestDelegate handler)
    {
        var builder = WebApplication.CreateSlimBuilder();

        // Port 0 lets the OS pick a free port, so tests can run concurrently.
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddLogging(logging => logging.ClearProviders());

        var app = builder.Build();
        app.Run(handler);

        await app.StartAsync().ConfigureAwait(false);

        return new FakePlaylistHost(app, new Uri(app.Urls.First(), UriKind.Absolute));
    }

    /// <summary>Answers every request with <paramref name="statusCode"/> and no body.</summary>
    public static Task<FakePlaylistHost> StartAsync(int statusCode)
    {
        return StartAsync(context =>
        {
            context.Response.StatusCode = statusCode;
            return Task.CompletedTask;
        });
    }

    /// <summary>An address on this host, relative to its base.</summary>
    public Uri Address(string relative)
    {
        return new Uri(BaseUrl, relative);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
    }
}
