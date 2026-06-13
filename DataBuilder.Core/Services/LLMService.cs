using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DataBuilder.Core.DTOs;
using DataBuilder.Core.Entities;
using DataBuilder.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace DataBuilder.Core.Services;

public class LLMService : ILLMService
{
    private readonly IEncryptionService _encryption;
    private readonly ILogger<LLMService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    private const string HttpClientName = "LLM";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // ==================== LLMConfig 维度缓存（线程安全）====================
    // Bug #2 修复：原实现每次调用都重设 client.BaseAddress / Authorization，
    // 共享 handler 池下并发 Project A/B 用不同 ApiUrl/ApiKey 时互相覆盖，
    // 未发出的请求 BaseAddress/Authorization 错乱（race condition）。
    //
    // 新方案：缓存 LLMConfig.Id -> 规范化 ApiUrl 与解密后 ApiKey，
    // 调用时用绝对 URI + request.Headers.Authorization，
    // 不再修改 IHttpClientFactory 返回的 HttpClient 共享状态。
    private readonly Dictionary<int, string> _baseAddressCache = new();
    private readonly Dictionary<int, string> _apiKeyCache = new();
    private readonly object _cacheLock = new();

    public LLMService(
        IEncryptionService encryption,
        IHttpClientFactory httpClientFactory,
        ILogger<LLMService> logger)
    {
        _encryption = encryption;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _logger.LogInformation("LLMService 初始化完成（IHttpClientFactory 注入，命名 client: {ClientName}）", HttpClientName);
    }

    /// <summary>
    /// 线程安全地按 LLMConfig.Id 获取规范化后的 BaseAddress（结尾带 /）
    /// </summary>
    private string GetOrAddBaseAddress(LLMConfig config)
    {
        if (_baseAddressCache.TryGetValue(config.Id, out var cached))
            return cached;

        lock (_cacheLock)
        {
            if (_baseAddressCache.TryGetValue(config.Id, out cached))
                return cached;

            var normalized = config.ApiUrl.TrimEnd('/') + "/";
            _baseAddressCache[config.Id] = normalized;
            return normalized;
        }
    }

    /// <summary>
    /// 线程安全地按 LLMConfig.Id 获取解密后的 ApiKey（缓存避免每次解密开销）
    /// </summary>
    private string GetOrAddApiKey(LLMConfig config)
    {
        if (_apiKeyCache.TryGetValue(config.Id, out var cached))
            return cached;

        lock (_cacheLock)
        {
            if (_apiKeyCache.TryGetValue(config.Id, out cached))
                return cached;

            var decrypted = _encryption.Decrypt(config.ApiKeyEncrypted);
            _apiKeyCache[config.Id] = decrypted;
            return decrypted;
        }
    }

    // ==================== 第一步：生成问题 ====================

    /// <summary>
    /// 第一步：从文本片段生成问题列表（指定模型配置）
    /// </summary>
    public async Task<List<string>> GenerateQuestionsAsync(
        string chunkText, LLMConfig config,
        string qaType = "Factoid", int count = 3, string? customPrompt = null,
        CancellationToken cancellationToken = default)
    {
        ValidateConfig(config);

        var systemPrompt = customPrompt ?? BuildDefaultQuestionSystemPrompt(qaType, count);
        var userPrompt = BuildQuestionUserPrompt(chunkText, count, qaType);

        var response = await CallLLMWithConfigAsync(
            config, systemPrompt, userPrompt, count * 300, cancellationToken);
        return ParseQuestionList(response);
    }

    // ==================== 第二步：生成答案 ====================

    /// <summary>
    /// 第二步：为指定问题生成答案（指定模型配置）
    /// </summary>
    public async Task<string> GenerateAnswerAsync(
        string chunkText, string question, LLMConfig config,
        string? customPrompt = null,
        CancellationToken cancellationToken = default)
    {
        ValidateConfig(config);

        var systemPrompt = customPrompt ?? BuildDefaultAnswerSystemPrompt();
        var userPrompt = BuildAnswerUserPrompt(chunkText, question);

        var response = await CallLLMWithConfigAsync(
            config, systemPrompt, userPrompt, config.MaxTokens, cancellationToken);
        return response.Trim();
    }

    // ==================== 通用 Chat Completion（用于打标等辅助任务）====================

