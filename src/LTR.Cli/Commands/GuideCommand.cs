using System.CommandLine;

namespace LTR.Cli.Commands;

/// <summary>
/// <c>guide</c> — imports and inspects a stored source's programme guide.
/// </summary>
internal sealed class GuideCommand
{
    private readonly CatalogueCommandRunner _catalogue;

    public GuideCommand(CatalogueCommandRunner catalogue)
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

        var command = new Command("guide", "Imports and inspects a stored source's programme guide.");
        command.Subcommands.Add(BuildImport(sourceIdOption));
        command.Subcommands.Add(BuildShow(sourceIdOption));

        return command;
    }

    private Command BuildImport(Option<int> sourceIdOption)
    {
        var forceOption = new Option<bool>("--force")
        {
            Description = "Fetch the guide even when the stored one is still fresh.",
        };

        var command = new Command(
            "import",
            "Downloads the source's XMLTV guide, stores it and matches it to the channel list.");

        command.Options.Add(sourceIdOption);
        command.Options.Add(forceOption);

        command.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(() =>
            _catalogue.RunAsync<GuideCommandHandler>(handler => handler.ImportAsync(
                parseResult.GetValue(sourceIdOption),
                parseResult.GetValue(forceOption),
                cancellationToken))));

        return command;
    }

    private Command BuildShow(Option<int> sourceIdOption)
    {
        var command = new Command("show", "Reports the stored guide's coverage and match rate.");
        command.Options.Add(sourceIdOption);

        command.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(() =>
            _catalogue.RunAsync<GuideCommandHandler>(handler => handler.ShowAsync(
                parseResult.GetValue(sourceIdOption),
                cancellationToken))));

        return command;
    }
}
