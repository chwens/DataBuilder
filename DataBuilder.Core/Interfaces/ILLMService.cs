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
    /// 第二步：为指定问题生成答案（指定模型配置）
    /// </summary>
    Task<string> GenerateAnswerAsync(string chunkText, string question, LLMConfig config,
        string? customPrompt = null,
        CancellationToken cancellationToken = default);
}
