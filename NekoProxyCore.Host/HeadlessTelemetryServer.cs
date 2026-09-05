using System.IO.Pipes;
using System.Text;
using NekoProxyCore.Core;
using NekoProxyCore.Host.Protocol;

namespace NekoProxyCore.Host;

internal sealed class HeadlessTelemetryServer
{
    public const string PipeName = TelemetryProtocol.PipeName;

    private readonly ITelemetryBuffer _buffer;
    private readonly ICoreDiagnosticSink _diagnostics;
    private readonly string _pipeName;

    public HeadlessTelemetryServer(
        ITelemetryBuffer buffer,
        string pipeName = PipeName,
        ICoreDiagnosticSink? diagnostics = null)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        _pipeName = string.IsNullOrWhiteSpace(pipeName)
            ? throw new ArgumentException("A pipe name is required.", nameof(pipeName))
            : pipeName;
        _diagnostics = diagnostics ?? NullCoreDiagnosticSink.Instance;
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
                    PipeDirection.Out,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await StreamTelemetryToClientAsync(pipe, cancellationToken).ConfigureAwait(false);
                }
                catch (IOException)
                {
                    // Client disconnects, broken pipes, or reader closed are handled cleanly.
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch
                {
                    // Telemetry client transmission failures are isolated from host runtime.
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Graceful shutdown.
        }
        catch
        {
            // Pipe creation failure or OS exception must not crash the host process.
        }
    }

    private async Task StreamTelemetryToClientAsync(Stream stream, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var frame = await _buffer.DequeueAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(frame))
                continue;

            var payload = Encoding.UTF8.GetBytes(frame + "\n");
            if (payload.Length > TelemetryProtocol.MaxFrameBytes)
                continue;

            await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
