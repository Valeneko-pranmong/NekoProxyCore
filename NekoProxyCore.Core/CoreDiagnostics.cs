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
    RuntimeAuthorizationUnavailable,
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
            CoreDiagnosticCategory.RuntimeAuthorizationUnavailable => "RUNTIME_AUTHORIZATION_UNAVAILABLE",
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
