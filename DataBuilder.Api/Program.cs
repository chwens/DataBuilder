using DataBuilder.Api.Models;
using DataBuilder.Core;
using DataBuilder.Core.Entities;
using DataBuilder.Core.Interfaces;
using DataBuilder.Core.Services;
using Microsoft.EntityFrameworkCore;
using NLog.Extensions.Logging;

// 加载 .env 文件中的环境变量（必须在 CreateBuilder 之前，否则 Configuration 无法读取）
var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
if (string.IsNullOrEmpty(env) || env == "Development")
{
    LoadEnvFile(Path.Combine(Directory.GetCurrentDirectory(), "..", ".env"));
}

var builder = WebApplication.CreateBuilder(args);

// 配置 NLog
builder.Logging.ClearProviders();
builder.Logging.AddNLog();

// 数据库 — MySQL（带 null 检查，避免未配置时启动崩溃）
var connectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.WriteLine("警告: 未配置数据库连接字符串，跳过数据库注册。");
}
else
{
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));
}

// 注册核心服务
builder.Services.AddScoped<IDocumentParser, DocumentParser>();
builder.Services.AddScoped<IAlpacaExporter, AlpacaExporter>();

// LLM Service — 不再依赖注入 HttpClient，改为由 LLMConfig 驱动每次创建
builder.Services.AddScoped<ILLMService, LLMService>();

builder.Services.Configure<SiteOptions>(builder.Configuration.GetSection("Site"));

builder.Services.AddSingleton<IEncryptionService, EncryptionService>();

builder.Services.AddControllersWithViews();

var app = builder.Build();

// 种子数据：如果没有 LLMConfig，创建默认 MiniMax 配置
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var encryption = scope.ServiceProvider.GetRequiredService<IEncryptionService>();

    if (!await db.LLMConfigs.AnyAsync())
    {
        var apiKey = Environment.GetEnvironmentVariable("MINIMAX_TOKEN")
                  ?? builder.Configuration["LLM:ApiKey"]
                  ?? "";

        if (!string.IsNullOrEmpty(apiKey))
        {
            db.LLMConfigs.Add(new LLMConfig
            {
                Provider = "MiniMax",
                ApiUrl = "https://api.minimax.chat/v1/",
                ApiKeyEncrypted = encryption.Encrypt(apiKey),
                ModelId = "MiniMax-M2.5",
                ModelName = "MiniMax-M2.5",
                ModelLabel = "MiniMax",
                Temperature = 0.7f,
                MaxTokens = 8192,
                TopP = 1.0f,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

// 简易 .env 加载器
static void LoadEnvFile(string path)
{
    if (!File.Exists(path)) return;

    foreach (var line in File.ReadAllLines(path))
    {
        var trimmed = line.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
            continue;

        var eq = trimmed.IndexOf('=');
        if (eq <= 0) continue;

        var key = trimmed[..eq].Trim();
        var value = trimmed[(eq + 1)..].Trim();

        if (Environment.GetEnvironmentVariable(key) is null)
            Environment.SetEnvironmentVariable(key, value);
    }
}
