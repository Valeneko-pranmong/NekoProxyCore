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

/// <summary>
/// Verifies authorization material only after the protocol host atomically admitted the request
/// and consumed its one-use challenge.
/// </summary>
public sealed class ChallengePermitStartAuthorizer : IProxyStartAuthorizer
{
    private readonly IPermitVerifier _permitVerifier;
    private readonly ICoreDiagnosticSink _diagnostics;

    public ChallengePermitStartAuthorizer(IPermitVerifier permitVerifier)
        : this(permitVerifier, null)
    {
    }

    public ChallengePermitStartAuthorizer(
        IPermitVerifier permitVerifier,
        ICoreDiagnosticSink? diagnostics)
    {
        _permitVerifier = permitVerifier ?? throw new ArgumentNullException(nameof(permitVerifier));
        _diagnostics = diagnostics ?? NullCoreDiagnosticSink.Instance;
    }

    public async Task<ProxyError?> AuthorizeAsync(ProxyStartRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Permit is null || string.IsNullOrEmpty(request.AdmittedChallenge))
        {
            return new ProxyError(
                ProxyErrorCode.AuthorizationRequired, "Online authorization is required.");
        }

        try
        {
            return await _permitVerifier.VerifyAsync(
                    request.Permit,
                    request.AdmittedChallenge,
                    request.CancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (request.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            CoreDiagnosticReporter.ReportSafely(
                _diagnostics,
                CoreDiagnosticStage.Authorization,
                CoreDiagnosticCategory.AuthorizerException);
            return new ProxyError(
                ProxyErrorCode.AuthorizationUnavailable, "Online authorization is unavailable.");
        }
    }
}
