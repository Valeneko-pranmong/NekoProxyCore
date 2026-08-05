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

    public HeadlessControlServer(IProxyRuntime runtime, ICoreChallengeService challenges)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _challenges = challenges ?? throw new ArgumentNullException(nameof(challenges));
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeServerStream(
                PipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            await ServeClientAsync(pipe, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ServeClientAsync(Stream stream, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var frame = await ReadFrameAsync(stream, cancellationToken).ConfigureAwait(false);
            if (frame is null)
                return;

            string response;
            if (!ControlProtocol.TryParseRequest(frame, _challenges, out var request, out var error))
            {
                response = ControlProtocol.Serialize(error!);
            }
            else
            {
                response = await DispatchAsync(request!, cancellationToken).ConfigureAwait(false);
            }

            await WriteFrameAsync(stream, response, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<string> DispatchAsync(ControlRequest request, CancellationToken cancellationToken)
    {
        switch (request.Command)
        {
            case ControlCommand.Challenge:
                return ControlProtocol.SerializeChallenge(request.CorrelationId, _challenges.Issue());
            case ControlCommand.Start:
                if (!request.TryCreateStartRequest(out var startRequest, out var error))
                    return ControlProtocol.Serialize(error!);
                return ControlProtocol.Serialize(
                    ControlResponse.FromResult(await _runtime.StartAsync(startRequest!).ConfigureAwait(false)),
                    "startResponse");
            case ControlCommand.Status:
                return ControlProtocol.Serialize(ControlResponse.FromStatus(
                    await _runtime.GetStatusAsync(cancellationToken).ConfigureAwait(false),
                    request.CorrelationId), "statusResponse");
            case ControlCommand.Stop:
                return ControlProtocol.Serialize(ControlResponse.FromResult(
                    await _runtime.StopAsync(cancellationToken).ConfigureAwait(false),
                    request.CorrelationId), "stopResponse");
            default:
                throw new InvalidOperationException("Unsupported control command.");
        }
    }

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
}
