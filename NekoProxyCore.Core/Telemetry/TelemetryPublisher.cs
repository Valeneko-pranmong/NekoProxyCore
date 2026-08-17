using System.Globalization;
using System.Text.Json;

namespace NekoProxyCore.Core;

public interface ITelemetryPublisher
{
    ulong DroppedEventsCount { get; }
    void Publish<T>(string messageType, string component, T payload);
    void PublishLifecycle(string messageType, string component = "core");
}

public sealed class TelemetryPublisher : ITelemetryPublisher
{
    public const int SchemaVersion = 1;

    private readonly ITelemetryBuffer _buffer;
    private readonly Func<DateTimeOffset> _clock;
    private long _sequence;

    public TelemetryPublisher(
        ITelemetryBuffer buffer,
        Func<DateTimeOffset>? clock = null)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public ulong DroppedEventsCount => _buffer.DroppedEventsCount;

    public void Publish<T>(string messageType, string component, T payload)
    {
        if (string.IsNullOrWhiteSpace(messageType))
            throw new ArgumentException("Message type cannot be null or empty.", nameof(messageType));
        if (string.IsNullOrWhiteSpace(component))
            throw new ArgumentException("Component cannot be null or empty.", nameof(component));

        try
        {
            var seq = (ulong)Interlocked.Increment(ref _sequence);
            var timestamp = _clock().UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
            var envelope = new TelemetryEnvelope<T>(
                SchemaVersion,
                seq,
                timestamp,
                messageType,
                component,
                payload);

            var json = JsonSerializer.Serialize(envelope);
            _buffer.Enqueue(json);
        }
        catch
        {
            // Telemetry failures must never propagate into proxy or control paths.
        }
    }

    public void PublishLifecycle(string messageType, string component = "core")
    {
        Publish(messageType, component, new EmptyPayload());
    }

    private sealed record EmptyPayload;
}
