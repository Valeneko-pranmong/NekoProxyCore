namespace NekoProxyCore.Core;

public enum ProxyErrorCode
{
    InvalidConfiguration,
    AuthorizationRequired,
    AuthorizationInvalid,
    AuthorizationExpired,
    AuthorizationReplay,
    AuthorizationUnavailable,
    SessionInactive,
    AlreadyRunning,
    NotRunning,
    UnsupportedMode,
    ProcessNotFound,
    ProcessExited,
    StartFailed,
    StopFailed,
    Timeout,
    Cancelled
}
