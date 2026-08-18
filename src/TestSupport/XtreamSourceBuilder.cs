using LTR.Core.Content;
using LTR.Core.Sources;

namespace LTR.TestSupport;

/// <summary>
/// Builds <see cref="XtreamSource"/> instances for tests, so each test states only the properties
/// it actually cares about.
/// </summary>
/// <remarks>
/// Shared rather than per project since the review after the pinned categories: eleven test classes had
/// written this object out by hand, and a field added to a source meant eleven edits — of which the
/// forgotten one fails as a test that was never asking about the field.
/// </remarks>
internal sealed class XtreamSourceBuilder
{
    private int _id;
    private string _name = "Test source";
    private string _baseUrl = "http://panel.example:8080";
    private string _username = "user";
    private string _password = "pass";
    private string _userAgent = "TestAgent/1.0";
    private StreamFormat _preferredFormat = StreamFormat.MpegTs;
    private ProviderCapabilities _capabilities = new();
    private DateTimeOffset _createdUtc = DateTimeOffset.UnixEpoch;

    /// <summary>
    /// The local identity, which a test needs whenever a store is asked about the source by it.
    /// </summary>
    /// <remarks>
    /// Left at zero by default, as an unsaved entity is. The provider-facing tests never ask.
    /// </remarks>
    public XtreamSourceBuilder WithId(int id)
    {
        _id = id;
        return this;
    }

    public XtreamSourceBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    public XtreamSourceBuilder WithCreatedUtc(DateTimeOffset createdUtc)
    {
        _createdUtc = createdUtc;
        return this;
    }

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
            Id = _id,
            Name = _name,
            CreatedUtc = _createdUtc,
            BaseUrl = new Uri(_baseUrl, UriKind.Absolute),
            Username = _username,
            Password = _password,
            UserAgent = _userAgent,
            PreferredStreamFormat = _preferredFormat,
            Capabilities = _capabilities,
        };
    }
}
