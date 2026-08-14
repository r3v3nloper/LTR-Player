using LTR.Catalogue;
using LTR.Core.Content;
using LTR.Core.Sources;

namespace LTR.Player.Wpf;

/// <summary>
/// Answers detail requests from what a test put in it, and records what was asked for.
/// </summary>
/// <remarks>
/// The real service decides whether to call the panel; that decision is covered against a real database in
/// the catalogue tests. What the window cares about is which item it asked about and whether a slow answer
/// can overwrite a newer selection, so this fake can be made to wait.
/// </remarks>
internal sealed class FakeVodDetailService : IVodDetailService
{
    private readonly List<string> _requests = [];

    public List<VodItem> Movies { get; } = [];

    public List<Series> Series { get; } = [];

    public IReadOnlyList<string> Requests => _requests;

    /// <summary>
    /// When set, every request waits on it, so a test can hold an answer back and change the selection
    /// underneath it.
    /// </summary>
    public TaskCompletionSource? Gate { get; set; }

    public async Task<Series?> GetSeriesAsync(
        PlaylistSource source,
        int seriesId,
        CancellationToken cancellationToken)
    {
        _requests.Add($"series:{seriesId}");
        await WaitAsync(cancellationToken).ConfigureAwait(false);

        return Series.FirstOrDefault(series => series.Id == seriesId);
    }

    public async Task<VodItem?> GetMovieAsync(
        PlaylistSource source,
        int movieId,
        CancellationToken cancellationToken)
    {
        _requests.Add($"movie:{movieId}");
        await WaitAsync(cancellationToken).ConfigureAwait(false);

        return Movies.FirstOrDefault(movie => movie.Id == movieId);
    }

    private async Task WaitAsync(CancellationToken cancellationToken)
    {
        if (Gate is { } gate)
        {
            await gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
