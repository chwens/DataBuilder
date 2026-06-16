using DataBuilder.Core.DTOs;
using DataBuilder.Core.Entities;

namespace DataBuilder.Core.Interfaces;

/// <summary>
/// LLM 调用服务 — 封装 Minimax/OpenAI 兼容接口
/// 采用两步法：先生成问题列表，再逐问题生成答案
/// </summary>
public interface ILLMService
{
    /// <summary>
    /// 第一步：从文本片段生成问题列表（指定模型配置）
    /// </summary>
    Task<List<string>> GenerateQuestionsAsync(string chunkText, LLMConfig config,
        string qaType = "Factoid", int count = 3, string? customPrompt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 第二步：为指定问题生成答案并同步返回主题标签（指定模型配置）。
    /// LLM 同时返回答案内容和 1-3 个中文主题标签，避免二次 LLM 调用。
    /// </summary>
    /// <returns>结构化响应，包含 TopicRaw（原始主题标签）和 Answer（答案内容）。</returns>
    Task<AnswerWithTopic> GenerateAnswerAsync(string chunkText, string question, LLMConfig config,
        string? customPrompt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 纯 Chat Completion 调用（无业务模板），用于统计/打标等辅助任务。
    /// </summary>
    /// <param name="systemPrompt">系统提示词</param>
    /// <param name="userPrompt">用户提示词</param>
    /// <param name="config">LLM 模型配置</param>
    /// <param name="maxTokens">最大生成 token 数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>LLM 返回的 content 字符串（原始，未做格式清洗）</returns>
    Task<string> ChatAsync(string systemPrompt, string userPrompt, LLMConfig config,
        int maxTokens = 2048,
        CancellationToken cancellationToken = default);
}
