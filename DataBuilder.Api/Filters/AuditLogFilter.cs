using System.Diagnostics;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace DataBuilder.Api.Filters;

/// <summary>
/// AOP 切片：拦截所有 Controller Action，记录 Controller / Action / 耗时 / 状态码 / 请求路径。
/// <para>
/// 使用 <b>IAsyncResultFilter</b> 而非 IAsyncActionFilter 的原因：
/// </para>
/// <list type="bullet">
/// <item><b>时序正确</b>：IAsyncResultFilter.OnResultExecutionAsync 的委托在
///   IActionResult.ExecuteResultAsync() 执行之后返回，此时 Response.StatusCode
///   已是最终值。IAsyncActionFilter 的 finally 块在 Result 执行前运行，非 200
///   状态码（302/400/401/404/500）会被误报为 200。</item>
/// <item><b>异常可见</b>：ResultExecutionDelegate 返回 ResultExecutedContext，
///   其中包含 Exception / ExceptionHandled / Canceled 信息，用于区分正常响应、
///   已处理异常、未处理异常和请求取消。</item>
/// </list>
/// 通过 <c>AddControllersWithViews(options =&gt; options.Filters.AddService&lt;AuditLogFilter&gt;())</c> 全局注册。
/// </summary>
public sealed class AuditLogFilter : IAsyncResultFilter
{
    private readonly ILogger<AuditLogFilter> _logger;

    public AuditLogFilter(ILogger<AuditLogFilter> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 在 Result 执行前后进行审计记录。
    /// 注意：Action 业务异常在 ControllerActionInvoker 内部被 catch 并存入
    /// ActionExecutedContext.Exception，不会传播到 IAsyncActionFilter 的 catch 块。
    /// IAsyncResultFilter 阶段 Action 异常已转为最终响应（500 或异常处理结果），
    /// StatusCode 已确定。
    /// </summary>
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        var stopwatch = Stopwatch.StartNew();

        // await next() 执行 IActionResult.ExecuteResultAsync() 并返回 ResultExecutedContext。
        // 返回后 Response.StatusCode 为最终值。
        ResultExecutedContext executedContext = await next();

        stopwatch.Stop();

        // Use ActionDescriptor.RouteValues instead of RouteData.Values.
        // ActionDescriptor.RouteValues is populated by ControllerActionDescriptorProvider
        // at startup and is reliable regardless of conventional vs attribute routing.
        // RouteData.Values may lack controller/action keys under pure attribute routes.
        var controller = context.ActionDescriptor.RouteValues["controller"] ?? "?";
        var action = context.ActionDescriptor.RouteValues["action"] ?? "?";
        var path = context.HttpContext.Request.Path;
        var statusCode = context.HttpContext.Response.StatusCode;
        var elapsedMs = stopwatch.ElapsedMilliseconds;

        // ---------- 按状态码分级 ----------
        LogLevel level = statusCode switch
        {
            >= 200 and < 300 => LogLevel.Information,
            >= 400 and < 500 => LogLevel.Warning,
            >= 500               => LogLevel.Error,
            _                    => LogLevel.Information
        };

        // ---------- 读取 ResultExecutedContext 的状态 ----------
        bool canceled = executedContext.Canceled;
        bool exceptionHandled = executedContext.ExceptionHandled;
        Exception? ex = executedContext.Exception;

        // ---------- 根据状态输出不同审计消息 ----------
        if (canceled)
        {
            _logger.Log(level,
                "[Audit] {Controller}.{Action} {Path} -> {StatusCode} in {ElapsedMs}ms (Canceled)",
                controller, action, path, statusCode, elapsedMs);
        }
        else if (ex is not null && !exceptionHandled)
        {
            _logger.Log(level, ex,
                "[Audit] {Controller}.{Action} {Path} -> {StatusCode} in {ElapsedMs}ms (Unhandled Result Exception)",
                controller, action, path, statusCode, elapsedMs);
        }
        else if (ex is not null && exceptionHandled)
        {
            _logger.Log(level,
                "[Audit] {Controller}.{Action} {Path} -> {StatusCode} in {ElapsedMs}ms (ExceptionHandled)",
                controller, action, path, statusCode, elapsedMs);
        }
        else
        {
            _logger.Log(level,
                "[Audit] {Controller}.{Action} {Path} -> {StatusCode} in {ElapsedMs}ms",
                controller, action, path, statusCode, elapsedMs);
        }
    }
}