    /// <summary>
    /// 纯 Chat Completion 调用，直接透传 system/user 提示词。
    /// 内部复用 CallLLMWithConfigAsync 的鉴权/重试/超时逻辑。
    /// </summary>
    public async Task<string> ChatAsync(
        string systemPrompt, string userPrompt, LLMConfig config,
        int maxTokens = 2048,
        CancellationToken cancellationToken = default)
    {
        ValidateConfig(config);
        return await CallLLMWithConfigAsync(
            config, systemPrompt, userPrompt, maxTokens, cancellationToken);
    }

    // ==================== LLM 调用核心 ====================

    /// <summary>
    /// 使用 LLMConfig 配置调用 LLM
    ///
    /// Bug #2 修复要点：
    /// 1. 不再修改 IHttpClientFactory 返回的 HttpClient.BaseAddress / DefaultRequestHeaders.Authorization，
    ///    避免共享 handler 池下并发不同 LLMConfig 互相覆盖造成 race。
    /// 2. BaseAddress / ApiKey 通过缓存（GetOrAddBaseAddress / GetOrAddApiKey）按 LLMConfig.Id 隔离，
    ///    并对 LLMConfig.Id 加锁保证线程安全。
    /// 3. 调用时构造绝对 URI，并把 Authorization 放在 HttpRequestMessage.Headers（per-request），
    ///    完全不污染 HttpClient 共享状态。
    /// </summary>
    private async Task<string> CallLLMWithConfigAsync(
        LLMConfig config, string systemPrompt, string userPrompt,
        int maxTokens, CancellationToken cancellationToken = default)
    {
        var baseAddress = GetOrAddBaseAddress(config);
        var apiKey = GetOrAddApiKey(config);

        // 通过 IHttpClientFactory 获取命名 client，仅用于复用底层 HttpMessageHandler 池和超时设置
        var client = _httpClientFactory.CreateClient(HttpClientName);

        var chatRequest = new ChatCompletionRequest
        {
            Model = config.ModelId,
            Messages = new List<ChatMessage>
            {
                new() { Role = "system", Content = systemPrompt },
                new() { Role = "user", Content = userPrompt }
            },
            Temperature = config.Temperature,
            MaxTokens = maxTokens,
            TopP = config.TopP
        };

        return await CallLLMWithClientAsync(
            client, baseAddress, apiKey, config.ModelId, chatRequest, cancellationToken);
    }

