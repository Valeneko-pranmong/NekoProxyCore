using System.IO.Pipes;
using System.Text;
using NekoProxyCore.Core;
using NekoProxyCore.Host.Protocol;

namespace NekoProxyCore.Host;

internal sealed class HeadlessControlServer
{
    public const string PipeName = ControlProtocol.PipeName;

    private readonly IProxyRuntime _runtime;
    private readonly ICoreChallengeService _challenges;
    private readonly HostShutdownSignal _shutdown;
    private readonly string _pipeName;

    public HeadlessControlServer(
        IProxyRuntime runtime,
        ICoreChallengeService challenges,
        HostShutdownSignal shutdown,
        string pipeName = PipeName)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _challenges = challenges ?? throw new ArgumentNullException(nameof(challenges));
        _shutdown = shutdown ?? throw new ArgumentNullException(nameof(shutdown));
        _pipeName = string.IsNullOrWhiteSpace(pipeName)
            ? throw new ArgumentException("A pipe name is required.", nameof(pipeName))
            : pipeName;
    }

    internal string PipeNameForTesting => _pipeName;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await using var pipe = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                await ServeClientAsync(pipe, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Graceful host shutdown closes the active pipe and completes the normal host loop.
        }
    }

    private async Task ServeClientAsync(Stream stream, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var frame = await ReadFrameAsync(stream, cancellationToken).ConfigureAwait(false);
            if (frame is null)
                return;

            DispatchResult dispatch;
            if (!ControlProtocol.TryParseRequest(frame, _challenges, out var request, out var error))
            {
                dispatch = new DispatchResult(ControlProtocol.Serialize(error!), false);
            }
            else
            {
                dispatch = await DispatchAsync(request!, cancellationToken).ConfigureAwait(false);
            }

            await WriteFrameAsync(stream, dispatch.Response, cancellationToken).ConfigureAwait(false);
            if (dispatch.RequestHostShutdown)
            {
                _shutdown.RequestShutdown();
                return;
            }
        }
    }

    private async Task<DispatchResult> DispatchAsync(
        ControlRequest request,
        CancellationToken cancellationToken)
    {
        switch (request.Command)
        {
            case ControlCommand.Challenge:
                return Response(ControlProtocol.SerializeChallenge(request.CorrelationId, _challenges.Issue()));
            case ControlCommand.Start:
                if (!request.TryCreateStartRequest(out var startRequest, out var error))
                    return Response(ControlProtocol.Serialize(error!));
                return Response(ControlProtocol.Serialize(
                    ControlResponse.FromResult(await _runtime.StartAsync(startRequest!).ConfigureAwait(false)),
                    "startResponse"));
            case ControlCommand.Status:
                return Response(ControlProtocol.Serialize(ControlResponse.FromStatus(
                    await _runtime.GetStatusAsync(cancellationToken).ConfigureAwait(false),
                    request.CorrelationId), "statusResponse"));
            case ControlCommand.Stop:
                return Response(ControlProtocol.Serialize(ControlResponse.FromResult(
                    await _runtime.StopAsync(cancellationToken).ConfigureAwait(false),
                    request.CorrelationId), "stopResponse"));
            case ControlCommand.Shutdown:
                var stopped = await _runtime.StopAsync(cancellationToken).ConfigureAwait(false);
                return stopped.Succeeded && stopped.Status == ProxyStatusKind.Stopped
                    ? new DispatchResult(
                        ControlProtocol.Serialize(ControlResponse.ShutdownSuccess(request.CorrelationId)),
                        true)
                    : Response(ControlProtocol.Serialize(
                        ControlResponse.ShutdownFailure(stopped, request.CorrelationId)));
            default:
                throw new InvalidOperationException("Unsupported control command.");
        }
    }

    private static DispatchResult Response(string response) => new(response, false);

    private static async Task<string?> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        var bytes = new List<byte>();
        var oneByte = new byte[1];
        while (bytes.Count <= ControlProtocol.MaxFrameBytes)
        {
            var read = await stream.ReadAsync(oneByte, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                return bytes.Count == 0 ? null : throw new IOException("Incomplete control frame payload.");
            if (oneByte[0] == (byte)'\n')
                return bytes.Count == 0
                    ? throw new IOException("Control frame length is invalid.")
                    : new UTF8Encoding(false, true).GetString(bytes.ToArray());
            bytes.Add(oneByte[0]);
        }
        throw new IOException("Control frame length is invalid.");
    }

    private static async Task WriteFrameAsync(Stream stream, string frame, CancellationToken cancellationToken)
    {
        var payload = Encoding.UTF8.GetBytes(frame + "\n");
        if (payload.Length is 0 or > ControlProtocol.MaxFrameBytes)
            throw new IOException("Control response length is invalid.");
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed record DispatchResult(string Response, bool RequestHostShutdown);
}
