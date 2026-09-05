namespace NekoProxyCore.Core;

public enum ProxyErrorCode
{
    InvalidConfiguration = 0,
    AuthorizationRequired = 1,
    AuthorizationInvalid = 2,
    AuthorizationExpired = 3,
    AuthorizationReplay = 4,
    AuthorizationUnavailable = 5,
    SessionInactive = 6,
    AlreadyRunning = 7,
    NotRunning = 8,
    UnsupportedMode = 9,
    ProcessNotFound = 10,
    ProcessExited = 11,
    StartFailed = 12,
    StopFailed = 13,
    Timeout = 14,
    Cancelled = 15,
    EntitlementInactive = 16,
    HeartbeatStale = 17,
    ConfigurationMismatch = 18,
    ProtocolInvalid = 19,
    StartTimeout = 20
}
