using NekoProxyCore.Core;

namespace NekoProxyCore.Host.Protocol;

public enum ControlCommand
{
    Start = 0,
    Status = 1,
    Stop = 2,
    Challenge = 3,
    Shutdown = 4,
    RuntimeConfigCatalog = 5,
    RuntimeConfigValidate = 6
}

public sealed class ControlRequest
{
    internal ControlRequest(
        ControlCommand command,
        string correlationId,
        string? processName,
        uint? targetPid,
        string? profileReference,
        string? serverReference,
        SensitivePermit? permit,
        string? admittedChallenge)
    {
        Command = command;
        CorrelationId = correlationId;
        ProcessName = processName;
        TargetPid = targetPid;
        ProfileReference = profileReference;
        ServerReference = serverReference;
        Permit = permit;
        AdmittedChallenge = admittedChallenge;
    }

    public ControlCommand Command { get; }
    public string CorrelationId { get; }
    public string? ProcessName { get; }
    public uint? TargetPid { get; }
    public string? ProfileReference { get; }
    public string? ServerReference { get; }
    public SensitivePermit? Permit { get; }
    internal string? AdmittedChallenge { get; }

    public bool TryCreateStartRequest(out ProxyStartRequest? request, out ControlResponse? error)
    {
        request = null;
        error = null;
        if (Command != ControlCommand.Start ||
            TargetPid is null ||
            Permit is null ||
            string.IsNullOrEmpty(AdmittedChallenge) ||
            !ProxyConfiguration.TryCreate(
                ProxyModeKind.Process,
                ProcessName!,
                ProfileReference!,
                ServerReference!,
                null,
                null,
                out var configuration,
                out _,
                TargetPid))
        {
            error = ControlResponse.ProtocolInvalid(CorrelationId);
            return false;
        }

        request = new ProxyStartRequest(
            configuration!,
            CorrelationId,
            permit: Permit,
            admittedChallenge: AdmittedChallenge);
        return true;
    }
}
