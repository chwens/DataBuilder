namespace DataBuilder.Core.DTOs;

/// <summary>
/// 结构化答案响应：LLM 在生成答案时同步返回主题标签。
/// TopicRaw 为 LLM 自由发挥的原始主题（逗号分隔的短标签），
/// 后续由 TopicTaggingQueue 第二轮聚类归一化为标准 Topic。
/// </summary>
public readonly record struct AnswerWithTopic(string TopicRaw, string Answer);
