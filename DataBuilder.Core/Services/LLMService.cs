using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DataBuilder.Core.DTOs;
using DataBuilder.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DataBuilder.Core.Services;

public class LLMService : ILLMService
{
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly ILogger<LLMService> _logger;

    private static readonly Regex JsonArrayRegex = new(
        @"\[[\s\S]*?\]", RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public LLMService(HttpClient httpClient, IConfiguration configuration, ILogger<LLMService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        var baseUrl = configuration["LLM:BaseUrl"] ?? "https://api.minimax.chat/v1";
        var apiKey = configuration["LLM:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
            apiKey = Environment.GetEnvironmentVariable("MINIMAX_TOKEN") ?? "";
        _model = configuration["LLM:Model"] ?? "MiniMax-M2.5";

        // 必须保留尾部斜杠，否则 HttpClient 在拼接相对 URI 时会丢弃最后一个路径段
        // 例如 https://api.minimax.chat/v1 + chat/completions → https://api.minimax.chat/chat/completions (错误!)
        _httpClient.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);
        _httpClient.Timeout = TimeSpan.FromSeconds(120);

        _logger.LogInformation("LLMService 初始化: BaseUrl={BaseUrl}, Model={Model}, HasApiKey={HasKey}",
            baseUrl, _model, !string.IsNullOrEmpty(apiKey));
    }

    // ==================== 第一步：生成问题 ====================

    public async Task<List<string>> GenerateQuestionsAsync(
        string chunkText, string qaType = "Factoid", int count = 3, string? customPrompt = null,
        CancellationToken cancellationToken = default)
    {
        var systemPrompt = customPrompt ?? BuildDefaultQuestionSystemPrompt(qaType, count);
        var userPrompt = BuildQuestionUserPrompt(chunkText, count, qaType);

        var response = await CallLLMAsync(systemPrompt, userPrompt, 0.7f, count * 300, cancellationToken);
        return ParseQuestionList(response);
    }

    // ==================== 第二步：生成答案 ====================

    public async Task<string> GenerateAnswerAsync(
        string chunkText, string question, string? customPrompt = null,
        CancellationToken cancellationToken = default)
    {
        var systemPrompt = customPrompt ?? BuildDefaultAnswerSystemPrompt();
        var userPrompt = BuildAnswerUserPrompt(chunkText, question);

        var response = await CallLLMAsync(systemPrompt, userPrompt, 0.7f, 1500, cancellationToken);
        return response.Trim();
    }

    // ==================== LLM 调用核心 ====================

    private async Task<string> CallLLMAsync(
        string systemPrompt, string userPrompt, float temperature, int maxTokens,
        CancellationToken cancellationToken = default)
    {
        var request = new ChatCompletionRequest
        {
            Model = _model,
            Messages = new List<ChatMessage>
            {
                new() { Role = "system", Content = systemPrompt },
                new() { Role = "user", Content = userPrompt }
            },
            Temperature = temperature,
            MaxTokens = maxTokens
        };

        for (int attempt = 0; attempt < 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var json = JsonSerializer.Serialize(request);
                var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync("chat/completions", httpContent, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("LLM API 返回错误 (尝试 {Attempt}/3): {StatusCode}",
                        attempt + 1, response.StatusCode);
                    throw new HttpRequestException($"LLM API 返回 {response.StatusCode}: {body}");
                }

                var completion = JsonSerializer.Deserialize<ChatCompletionResponse>(body);

                var content = completion?.Choices?.FirstOrDefault()?.Message.Content;
                if (string.IsNullOrEmpty(content))
                    throw new InvalidOperationException("LLM 返回了空响应或无效的 choices 列表");

                return content;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "LLM 调用失败 (尝试 {Attempt}/3)", attempt + 1);

                if (attempt >= 2)
                    throw new InvalidOperationException("LLM 调用在 3 次重试后仍然失败", ex);

                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken);
            }
        }

        throw new InvalidOperationException("LLM 调用在 3 次重试后仍然失败");
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
        var jsonMatch = JsonArrayRegex.Match(rawText);
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
