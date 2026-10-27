using System.Collections.Concurrent;
using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace ProductAssetManager.Api.Tests.ApiTests;

public record CapturedLog(
    string? CategoryName,
    string? Body,
    ActivityTraceId TraceId,
    ActivitySpanId SpanId,
    IReadOnlyDictionary<string, object?> Attributes,
    Exception? Exception);

public class CapturedLogs : BaseProcessor<LogRecord>
{
    private readonly ConcurrentQueue<CapturedLog> _logs = new();

    public IReadOnlyList<CapturedLog> Snapshot() => _logs.ToList();

    public override void OnEnd(LogRecord data)
    {
        var attributes = new Dictionary<string, object?>();

        if (data.Attributes is not null)
        {
            foreach (var attribute in data.Attributes)
            {
                attributes[attribute.Key] = attribute.Value;
            }
        }

        _logs.Enqueue(new CapturedLog(data.CategoryName, data.Body, data.TraceId, data.SpanId, attributes, data.Exception));
    }
}
