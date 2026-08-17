using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;

namespace LTR.Cli.Commands;

/// <summary>
/// <c>channels</c> — fetches the live catalogue from a panel and summarises it.
/// </summary>
internal sealed class ChannelsCommand
{
    private readonly IServiceProvider _services;
    private readonly SourceOptions _sourceOptions;

    public ChannelsCommand(IServiceProvider services, SourceOptions sourceOptions)
    {
        _services = services;
        _sourceOptions = sourceOptions;
    }

    public Command Build()
    {
        var filterOption = new Option<string?>("--filter", "-f")
        {
            Description = "Show only channels whose name contains this text.",
        };

        var limitOption = new Option<int>("--limit")
        {
            Description = "Maximum number of channels to print.",
            DefaultValueFactory = _ => CommandDefaults.Limit,
        };

        var command = new Command("channels", "Fetches the live catalogue and summarises it.");
        _sourceOptions.AddTo(command);
        command.Options.Add(filterOption);
        command.Options.Add(limitOption);

        command.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(() =>
            _services.GetRequiredService<ChannelsCommandHandler>()
                .ExecuteAsync(
                    _sourceOptions.ToSource(parseResult),
                    parseResult.GetValue(filterOption),
                    parseResult.GetValue(limitOption),
                    cancellationToken)));

        return command;
    }
}
