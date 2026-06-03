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
    /// 第一步：从文本片段生成问题列表（使用 .env 默认配置）
    /// </summary>
    /// <param name="chunkText">文本片段</param>
    /// <param name="qaType">问答类型: Factoid / Reasoning / Summary</param>
    /// <param name="count">期望生成的问题数量</param>
    /// <param name="customPrompt">用户自定义的问题生成专家 Prompt（null 则用默认）</param>
    /// <returns>问题文本列表</returns>
    Task<List<string>> GenerateQuestionsAsync(string chunkText, string qaType = "Factoid",
        int count = 3, string? customPrompt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 第一步：从文本片段生成问题列表（指定模型配置）
    /// </summary>
    Task<List<string>> GenerateQuestionsAsync(string chunkText, LLMConfig config,
        string qaType = "Factoid", int count = 3, string? customPrompt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 第二步：为指定问题生成答案（使用 .env 默认配置）
    /// </summary>
    /// <param name="chunkText">原始文本片段（作为答案参考上下文）</param>
    /// <param name="question">问题文本</param>
    /// <param name="customPrompt">用户自定义的答案生成专家 Prompt（null 则用默认）</param>
    /// <returns>答案文本</returns>
    Task<string> GenerateAnswerAsync(string chunkText, string question, string? customPrompt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 第二步：为指定问题生成答案（指定模型配置）
    /// </summary>
    Task<string> GenerateAnswerAsync(string chunkText, string question, LLMConfig config,
        string? customPrompt = null,
        CancellationToken cancellationToken = default);
}
