namespace LTR.Catalogue;

/// <summary>
/// The steps a guide import passes through.
/// </summary>
/// <remarks>
/// Stages rather than text, for the same reason as <see cref="SourceImportStage"/>: the wording belongs
/// to whoever shows it.
/// </remarks>
public enum GuideImportStage
{
    /// <summary>Establishing where the guide is and opening it.</summary>
    Locating = 0,

    /// <summary>Reading the document and writing programmes as they arrive.</summary>
    Reading = 1,

    /// <summary>Working out which guide channel belongs to which channel.</summary>
    Matching = 2,

    /// <summary>Discarding programmes that have already ended.</summary>
    Pruning = 3,
}
