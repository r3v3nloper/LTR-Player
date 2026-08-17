using LTR.Core.Sources;
using LTR.Providers;

namespace LTR.TestSupport;

/// <summary>
/// A provider registry that refuses everything, so a test double states only what it answers.
/// </summary>
/// <remarks>
/// <para>
/// Every double of <see cref="IProviderRegistry"/> answers one or two of its members and has to declare all
/// of them; each member added to the interface cost an edit in each double, twice over, for a body that only
/// throws. Overriding what a test actually needs is the whole of what those doubles are about.
/// </para>
/// <para>
/// The refusal names the double and the member, which is what a failing test needs to read: it means the code
/// under test reached for a component the double was never given, and that is a fact about the test rather
/// than about the registry. Why a particular double answers what it does belongs on the double itself.
/// </para>
/// <para>
/// Shared by linking this file, as <see cref="TestClock"/> is. A project of its own would be more ceremony
/// than one class deserves, and a copy per project is how two copies drift apart.
/// </para>
/// </remarks>
internal abstract class NotSupportedProviderRegistry : IProviderRegistry
{
    public virtual IContentProvider CreateProvider(PlaylistSource source)
    {
        throw Refuse(nameof(CreateProvider));
    }

    public virtual IProviderCapabilityProbe GetCapabilityProbe(PlaylistSource source)
    {
        throw Refuse(nameof(GetCapabilityProbe));
    }

    public virtual IStreamUrlResolver GetStreamUrlResolver(PlaylistSource source)
    {
        throw Refuse(nameof(GetStreamUrlResolver));
    }

    public virtual IGuideSource GetGuideSource(PlaylistSource source)
    {
        throw Refuse(nameof(GetGuideSource));
    }

    public virtual ISensitiveUrlSanitizer GetUrlSanitizer(PlaylistSource source)
    {
        throw Refuse(nameof(GetUrlSanitizer));
    }

    private NotSupportedException Refuse(string member)
    {
        return new NotSupportedException(
            $"{GetType().Name} does not answer {member}; nothing it stands in for asks the registry for it.");
    }
}
