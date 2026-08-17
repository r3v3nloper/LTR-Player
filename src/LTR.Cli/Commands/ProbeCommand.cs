using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;

namespace LTR.Cli.Commands;

/// <summary>
/// <c>probe</c> — reports the subscription state and what the panel supports.
/// </summary>
internal sealed class ProbeCommand
{
    private readonly IServiceProvider _services;
    private readonly SourceOptions _sourceOptions;

    public ProbeCommand(IServiceProvider services, SourceOptions sourceOptions)
    {
        _services = services;
        _sourceOptions = sourceOptions;
    }

    public Command Build()
    {
        var command = new Command("probe", "Reports the subscription state and what the panel supports.");
        _sourceOptions.AddTo(command);

        command.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(() =>
            _services.GetRequiredService<ProbeCommandHandler>()
                .ExecuteAsync(_sourceOptions.ToSource(parseResult), cancellationToken)));

        return command;
    }
}
