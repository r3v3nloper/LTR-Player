namespace LTR.Epg.Xmltv;

/// <summary>
/// A <c>&lt;channel&gt;</c> declaration from an XMLTV document.
/// </summary>
/// <param name="Id">The <c>id</c> attribute, which programmes reference.</param>
/// <param name="DisplayName">
/// The first display name given. XMLTV permits one per language and providers use that freely; the
/// first is taken because a guide lists its own preferred spelling first, and matching needs one name
/// rather than the best name.
/// </param>
/// <param name="IconUrl">Channel logo, when the guide states one.</param>
public sealed record XmltvChannel(string Id, string? DisplayName, string? IconUrl);
