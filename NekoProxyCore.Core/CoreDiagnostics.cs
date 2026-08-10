namespace NekoProxyCore.Core;

/// <summary>Allow-listed stages for opt-in, sanitized Core diagnostics.</summary>
public enum CoreDiagnosticStage
{
    Authorization,
    PermitParse,
    KeyResolve,
    SignatureVerify,
    ClaimsValidate,
    ClockValidate,
    ConfigurationDigestValidate,
    TargetChallengeBind,
    JtiConsume,
    ProcessPrecondition,
    RuntimeTargetRecheck,
    RuntimeTargetPostcheck,
    SessionResolve,
    EngineStart,
    EngineCleanup,
    RuntimeCleanup,
    RuntimeStart,
    ControlResponse
}

/// <summary>Allow-listed outcomes for opt-in, sanitized Core diagnostics.</summary>
public enum CoreDiagnosticCategory
{
    StageCompleted,
    AuthKeyResolveException,
    AuthKeyTypeUnavailable,
    AuthClockUntrusted,
    AuthClockRollback,
    AuthClockException,
    AuthVerifierUnexpectedException,
    AuthorizerException,
    ProcessExactResolverUnavailable,
    RuntimeTargetProcessNotFound,
    RuntimeTargetProcessExited,
    RuntimeTargetVerificationUnavailable,
    RuntimeTargetUnsupportedMode,
    RuntimeTargetCancelled,
    RuntimeTargetUnexpectedException,
    SessionProfileReferenceInvalid,
    SessionServerReferenceInvalid,
    SessionProfileNotFound,
    SessionServerNotFound,
    SessionProfileServerMismatch,
    SessionModeNotFound,
    SessionModeAmbiguous,
    EngineStartEntered,
    EngineStartProxyError,
    EngineStartUnexpectedException,
    EngineStartCancelled,
    EngineCleanupCompleted,
    EngineCleanupFailure,
    RuntimeCleanupCompleted,
    RuntimeCleanupFailure,
    RuntimeStartProxyError,
    RuntimeStartUnexpectedException,
    RuntimeStartCancelled,
    RuntimeStartTimeout,
    RuntimeAuthorizationUnavailable,
    RuntimeInvalidConfigurationMappedToConfigurationMismatch,
    ControlErrorTranslatedToAuthorizationUnavailable,
    ControlFailedStatusErrorMissing
}

public sealed record CoreDiagnosticEvent(
    CoreDiagnosticStage Stage,
    CoreDiagnosticCategory Category);

public interface ICoreDiagnosticSink
{
    void Report(CoreDiagnosticEvent diagnosticEvent);
}

public sealed class NullCoreDiagnosticSink : ICoreDiagnosticSink
{
    public static readonly NullCoreDiagnosticSink Instance = new();

    private NullCoreDiagnosticSink()
    {
    }

    public void Report(CoreDiagnosticEvent diagnosticEvent)
    {
    }
}

/// <summary>
/// Emits only fixed tokens derived from closed enums. It has no API for permit, claim,
/// process, key, exception-message, or stack-trace data.
/// </summary>
public sealed class SanitizedTextCoreDiagnosticSink : ICoreDiagnosticSink
{
    private readonly TextWriter _writer;
    private readonly object _gate = new();

