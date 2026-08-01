namespace NekoProxyCore.Core;

public enum ProxyErrorCode
{
    InvalidConfiguration,
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
