using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;

namespace LTR.Cli.Commands;

/// <summary>
/// <c>resolve</c> — builds the playable address for one channel.
/// </summary>
internal sealed class ResolveCommand
{
    private readonly IServiceProvider _services;
    private readonly SourceOptions _sourceOptions;

    public ResolveCommand(IServiceProvider services, SourceOptions sourceOptions)
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

        var revealOption = new Option<bool>("--reveal")
        {
            Description = "Print the address with credentials in clear text.",
        };

        var probeOption = new Option<bool>("--probe")
        {
            Description = "Probe the panel first, so the container matches what it actually serves.",
        };

        var command = new Command("resolve", "Builds the playable address for one channel.");
        _sourceOptions.AddTo(command);
        command.Options.Add(streamIdOption);
        command.Options.Add(revealOption);
        command.Options.Add(probeOption);

        command.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(() =>
            _services.GetRequiredService<ResolveCommandHandler>()
                .ExecuteAsync(
                    _sourceOptions.ToSource(parseResult),
                    parseResult.GetValue(streamIdOption) ?? string.Empty,
                    parseResult.GetValue(revealOption),
                    parseResult.GetValue(probeOption),
                    cancellationToken)));

        return command;
    }
}
