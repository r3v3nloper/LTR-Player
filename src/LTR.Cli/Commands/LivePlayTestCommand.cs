using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;

namespace LTR.Cli.Commands;

/// <summary>
/// <c>play-test</c> — opens a channel headlessly and verifies the connection is released.
/// </summary>
/// <remarks>
/// Named for the live half of the pair: <c>vod play-test</c> does the same for a stored film, and both
/// share the hold itself through <see cref="StreamHoldTest"/>.
/// </remarks>
internal sealed class LivePlayTestCommand
{
    private readonly IServiceProvider _services;
    private readonly SourceOptions _sourceOptions;

    public LivePlayTestCommand(IServiceProvider services, SourceOptions sourceOptions)
    {
        _services = services;
        _sourceOptions = sourceOptions;
    }

    public Command Build()
    {
        var streamIdOption = new Option<string>("--stream-id")
        {
            Description = "Provider stream id, as printed by the channels command.",
            Required = true,
        };

        var secondsOption = new Option<int>("--seconds")
        {
            Description = "How long to hold the stream open.",
            DefaultValueFactory = _ => CommandDefaults.HoldSeconds,
        };

        var command = new Command(
            "play-test",
            "Opens a channel headlessly, reports its tracks, then verifies the connection is released.");

        _sourceOptions.AddTo(command);
        command.Options.Add(streamIdOption);
        command.Options.Add(secondsOption);

        command.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(() =>
            _services.GetRequiredService<PlayTestCommandHandler>()
                .ExecuteAsync(
                    _sourceOptions.ToSource(parseResult),
                    parseResult.GetValue(streamIdOption) ?? string.Empty,
                    parseResult.GetValue(secondsOption),
                    cancellationToken)));

        return command;
    }
}