    public SanitizedTextCoreDiagnosticSink(TextWriter writer) =>
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));

    public void Report(CoreDiagnosticEvent diagnosticEvent)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);
        if (!TryGetStageToken(diagnosticEvent.Stage, out var stage) ||
            !TryGetCategoryToken(diagnosticEvent.Category, out var category))
        {
            return;
        }

        lock (_gate)
            _writer.WriteLine($"NEKO_CORE_DIAGNOSTIC stage={stage} category={category}");
    }

    private static bool TryGetStageToken(CoreDiagnosticStage stage, out string token)
    {
        token = stage switch
        {
            CoreDiagnosticStage.Authorization => "AUTHORIZATION",
            CoreDiagnosticStage.PermitParse => "PERMIT_PARSE",
            CoreDiagnosticStage.KeyResolve => "KEY_RESOLVE",
            CoreDiagnosticStage.SignatureVerify => "SIGNATURE_VERIFY",
            CoreDiagnosticStage.ClaimsValidate => "CLAIMS_VALIDATE",
            CoreDiagnosticStage.ClockValidate => "CLOCK_VALIDATE",
            CoreDiagnosticStage.ConfigurationDigestValidate => "CONFIG_DIGEST_VALIDATE",
            CoreDiagnosticStage.TargetChallengeBind => "TARGET_CHALLENGE_BIND",
            CoreDiagnosticStage.JtiConsume => "JTI_CONSUME",
            CoreDiagnosticStage.ProcessPrecondition => "PROCESS_PRECONDITION",
            CoreDiagnosticStage.RuntimeTargetRecheck => "RUNTIME_TARGET_RECHECK",
            CoreDiagnosticStage.RuntimeTargetPostcheck => "RUNTIME_TARGET_POSTCHECK",
            CoreDiagnosticStage.SessionResolve => "SESSION_RESOLVE",
            CoreDiagnosticStage.EngineStart => "ENGINE_START",
            CoreDiagnosticStage.EngineCleanup => "ENGINE_CLEANUP",
            CoreDiagnosticStage.RuntimeCleanup => "RUNTIME_CLEANUP",
            CoreDiagnosticStage.RuntimeStart => "RUNTIME_START",
            CoreDiagnosticStage.ControlResponse => "CONTROL_RESPONSE",
            _ => string.Empty
        };
        return token.Length != 0;
    }

    private static bool TryGetCategoryToken(CoreDiagnosticCategory category, out string token)
    {
        token = category switch
        {
            CoreDiagnosticCategory.StageCompleted => "STAGE_COMPLETED",
            CoreDiagnosticCategory.AuthKeyResolveException => "AUTH_KEY_RESOLVE_EXCEPTION",
            CoreDiagnosticCategory.AuthKeyTypeUnavailable => "AUTH_KEY_TYPE_UNAVAILABLE",
            CoreDiagnosticCategory.AuthClockUntrusted => "AUTH_CLOCK_UNTRUSTED",
            CoreDiagnosticCategory.AuthClockRollback => "AUTH_CLOCK_ROLLBACK",
            CoreDiagnosticCategory.AuthClockException => "AUTH_CLOCK_EXCEPTION",
            CoreDiagnosticCategory.AuthVerifierUnexpectedException => "AUTH_VERIFIER_UNEXPECTED_EXCEPTION",
            CoreDiagnosticCategory.AuthorizerException => "AUTHORIZER_EXCEPTION",
            CoreDiagnosticCategory.ProcessExactResolverUnavailable => "PROCESS_EXACT_RESOLVER_UNAVAILABLE",
            CoreDiagnosticCategory.RuntimeTargetProcessNotFound => "RUNTIME_TARGET_PROCESS_NOT_FOUND",
            CoreDiagnosticCategory.RuntimeTargetProcessExited => "RUNTIME_TARGET_PROCESS_EXITED",
            CoreDiagnosticCategory.RuntimeTargetVerificationUnavailable =>
                "RUNTIME_TARGET_VERIFICATION_UNAVAILABLE",
            CoreDiagnosticCategory.RuntimeTargetUnsupportedMode => "RUNTIME_TARGET_UNSUPPORTED_MODE",
            CoreDiagnosticCategory.RuntimeTargetCancelled => "RUNTIME_TARGET_CANCELLED",
            CoreDiagnosticCategory.RuntimeTargetUnexpectedException =>
                "RUNTIME_TARGET_UNEXPECTED_EXCEPTION",
            CoreDiagnosticCategory.SessionProfileReferenceInvalid => "SESSION_PROFILE_REFERENCE_INVALID",
            CoreDiagnosticCategory.SessionServerReferenceInvalid => "SESSION_SERVER_REFERENCE_INVALID",
            CoreDiagnosticCategory.SessionProfileNotFound => "SESSION_PROFILE_NOT_FOUND",
            CoreDiagnosticCategory.SessionServerNotFound => "SESSION_SERVER_NOT_FOUND",
            CoreDiagnosticCategory.SessionProfileServerMismatch => "SESSION_PROFILE_SERVER_MISMATCH",
            CoreDiagnosticCategory.SessionModeNotFound => "SESSION_MODE_NOT_FOUND",
            CoreDiagnosticCategory.SessionModeAmbiguous => "SESSION_MODE_AMBIGUOUS",
            CoreDiagnosticCategory.EngineStartEntered => "ENGINE_START_ENTERED",
            CoreDiagnosticCategory.EngineStartProxyError => "ENGINE_START_PROXY_ERROR",
            CoreDiagnosticCategory.EngineStartUnexpectedException => "ENGINE_START_UNEXPECTED_EXCEPTION",
            CoreDiagnosticCategory.EngineStartCancelled => "ENGINE_START_CANCELLED",
            CoreDiagnosticCategory.EngineCleanupCompleted => "ENGINE_CLEANUP_COMPLETED",
            CoreDiagnosticCategory.EngineCleanupFailure => "ENGINE_CLEANUP_FAILURE",
            CoreDiagnosticCategory.RuntimeCleanupCompleted => "RUNTIME_CLEANUP_COMPLETED",
            CoreDiagnosticCategory.RuntimeCleanupFailure => "RUNTIME_CLEANUP_FAILURE",
            CoreDiagnosticCategory.RuntimeStartProxyError => "RUNTIME_START_PROXY_ERROR",
            CoreDiagnosticCategory.RuntimeStartUnexpectedException => "RUNTIME_START_UNEXPECTED_EXCEPTION",
            CoreDiagnosticCategory.RuntimeStartCancelled => "RUNTIME_START_CANCELLED",
            CoreDiagnosticCategory.RuntimeStartTimeout => "RUNTIME_START_TIMEOUT",
            CoreDiagnosticCategory.RuntimeAuthorizationUnavailable => "RUNTIME_AUTHORIZATION_UNAVAILABLE",
            CoreDiagnosticCategory.RuntimeInvalidConfigurationMappedToConfigurationMismatch =>
                "RUNTIME_INVALID_CONFIGURATION_MAPPED_TO_CONFIGURATION_MISMATCH",
            CoreDiagnosticCategory.ControlErrorTranslatedToAuthorizationUnavailable =>
                "CONTROL_ERROR_TRANSLATED_TO_AUTHORIZATION_UNAVAILABLE",
            CoreDiagnosticCategory.ControlFailedStatusErrorMissing =>
                "CONTROL_FAILED_STATUS_ERROR_MISSING",
            _ => string.Empty
        };
        return token.Length != 0;
    }
}

public static class CoreDiagnosticReporter
{
    public static void ReportSafely(
        ICoreDiagnosticSink sink,
        CoreDiagnosticStage stage,
        CoreDiagnosticCategory category)
    {
        try
        {
            sink.Report(new CoreDiagnosticEvent(stage, category));
        }
        catch
        {
            // Diagnostics are optional presentation only and cannot alter fail-closed behavior.
        }
    }
}
