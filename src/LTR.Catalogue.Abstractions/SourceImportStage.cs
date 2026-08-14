namespace LTR.Catalogue;

/// <summary>
/// The steps an import passes through, reported so a caller can say what is happening.
/// </summary>
/// <remarks>
/// Stages rather than text, because the wording belongs to whoever is showing it: a window writes a
/// sentence into a status line, a command line tool prints a terse line, and neither belongs in a
/// service.
/// </remarks>
public enum SourceImportStage
{
    /// <summary>Checking the credentials and the subscription's state.</summary>
    Authenticating = 0,

    /// <summary>Establishing what the source supports.</summary>
    Probing = 1,

    /// <summary>Downloading categories and channels.</summary>
    FetchingCatalogue = 2,

    /// <summary>
    /// Downloading the film and series catalogues, which is reported separately because it is the longest
    /// step of an import on a subscription that offers them.
    /// </summary>
    FetchingVod = 3,

    /// <summary>Reconciling what was fetched against what is stored.</summary>
    Storing = 4,
}
