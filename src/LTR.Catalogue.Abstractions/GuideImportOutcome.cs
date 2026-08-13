namespace LTR.Catalogue;

/// <summary>
/// How a guide import ended.
/// </summary>
/// <remarks>
/// Distinct outcomes rather than a boolean, because they call for different words on screen: "this
/// subscription has no guide" is a fact the user cannot act on, "the guide was empty" suggests the
/// address is wrong, and "not due" is not a result at all but an explanation for why nothing happened.
/// </remarks>
public enum GuideImportOutcome
{
    Imported = 0,

    NoGuideAvailable = 1,

    Empty = 2,

    /// <summary>The stored guide was recent enough that an automatic import was skipped.</summary>
    NotDue = 3,
}
