using System.CommandLine;

namespace LTR.Cli.Commands;

/// <summary>
/// <c>sources</c> — lists, adds, refreshes or removes sources in the local catalogue.
/// </summary>
internal sealed class SourcesCommand
{
    private readonly CatalogueCommandRunner _catalogue;

    public SourcesCommand(CatalogueCommandRunner catalogue)
    {
        _catalogue = catalogue;
    }

    public Command Build()
    {
        var command = new Command("sources", "Lists, adds or removes sources in the local catalogue.");

        // One instance per option, shared by the subcommands that take it: ParseResult matches on the
        // option object itself, and a redefined one reads back empty.
        var idArgument = new Argument<int>("id") { Description = "Source id, as shown by 'sources list'." };

        command.Subcommands.Add(BuildList());
        command.Subcommands.Add(BuildAddPlaylist());
        command.Subcommands.Add(BuildRefresh(idArgument));
        command.Subcommands.Add(BuildRemove(idArgument));

        return command;
    }

    private Command BuildList()
    {
        var command = new Command("list", "Shows the configured sources and which database holds them.");

        command.SetAction((_, cancellationToken) => CommandRunner.RunAsync(() =>
            _catalogue.RunAsync<SourcesCommandHandler>(handler => handler.ListAsync(cancellationToken))));

        return command;
    }

    private Command BuildAddPlaylist()
    {
        var addressArgument = new Argument<string>("address")
        {
            Description = "Playlist URL, or the full path to a local .m3u file.",
        };

        var nameOption = new Option<string?>("--name")
        {
            Description = "Display name for the source. Defaults to the host or file name.",
        };

        var command = new Command("add-playlist", "Adds an M3U playlist and imports its catalogue.");
        command.Arguments.Add(addressArgument);
        command.Options.Add(nameOption);

        command.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(() =>
            _catalogue.RunAsync<SourcesCommandHandler>(handler => handler.AddPlaylistAsync(
                parseResult.GetValue(addressArgument) ?? string.Empty,
                parseResult.GetValue(nameOption),
                cancellationToken))));

        return command;
    }

    private Command BuildRefresh(Argument<int> idArgument)
    {
        var command = new Command("refresh", "Re-imports a stored source's channels, films and series.");
        command.Arguments.Add(idArgument);

        command.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(() =>
            _catalogue.RunAsync<SourcesCommandHandler>(handler => handler.RefreshAsync(
                parseResult.GetValue(idArgument),
                cancellationToken))));

        return command;
    }

    private Command BuildRemove(Argument<int> idArgument)
    {
        var command = new Command("remove", "Removes a source together with its catalogue.");
        command.Arguments.Add(idArgument);

        command.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(() =>
            _catalogue.RunAsync<SourcesCommandHandler>(handler => handler.RemoveAsync(
                parseResult.GetValue(idArgument),
                cancellationToken))));

        return command;
    }
}
