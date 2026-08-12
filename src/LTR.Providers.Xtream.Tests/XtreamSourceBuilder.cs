using LTR.Core.Content;
using LTR.Core.Sources;

namespace LTR.Providers.Xtream;

/// <summary>
/// Builds <see cref="XtreamSource"/> instances for tests, so each test states only the properties
/// it actually cares about.
/// </summary>
internal sealed class XtreamSourceBuilder
{
    private string _baseUrl = "http://panel.example:8080";
    private string _username = "user";
    private string _password = "pass";
    private string _userAgent = "TestAgent/1.0";
    private StreamFormat _preferredFormat = StreamFormat.MpegTs;
    private ProviderCapabilities _capabilities = new();

    public XtreamSourceBuilder WithBaseUrl(string baseUrl)
    {
        _baseUrl = baseUrl;
        return this;
    }

    public XtreamSourceBuilder WithCredentials(string username, string password)
    {
        _username = username;
        _password = password;
        return this;
    }

    public XtreamSourceBuilder WithUserAgent(string userAgent)
    {
        _userAgent = userAgent;
        return this;
    }

    public XtreamSourceBuilder WithPreferredFormat(StreamFormat format)
    {
        _preferredFormat = format;
        return this;
    }

    public XtreamSourceBuilder WithCapabilities(ProviderCapabilities capabilities)
    {
        _capabilities = capabilities;
        return this;
    }

    public XtreamSource Build()
    {
        return new XtreamSource
        {
            Id = 1,
            Name = "Test source",
            BaseUrl = new Uri(_baseUrl, UriKind.Absolute),
            Username = _username,
            Password = _password,
            UserAgent = _userAgent,
            PreferredStreamFormat = _preferredFormat,
            Capabilities = _capabilities,
        };
    }
}
