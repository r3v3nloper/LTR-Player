using System.CommandLine;

namespace LTR.Cli.Commands;

/// <summary>
/// <c>vod</c> — inspects and plays a stored source's films and series.
/// </summary>
/// <remarks>
/// Every subcommand here works against a source already in the catalogue rather than against a panel
/// address, which is why they all take <c>--source-id</c> and none of them takes credentials.
/// </remarks>
internal sealed class VodCommand
{
    private readonly CatalogueCommandRunner _catalogue;

    public VodCommand(CatalogueCommandRunner catalogue)
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

        var command = new Command("vod", "Inspects and plays a stored source's films and series.");

        command.Subcommands.Add(BuildFilmList(sourceIdOption));
        command.Subcommands.Add(BuildSeriesList(sourceIdOption));
        command.Subcommands.Add(BuildEpisodes(sourceIdOption));
        command.Subcommands.Add(BuildShow(sourceIdOption));
        command.Subcommands.Add(BuildContinue(sourceIdOption));
        command.Subcommands.Add(BuildForget(sourceIdOption));
        command.Subcommands.Add(BuildPlayTest(sourceIdOption));

        return command;
    }

    private Command BuildFilmList(Option<int> sourceIdOption)
    {
        var (filterOption, limitOption) = ListingOptions("films");

        var command = new Command("list", "Lists the stored films.");
        command.Options.Add(sourceIdOption);
        command.Options.Add(filterOption);
        command.Options.Add(limitOption);

        command.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(() =>
            _catalogue.RunAsync<VodListingCommandHandler>(handler => handler.ListMoviesAsync(
                parseResult.GetValue(sourceIdOption),
                parseResult.GetValue(filterOption),
                parseResult.GetValue(limitOption),
                cancellationToken))));

        return command;
    }

    private Command BuildSeriesList(Option<int> sourceIdOption)
    {
        var (filterOption, limitOption) = ListingOptions("series");

        var command = new Command("series", "Lists the stored series.");
        command.Options.Add(sourceIdOption);
        command.Options.Add(filterOption);
        command.Options.Add(limitOption);

        command.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(() =>
            _catalogue.RunAsync<VodListingCommandHandler>(handler => handler.ListSeriesAsync(
                parseResult.GetValue(sourceIdOption),
                parseResult.GetValue(filterOption),
                parseResult.GetValue(limitOption),
                cancellationToken))));

        return command;
    }

    private Command BuildEpisodes(Option<int> sourceIdOption)
    {
        var seriesIdOption = new Option<int>("--series-id")
        {
            Description = "Local series id, as shown by 'vod series'. Not the panel's own id.",
            Required = true,
        };

        var command = new Command(
            "episodes",
            "Shows a series' seasons and episodes, fetching them if the stored copy is stale.");

        command.Options.Add(sourceIdOption);
        command.Options.Add(seriesIdOption);

        command.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(() =>
            _catalogue.RunAsync<VodDetailCommandHandler>(handler => handler.ShowSeriesAsync(
                parseResult.GetValue(sourceIdOption),
                parseResult.GetValue(seriesIdOption),
                cancellationToken))));

        return command;
    }

    private Command BuildShow(Option<int> sourceIdOption)
    {
        var movieIdOption = new Option<int>("--movie-id")
        {
            Description = "Local film id, as shown by 'vod list'.",
            Required = true,
        };

        var command = new Command(
            "show",
            "Shows one film's detail, fetching it if it has never been read.");

        command.Options.Add(sourceIdOption);
        command.Options.Add(movieIdOption);

        command.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(() =>
            _catalogue.RunAsync<VodDetailCommandHandler>(handler => handler.ShowMovieAsync(
                parseResult.GetValue(sourceIdOption),
                parseResult.GetValue(movieIdOption),
                cancellationToken))));

        return command;
    }

    private Command BuildContinue(Option<int> sourceIdOption)
    {
        var command = new Command("continue", "Lists what is part-watched, most recent first.");
        command.Options.Add(sourceIdOption);

        command.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(() =>
            _catalogue.RunAsync<WatchProgressCommandHandler>(handler => handler.ContinueWatchingAsync(
                parseResult.GetValue(sourceIdOption),
                cancellationToken))));

        return command;
    }

    private Command BuildForget(Option<int> sourceIdOption)
    {
        var movieIdOption = new Option<int?>("--movie-id") { Description = "Local film id." };
        var episodeIdOption = new Option<int?>("--episode-id") { Description = "Local episode id." };

        var command = new Command(
            "forget",
            "Takes a film or episode off the continue-watching list, keeping it in the catalogue.");

        command.Options.Add(sourceIdOption);
        command.Options.Add(movieIdOption);
        command.Options.Add(episodeIdOption);

        command.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(() =>
            _catalogue.RunAsync<WatchProgressCommandHandler>(handler => handler.ForgetAsync(
                parseResult.GetValue(sourceIdOption),
                parseResult.GetValue(movieIdOption),
                parseResult.GetValue(episodeIdOption),
                cancellationToken))));

        return command;
    }

    private Command BuildPlayTest(Option<int> sourceIdOption)
    {
        // Optional here, unlike in the listing commands, because exactly one of the two must be given and
        // System.CommandLine cannot express that; the handler says so plainly instead.
        var movieIdOption = new Option<int?>("--movie-id")
        {
            Description = "Local film id, as shown by 'vod list'.",
        };

        var episodeIdOption = new Option<int?>("--episode-id")
        {
            Description = "Local episode id, as shown by 'vod episodes'.",
        };

        var secondsOption = new Option<int>("--seconds")
        {
            Description = "How long to hold the stream open.",
            DefaultValueFactory = _ => CommandDefaults.HoldSeconds,
        };

        var startAtOption = new Option<int>("--start-at")
        {
            Description = "Start this many seconds in, which is how resuming is verified.",
        };

        var rememberOption = new Option<bool>("--remember")
        {
            Description = "Record where playback got to, as the window does, so 'vod continue' shows it.",
        };

        // The only way to check the seek bar's own call without the window. --start-at is honoured while the
        // stream opens, which is a different code path from a seek issued against one already playing.
        var seekToOption = new Option<int>("--seek-to")
        {
            Description = "Seek here, in seconds, part-way through the hold, as the seek bar does.",
        };

        var command = new Command(
            "play-test",
            "Opens a stored film or episode headlessly, reports its position, then releases it.");

        command.Options.Add(sourceIdOption);
        command.Options.Add(movieIdOption);
        command.Options.Add(episodeIdOption);
        command.Options.Add(secondsOption);
        command.Options.Add(startAtOption);
        command.Options.Add(seekToOption);
        command.Options.Add(rememberOption);

        command.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(() =>
            _catalogue.RunAsync<VodPlayTestCommandHandler>(handler => handler.ExecuteAsync(
                parseResult.GetValue(sourceIdOption),
                parseResult.GetValue(movieIdOption),
                parseResult.GetValue(episodeIdOption),
                parseResult.GetValue(secondsOption),
                parseResult.GetValue(startAtOption),
                parseResult.GetValue(seekToOption),
                parseResult.GetValue(rememberOption),
                cancellationToken))));

        return command;
    }

    /// <summary>The filter and limit both listings take, worded for what is being listed.</summary>
    private static (Option<string?> Filter, Option<int> Limit) ListingOptions(string subject)
    {
        return (
            new Option<string?>("--filter", "-f")
            {
                Description = $"Show only {subject} whose name contains this text.",
            },
            new Option<int>("--limit")
            {
                Description = $"Maximum number of {subject} to print.",
                DefaultValueFactory = _ => CommandDefaults.Limit,
            });
    }
}
