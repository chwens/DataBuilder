using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DataBuilder.Api.Middlewares;

/// <summary>
/// 自定义 Middleware：包裹整个请求管道，统计耗时并写入 <c>X-Response-Time-Ms</c> 响应头。
/// 位置：在 <c>UseRouting</c> 之前注册，确保在 endpoint 选择前记录起点。
/// </summary>
public sealed class RequestTimingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestTimingMiddleware> _logger;
    private readonly int _slowRequestThresholdMs;

    public RequestTimingMiddleware(
        RequestDelegate next,
        ILogger<RequestTimingMiddleware> logger,
        IConfiguration configuration)
    {
        _next = next;
        _logger = logger;
        _slowRequestThresholdMs = configuration.GetValue<int>("RequestTiming:SlowThresholdMs", 500);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var hasException = false;
        try
        {
            // P0 fix: register OnStarting callback BEFORE _next() so we never write headers
            // after the response has already started (avoids InvalidOperationException that
            // would mask the original exception).
            context.Response.OnStarting(() =>
            {
                stopwatch.Stop();
                var elapsedMs = stopwatch.ElapsedMilliseconds;
                var statusCode = context.Response.StatusCode;

                // P1 fix: only write X-Response-Time-Ms for successful responses (status < 400),
                // preventing the header from leaking to error pages via UseExceptionHandler.Clear().
                // Belt-and-suspenders: even though OnStarting fires before first byte,
                // guard against edge cases where headers might already be locked.
                if (statusCode < 400
                    && !context.Response.HasStarted
                    && !context.Response.Headers.IsReadOnly)
                {
                    context.Response.Headers["X-Response-Time-Ms"] = elapsedMs.ToString();
                }

                return Task.CompletedTask;
            });

            await _next(context);
        }
        catch (Exception)
        {
            hasException = true;
            throw;
        }
        finally
        {
            stopwatch.Stop();
            var elapsedMs = stopwatch.ElapsedMilliseconds;

            var path = context.Request.Path;
            var method = context.Request.Method;

            // P0 fix: if _next() threw, StatusCode is unreliable (still 200 because
            // UseExceptionHandler hasn't run yet). Report 500 for exceptions.
            var statusCode = hasException ? 500 : context.Response.StatusCode;

            if (hasException || statusCode >= 400)
            {
                // P2 fix: structured error log for routing failures (404), auth
                // rejections (401/403), server errors (500), and unhandled exceptions.
                _logger.LogError("请求异常: {Method} {Path} -> {StatusCode} in {ElapsedMs}ms",
                    method, path, statusCode, elapsedMs);
            }
            else if (elapsedMs > _slowRequestThresholdMs)
            {
                // P2 fix: only log slow requests as Warning. Fast normal requests
                // produce no log at all.
                _logger.LogWarning("慢请求: {Method} {Path} -> {StatusCode} in {ElapsedMs}ms",
                    method, path, statusCode, elapsedMs);
            }
            // Fast normal requests (status < 400, elapsed <= threshold): intentionally no log.
        }
    }
}
