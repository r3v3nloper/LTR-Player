using System.Runtime.Versioning;
using LTR.Core;
using LTR.Core.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace LTR.Security.Dpapi;

/// <summary>
/// Registers credential protection.
/// </summary>
/// <remarks>
/// The platform decision lives here rather than in each application, for two reasons. Both applications
/// share one database, so registering different protectors would make them read the same rows
/// differently — this way there is one call and one answer. And the platform check belongs next to the
/// platform-specific implementation rather than duplicated at every composition root.
/// </remarks>
public static class CredentialProtectionServiceCollectionExtensions
{
    /// <summary>
    /// Uses the Windows Data Protection API where available, and stores credentials verbatim elsewhere.
    /// </summary>
    /// <remarks>
    /// The fallback is deliberate rather than a failure: the core is meant to stay usable off Windows
    /// for the planned web frontend, and refusing to start there would be worse than the weaker
    /// protection it already had.
    /// </remarks>
    public static IServiceCollection AddCredentialProtection(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (OperatingSystem.IsWindows())
        {
            AddWindowsDataProtection(services);
            return services;
        }

        services.TryAddSingleton<ICredentialProtector, PassThroughCredentialProtector>();
        return services;
    }

    /// <summary>
    /// A separate, platform-annotated method rather than a lambda guarded by an inline platform check.
    /// The analyser is right to reject the latter: a registration lambda runs when the service is first
    /// resolved, which is not inside the guard that surrounded its declaration.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static void AddWindowsDataProtection(IServiceCollection services)
    {
        services.TryAddSingleton<ICredentialProtector>(provider =>
            new DpapiCredentialProtector(
                provider.GetRequiredService<ILogger<DpapiCredentialProtector>>()));
    }
}
