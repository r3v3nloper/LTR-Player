using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LTR.Providers.Xtream;

/// <summary>
/// A throwaway HTTP server standing in for an Xtream panel.
/// </summary>
/// <remarks>
/// A real Kestrel host rather than a stubbed <see cref="HttpMessageHandler"/>, because the
/// behaviour under test includes redirect following, request headers and status-code handling —
/// all of which a handler stub would fake rather than exercise.
/// </remarks>
internal sealed class FakePanel : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentQueue<string> _observedUserAgents = new();

    private FakePanel(WebApplication app, Uri baseUrl)
    {
        _app = app;
        BaseUrl = baseUrl;
    }

    public Uri BaseUrl { get; }

    /// <summary>User agents seen on incoming requests, in arrival order.</summary>
    public IReadOnlyCollection<string> ObservedUserAgents => [.. _observedUserAgents];

    public static async Task<FakePanel> StartAsync(RequestDelegate handler)
    {
        var builder = WebApplication.CreateSlimBuilder();

        // Port 0 lets the OS pick a free port, so tests can run concurrently.
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddLogging(logging => logging.ClearProviders());

        var app = builder.Build();
        FakePanel? panel = null;

        app.Run(async context =>
        {
            panel?.RecordUserAgent(context);
            await handler(context).ConfigureAwait(false);
        });

        await app.StartAsync().ConfigureAwait(false);

        var address = app.Urls.First();
        panel = new FakePanel(app, new Uri(address, UriKind.Absolute));
        return panel;
    }

    /// <summary>
    /// Starts a panel that answers each <c>action</c> with a canned body, and answers anything not
    /// listed with 404 — which is how a panel that lacks an endpoint actually behaves.
    /// </summary>
    public static Task<FakePanel> StartAsync(IReadOnlyDictionary<string, string> responsesByAction)
    {
        return StartAsync(async context =>
        {
            var action = context.Request.Query["action"].ToString();

            if (!responsesByAction.TryGetValue(action, out var body))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(body).ConfigureAwait(false);
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
    }

    private void RecordUserAgent(HttpContext context)
    {
        _observedUserAgents.Enqueue(context.Request.Headers.UserAgent.ToString());
    }
}
