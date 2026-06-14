using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace DataBuilder.Api.Middlewares;

/// <summary>
/// 自定义 Middleware：包裹整个请求管道，统计耗时并写入 <c>X-Response-Time-Ms</c> 响应头。
/// 位置：在 <c>UseRouting</c> 之前注册，确保在 endpoint 选择前记录起点。
/// </summary>
public sealed class RequestTimingMiddleware
{
    private const int SlowRequestThresholdMs = 500;
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestTimingMiddleware> _logger;

    public RequestTimingMiddleware(RequestDelegate next, ILogger<RequestTimingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();
            var elapsedMs = stopwatch.ElapsedMilliseconds;

            // 必须在 _next 之后写响应头，否则会被后续中间件覆盖
            context.Response.Headers["X-Response-Time-Ms"] = elapsedMs.ToString();

            var path = context.Request.Path;
            var method = context.Request.Method;
            var statusCode = context.Response.StatusCode;

            if (elapsedMs > SlowRequestThresholdMs)
            {
                _logger.LogWarning("慢请求: {Method} {Path} -> {StatusCode} in {ElapsedMs}ms",
                    method, path, statusCode, elapsedMs);
            }
            else
            {
                _logger.LogInformation("{Method} {Path} -> {StatusCode} in {ElapsedMs}ms",
                    method, path, statusCode, elapsedMs);
            }
        }
    }
}
