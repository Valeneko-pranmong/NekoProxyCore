using NekoProxyCore.Core;

namespace NekoProxyCore.Host.Protocol;

public enum ControlCommand
{
    Start,
    Status,
    Stop
}

public sealed class ControlRequest
{
    internal ControlRequest(
        ControlCommand command,
        string correlationId,
        string? processName,
        string? profileReference,
        string? serverReference)
    {
        Command = command;
        CorrelationId = correlationId;
        ProcessName = processName;
        ProfileReference = profileReference;
        ServerReference = serverReference;
    }

    public ControlCommand Command { get; }

    public string CorrelationId { get; }

    public string? ProcessName { get; }

    public string? ProfileReference { get; }

    public string? ServerReference { get; }

    public bool TryCreateStartRequest(out ProxyStartRequest? request, out ControlResponse? error)
    {
        request = null;
        error = null;
        if (Command != ControlCommand.Start ||
            !ProxyConfiguration.TryCreate(
                ProxyModeKind.Process,
                ProcessName!,
                ProfileReference!,
                ServerReference!,
                null,
                null,
                out var configuration,
                out _))
        {
            error = ControlResponse.InvalidConfiguration(CorrelationId);
            return false;
        }

        request = new ProxyStartRequest(configuration!, CorrelationId);
        return true;
    }
}
