using System.Diagnostics;
using DocumentFlow.Infrastructure.Observability;

namespace DocumentFlow.Api.Hosted;

public sealed class RequestTelemetryMiddleware(RequestDelegate next, ILogger<RequestTelemetryMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers["X-Correlation-ID"].ToString();
        var correlationId = incoming.Length is > 0 and <= 64 &&
            incoming.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_')
            ? incoming : Guid.NewGuid().ToString("N");
        context.Response.Headers["X-Correlation-ID"] = correlationId;
        using var scope = logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId });
        var started = Stopwatch.GetTimestamp();
        var failed = false;
        try { await next(context); }
        catch { failed = true; throw; }
        finally
        {
            var elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unmatched";
            var statusCode = failed ? 500 : context.Response.StatusCode;
            var statusClass = $"{statusCode / 100}xx";
            var tags = new TagList { { "route", route }, { "method", context.Request.Method }, { "status_class", statusClass } };
            DocumentFlowMetrics.ApiRequests.Add(1, tags);
            DocumentFlowMetrics.ApiRequestDuration.Record(elapsedMs, tags);
            if (statusCode >= 500)
                logger.LogWarning("Request failed on {Route} with {StatusCode} in {ElapsedMs} ms",
                    route, statusCode, elapsedMs);
        }
    }
}
