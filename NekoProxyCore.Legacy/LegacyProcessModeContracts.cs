using NekoProxyCore.Core;

namespace NekoProxyCore.Legacy;

/// <summary>
/// Resolves the Core's opaque identifiers into a private legacy runtime session.
/// Implementations must keep any credential-bearing legacy configuration inside their assembly.
/// </summary>
public interface ILegacyProcessModeSessionResolver
{
    Task<ILegacyProcessModeSession> ResolveAsync(
        ProxyConfiguration configuration,
        IProxyStatusSink statusSink,
        CancellationToken cancellationToken);
}

/// <summary>
/// A single legacy ProcessMode lifecycle. It deliberately exposes no server, profile, URI,
/// credential, or native-driver details to NekoProxyCore.Core.
/// </summary>
public interface ILegacyProcessModeSession
{
    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}
