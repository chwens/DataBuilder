using System.Diagnostics;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace DataBuilder.Api.Filters;

/// <summary>
/// AOP 切片：拦截所有 Controller Action，记录 Controller / Action / 耗时 / 状态码 / 请求路径。
/// 通过 <c>AddControllersWithViews(options =&gt; options.Filters.AddService&lt;AuditLogFilter&gt;())</c> 全局注册。
/// </summary>
public sealed class AuditLogFilter : IAsyncActionFilter
{
    private readonly ILogger<AuditLogFilter> _logger;

    public AuditLogFilter(ILogger<AuditLogFilter> logger)
    {
        _logger = logger;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await next();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Action 执行异常: {Path}", context.HttpContext.Request.Path);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            var controller = context.RouteData.Values["controller"];
            var action = context.RouteData.Values["action"];
            var statusCode = context.HttpContext.Response.StatusCode;
            var path = context.HttpContext.Request.Path;
            var elapsedMs = stopwatch.ElapsedMilliseconds;

            _logger.LogInformation(
                "[Audit] {Controller}.{Action} {Path} -> {StatusCode} in {ElapsedMs}ms",
                controller, action, path, statusCode, elapsedMs);
        }
    }
}
