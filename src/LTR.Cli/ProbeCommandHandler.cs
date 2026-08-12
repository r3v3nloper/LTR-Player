using System.Globalization;
using LTR.Core.Sources;
using LTR.Providers;

namespace LTR.Cli;

/// <summary>
/// Reports what a panel supports and what state the subscription is in.
/// </summary>
internal sealed class ProbeCommandHandler
{
    private readonly IContentProviderFactory _providerFactory;
    private readonly IProviderCapabilityProbe _capabilityProbe;

    public ProbeCommandHandler(
        IContentProviderFactory providerFactory,
        IProviderCapabilityProbe capabilityProbe)
    {
        _providerFactory = providerFactory;
        _capabilityProbe = capabilityProbe;
    }

    public async Task<int> ExecuteAsync(XtreamSource source, CancellationToken cancellationToken)
    {
        var provider = _providerFactory.Create(source);
        var account = await provider.AuthenticateAsync(cancellationToken).ConfigureAwait(false);

        Console.WriteLine($"Panel        {source.BaseUrl}");
        Console.WriteLine($"Status       {account.Status}");
        Console.WriteLine($"Expires      {Describe(account.ExpiresAtUtc)}");
        Console.WriteLine($"Trial        {(account.IsTrial ? "yes" : "no")}");
        Console.WriteLine($"Connections  {account.ActiveConnections} of {DescribeLimit(account.MaxConnections)} in use");
        Console.WriteLine($"Formats      {DescribeFormats(account)}");

        if (!account.IsUsable)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"The subscription is not usable ({account.Status}).");
            return 1;
        }

        // Worth flagging loudly: a non-zero count while nothing is playing means an earlier session
        // leaked a connection, and the next stream may be refused because of it.
        if (account.ActiveConnections > 0)
        {
            Console.WriteLine();
            Console.WriteLine(
                $"Note: the panel already counts {account.ActiveConnections} open connection(s). "
                + "If nothing is playing, a previous session did not close cleanly.");
        }

        var capabilities = await _capabilityProbe.ProbeAsync(source, cancellationToken).ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine("Capabilities");
        WriteCapability("live", capabilities.SupportsLive);
        WriteCapability("vod", capabilities.SupportsVod);
        WriteCapability("series", capabilities.SupportsSeries);
        WriteCapability("xmltv guide", capabilities.SupportsXmltvEpg);
        WriteCapability("short epg", capabilities.SupportsShortEpg);
        WriteCapability("mpeg-ts", capabilities.SupportsMpegTs);
        WriteCapability("hls", capabilities.SupportsHls);

        return 0;
    }

    private static void WriteCapability(string name, bool isSupported)
    {
        Console.WriteLine($"  {(isSupported ? "yes" : " no")}  {name}");
    }

    private static string Describe(DateTimeOffset? expiresAtUtc)
    {
        return expiresAtUtc is null
            ? "never"
            : expiresAtUtc.Value.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
    }

    private static string DescribeLimit(int maxConnections)
    {
        return maxConnections <= 0
            ? "an unreported number of"
            : maxConnections.ToString(CultureInfo.InvariantCulture);
    }

    private static string DescribeFormats(ProviderAccount account)
    {
        return account.AllowedFormats.Count == 0
            ? "none reported"
            : string.Join(", ", account.AllowedFormats);
    }
}
