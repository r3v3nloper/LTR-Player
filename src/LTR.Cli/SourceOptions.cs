using System.CommandLine;
using LTR.Core.Sources;

namespace LTR.Cli;

/// <summary>
/// The options every command needs to address a panel, defined once and shared.
/// </summary>
/// <remarks>
/// Shared instances rather than per-command copies, because <see cref="ParseResult.GetValue{T}"/>
/// matches on the option object itself. Redefining them per command is the classic way to get a
/// silently empty value.
/// </remarks>
internal sealed class SourceOptions
{
    public SourceOptions()
    {
        Url = new Option<string>("--url")
        {
            Description = "Panel base address, for example http://host:8080",
            Required = true,
        };

        Username = new Option<string>("--user", "-u")
        {
            Description = "Subscription username",
            Required = true,
        };

        Password = new Option<string>("--pass", "-p")
        {
            Description = "Subscription password",
            Required = true,
        };

        UserAgent = new Option<string>("--agent")
        {
            Description = "User agent to send. Panels commonly reject agents they do not recognise.",
            DefaultValueFactory = _ => PlaylistSource.DefaultUserAgent,
        };
    }

    public Option<string> Url { get; }

    public Option<string> Username { get; }

    public Option<string> Password { get; }

    public Option<string> UserAgent { get; }

    /// <summary>Registers all shared options on a command.</summary>
    public void AddTo(Command command)
    {
        command.Options.Add(Url);
        command.Options.Add(Username);
        command.Options.Add(Password);
        command.Options.Add(UserAgent);
    }

    /// <summary>
    /// Builds an in-memory source from the parsed options. Nothing is persisted: the CLI exists to
    /// exercise the core against a live panel, not to manage the user's configuration.
    /// </summary>
    public XtreamSource ToSource(ParseResult parseResult)
    {
        var rawUrl = parseResult.GetValue(Url) ?? string.Empty;

        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var baseUrl))
        {
            throw new ArgumentException($"'{rawUrl}' is not an absolute URL.", nameof(parseResult));
        }

        return new XtreamSource
        {
            Name = baseUrl.Host,
            BaseUrl = baseUrl,
            Username = parseResult.GetValue(Username) ?? string.Empty,
            Password = parseResult.GetValue(Password) ?? string.Empty,
            UserAgent = parseResult.GetValue(UserAgent) ?? PlaylistSource.DefaultUserAgent,
            CreatedUtc = DateTimeOffset.UtcNow,
        };
    }
}
