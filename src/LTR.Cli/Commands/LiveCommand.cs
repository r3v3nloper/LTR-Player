using System.CommandLine;

namespace LTR.Cli.Commands;

/// <summary>
/// <c>live</c> — inspects a stored source's live channels and resolves one to an address.
/// </summary>
/// <remarks>
/// The stored counterpart of the panel commands, as <c>vod</c> is for films: <c>channels</c> fetches a
/// listing from a panel and needs credentials, while these work from what the catalogue holds and need only
/// a source id. That is what lets a playlist be addressed at all — its channel addresses arrive inside the
/// playlist and are stored, not composed from credentials.
///
/// Kept as its own group rather than as an alternative mode of <c>resolve</c>: that command's panel options
/// are required, on instances shared with three other commands, and two mutually exclusive ways of naming a
/// source is exactly what System.CommandLine cannot express — the same reason <c>vod play-test</c> is its own
/// command rather than a flag on <c>play-test</c>.
/// </remarks>
internal sealed class LiveCommand
{
    private readonly CatalogueCommandRunner _catalogue;

    public LiveCommand(CatalogueCommandRunner catalogue)
    {
        _catalogue = catalogue;
    }

    public Command Build()
    {
        var sourceIdOption = new Option<int>("--source-id")
        {
            Description = "Source id, as shown by 'sources list'.",
            Required = true,
        };

        var command = new Command("live", "Inspects a stored source's live channels.");
        command.Subcommands.Add(BuildList(sourceIdOption));
        command.Subcommands.Add(BuildResolve(sourceIdOption));

        return command;
    }

    private Command BuildList(Option<int> sourceIdOption)
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

        var command = new Command("list", "Lists the stored live channels.");
        command.Options.Add(sourceIdOption);
        command.Options.Add(filterOption);
        command.Options.Add(limitOption);

        command.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(() =>
            _catalogue.RunAsync<LiveCommandHandler>(handler => handler.ListAsync(
                parseResult.GetValue(sourceIdOption),
                parseResult.GetValue(filterOption),
                parseResult.GetValue(limitOption),
                cancellationToken))));

        return command;
    }

    private Command BuildResolve(Option<int> sourceIdOption)
    {
        var channelIdOption = new Option<int>("--channel-id")
        {
            Description = "Local channel id, as shown by 'live list'. Not the provider's own id.",
            Required = true,
        };

        var revealOption = new Option<bool>("--reveal")
        {
            Description = "Print the address with credentials in clear text.",
        };

        var command = new Command(
            "resolve",
            "Builds the playable address for one stored channel, of a panel or of a playlist.");

        command.Options.Add(sourceIdOption);
        command.Options.Add(channelIdOption);
        command.Options.Add(revealOption);

        command.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(() =>
            _catalogue.RunAsync<LiveCommandHandler>(handler => handler.ResolveAsync(
                parseResult.GetValue(sourceIdOption),
                parseResult.GetValue(channelIdOption),
                parseResult.GetValue(revealOption),
                cancellationToken))));

        return command;
    }
}