    /// <summary>
    /// HTTP 请求 + 3 次重试 + 响应解析核心逻辑
    ///
    /// Bug #2 修复：使用绝对 URI + per-request Authorization 头，
    /// 不修改传入的 HttpClient 共享状态，确保并发安全。
    /// </summary>
    private async Task<string> CallLLMWithClientAsync(
        HttpClient client, string baseAddress, string apiKey, string model,
        ChatCompletionRequest request,
        CancellationToken cancellationToken)
    {
        var endpoint = new Uri(baseAddress.TrimEnd('/') + "/chat/completions");

        for (int attempt = 0; attempt < 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var json = JsonSerializer.Serialize(request);
                var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

                // 每次请求都用新的 HttpRequestMessage，Authorization 放在 per-request header，
                // 完全隔离不同 LLMConfig 的鉴权信息。
                var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = httpContent
                };
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                var response = await client.SendAsync(httpRequest, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("LLM API 返回错误 (尝试 {Attempt}/3): {StatusCode}",
                        attempt + 1, response.StatusCode);

                    var statusCode = (int)response.StatusCode;
                    if (statusCode is >= 400 and < 500)
                    {
                        // 客户端错误（401/403/404 等）不会因重试而恢复，直接抛出不可重试异常
                        var truncatedBody = body.Length > 200 ? body[..200] + "..." : body;
                        throw new InvalidOperationException(
                            $"LLM API 返回客户端错误 {statusCode}: {truncatedBody}");
                    }

                    throw new HttpRequestException($"LLM API 返回 {response.StatusCode}: {body}");
                }

                var completion = JsonSerializer.Deserialize<ChatCompletionResponse>(body);

                var content = completion?.Choices?.FirstOrDefault()?.Message.Content;
                if (string.IsNullOrEmpty(content))
                    throw new HttpRequestException("LLM 返回了空响应或无效的 choices 列表");

                return content;
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not InvalidOperationException)
            {
                _logger.LogWarning(ex, "LLM 调用失败 (尝试 {Attempt}/3)", attempt + 1);

                if (attempt >= 2)
                    throw new InvalidOperationException("LLM 调用在 3 次重试后仍然失败", ex);

                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken);
            }
        }

        throw new InvalidOperationException("LLM 调用在 3 次重试后仍然失败");
    }

    /// <summary>
    /// 校验 LLMConfig 是否有效
    /// </summary>
    private static void ValidateConfig(LLMConfig config)
    {
        if (config.IsDeleted)
            throw new InvalidOperationException("模型配置已失效，请重新选择模型。");
    }

    // ==================== 默认 Prompt 模板 ====================

    /// <summary>
    /// 默认"问题生成专家" System Prompt
    /// 参考 EasyDataset lib/llm/prompts/question.js
    /// </summary>
    private static string BuildDefaultQuestionSystemPrompt(string qaType, int count)
    {
        return $$"""
# Role: 文本问题生成专家

## Profile
你是一名专业的文本分析与问题设计专家，能够从复杂文本中提炼关键信息并产出可用于模型微调的高质量问题集合。

## Skills
1. 精准理解原文内容，提取核心知识点
2. 设计有明确答案指向的问题
3. 控制问题难度多样性（简单/中等/困难）
4. 严格遵守输出格式

## Workflow
1. 文本解析：仔细阅读全文，识别关键信息点和逻辑结构
2. 问题设计：基于信息密度和重要性选择最佳提问切入点，问题类型为{{qaType}}
3. 质量检查：确保每个问题都能从原文中找到答案，且表述清晰自包含

## Constraints
1. 严格依据原文内容，不得虚构原文不存在的信息
2. 覆盖文本中不同主题和知识点
3. 不要提问关于"文本本身"的元信息（如"这段文字讲了什么"）
4. 不要使用"报告中提到""文章中说""文献指出"等引用性表述
5. 问题应当自包含，脱离上下文仍可被理解
6. 输出恰好 {{count}} 个问题
7. 确保问题数量符合要求

## OutputFormat
严格输出 JSON 字符串数组，不要包含任何其他文字：
```json
["问题1", "问题2", "问题3"]
```
""";
    }

    /// <summary>
    /// 默认"答案生成专家" System Prompt
    /// 参考 EasyDataset lib/llm/prompts/answer.js
    /// </summary>
    private static string BuildDefaultAnswerSystemPrompt()
    {
        return """
# Role: 微调数据集生成专家

## Profile
你是一名专业的微调数据集生成专家，擅长从给定的参考内容中生成高质量、准确的答案。
你对参考内容中的所有信息已内化为专业知识。

## Skills
1. 答案必须严格基于给定的参考内容
2. 答案必须准确，不能胡编乱造
3. 答案必须与问题紧密相关
4. 答案必须符合逻辑，条理清晰

## Workflow
1. 深呼吸，逐步思考问题
2. 分析参考内容，定位与问题相关的段落
3. 提取关键信息并组织语言
4. 生成准确、完整的答案
5. 自检：确认答案中的所有事实都可在参考内容中找到依据

## Constraints
1. 答案必须基于给定的参考内容，不允许使用外部知识
2. 答案应充分、详细、包含所有必要的信息，适合微调大模型训练使用
3. 不得出现"参考""依据""文献中提到""根据原文"等引用性表述
4. 直接给出答案内容，不要附加解释或元信息
""";
    }

    // ==================== User Prompt 构建 ====================

    private static string BuildQuestionUserPrompt(string chunkText, int count, string qaType)
    {
        return $$"""
## 需要分析的文本内容

------
{{chunkText}}
------

请根据以上文本，生成 {{count}} 个{{TypeDisplayName(qaType)}}问题。
""";
    }

    private static string BuildAnswerUserPrompt(string chunkText, string question)
    {
        return $$"""
## 参考内容

------
{{chunkText}}
------

## 问题

{{question}}

请根据参考内容，生成以上问题的答案。
""";
    }

    // ==================== 响应解析 ====================

    /// <summary>
    /// 从 LLM 响应中提取问题列表（JSON 数组）
    /// </summary>
    private static List<string> ParseQuestionList(string rawText)
    {
        var jsonMatch = LlmJsonRegex.Array.Match(rawText);
        if (!jsonMatch.Success)
        {
            // 回退: 按行拆分
            return rawText.Split('\n')
                .Select(l => l.Trim().Trim('"', '“', '”', '「', '」'))
                .Where(l => l.Length > 5)
                .ToList();
        }

        try
        {
            var questions = JsonSerializer.Deserialize<List<string>>(jsonMatch.Value, JsonOptions);
            return questions ?? new List<string>();
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }

    private static string TypeDisplayName(string type) => type switch
    {
        "Factoid" => "事实型",
        "Reasoning" => "推理型",
        "Summary" => "总结型",
        _ => type
    };
}
