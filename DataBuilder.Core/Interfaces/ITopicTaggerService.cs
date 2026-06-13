namespace DataBuilder.Core.Interfaces;

/// <summary>
/// 主题打标服务 — 两轮 LLM 打标：
///   第一轮：自由打标（每个问题 1-3 个中文短标签）→ QAPair.TopicRaw
///   第二轮：聚类归一化（5-15 个标准主题）→ QAPair.Topic
/// </summary>
public interface ITopicTaggerService
{
    /// <summary>
    /// 对指定 Document 下的全部 QAPair 顺序执行"自由打标"和"聚类归一化"两轮 LLM 调用，
    /// 并回填 QAPair.TopicRaw / QAPair.Topic 字段。
    /// 失败兜底：JSON 解析失败重试 1 次；仍失败则对应字段留空。
    ///
    /// 用法：通常由 TopicTaggingQueue 后台消费 Channel 时调用，Controller 不再直接 await。
    /// </summary>
    /// <param name="documentId">目标 Document Id</param>
    Task ClusterDocumentAsync(int documentId);
}
