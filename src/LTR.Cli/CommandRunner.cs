using LTR.Playback;
using LTR.Providers;
using LTR.Providers.Xtream;

namespace LTR.Cli;

/// <summary>
/// Turns the failures a panel or a stream can produce into readable messages and exit codes.
/// </summary>
/// <remarks>
/// An offline channel, an expired subscription or a panel serving an HTML error page are all normal
/// outcomes when talking to IPTV providers. Letting them reach the runtime as unhandled exceptions
/// would present routine conditions as crashes and bury the actual cause in a stack trace.
/// </remarks>
internal static class CommandRunner
{
    private const int Failure = 1;

    public static async Task<int> RunAsync(Func<Task<int>> command)
    {
        try
        {
            return await command().ConfigureAwait(false);
        }
        catch (ProviderRequestException exception)
        {
            // A panel is named as one, because that is what the user configured; anything else is a
            // provider, since a playlist has no better word for it.
            var subject = exception is XtreamApiException ? "Panel" : "Provider";
            Console.Error.WriteLine($"{subject} error: {exception.Message}");

            if (exception.SanitizedUrl is not null)
            {
                Console.Error.WriteLine($"Address: {exception.SanitizedUrl}");
            }

            return Failure;
        }
        catch (PlaybackFailedException exception)
        {
            Console.Error.WriteLine($"Playback error: {exception.Message}");
            return Failure;
        }
        catch (HttpRequestException exception)
        {
            Console.Error.WriteLine($"Network error: {exception.Message}");
            return Failure;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Cancelled.");
            return Failure;
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine($"Invalid argument: {exception.Message}");
            return Failure;
        }
    }
}
