namespace NekoProxyCore.Core;

/// <summary>
/// Authorizes a single runtime start attempt before any mode or engine side effects occur.
/// Production implementations must validate fresh server-issued authorization material.
/// </summary>
public interface IProxyStartAuthorizer
{
    Task<ProxyError?> AuthorizeAsync(ProxyStartRequest request);
}

/// <summary>
/// Fail-closed authorizer for entry points that do not yet supply server-verifiable authorization.
/// </summary>
public sealed class AuthorizationRequiredStartAuthorizer : IProxyStartAuthorizer
{
    public Task<ProxyError?> AuthorizeAsync(ProxyStartRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        return Task.FromResult<ProxyError?>(
            new ProxyError(ProxyErrorCode.AuthorizationRequired, "Online authorization is required."));
    }
}
