using System.Text.Json;
using DataBuilder.Core.Entities;
using DataBuilder.Core.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DataBuilder.Core.Services;

/// <summary>
/// 主题打标服务 — LLM 两轮打标实现。
/// 第一轮：自由打标（自由中文短标签，多标签逗号分隔）→ QAPair.TopicRaw
/// 第二轮：聚类归一化（5-15 个标准主题）→ QAPair.Topic
/// </summary>
public class TopicTaggerService : ITopicTaggerService
{
    private const int BatchSize = 50;            // 每批最多 50 个问题
    private const int LargeBatchThreshold = 200; // 超过该数量时分批循环
    private const int ClusterMaxTokens = 4096;   // 第二轮聚类允许的最大 token
    private const int TagMaxTokens = 512;        // 第一轮打标允许的最大 token（50 题 × 标签 < 2KB 足够）

    private readonly AppDbContext _db;
    private readonly ILLMService _llm;
    private readonly ILogger<TopicTaggerService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // 推理模型（MiniMax-M3 / DeepSeek-R1）会在 JSON 前后输出 <think>...</think> 思考过程，
    // 必须在解析前剥离，否则可能被错误地匹配进 JSON 片段。
    private static readonly System.Text.RegularExpressions.Regex ThinkBlockRegex = new(
        @"<think>[\s\S]*?</think>",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    public TopicTaggerService(
        AppDbContext db,
        ILLMService llm,
        ILogger<TopicTaggerService> logger)
    {
        _db = db;
        _llm = llm;
        _logger = logger;
    }

    // ==================== 公开入口：两轮打标合并 ====================

    /// <summary>
    /// 对指定 Document 下的全部 QAPair 顺序执行两轮打标。
    /// 第一轮：自由打标 → TopicRaw；第二轮：聚类归一化 → Topic。
    /// 通常由 TopicTaggingQueue 后台消费 Channel 时调用，Controller 不再直接 await。
    /// </summary>
    public async Task ClusterDocumentAsync(int documentId)
    {
        var qaPairs = await _db.QAPairs
            .Where(q => q.Chunk!.DocumentId == documentId)
            .OrderBy(q => q.Id)
            .ToListAsync();

        if (qaPairs.Count == 0)
        {
            _logger.LogDebug("ClusterDocumentAsync: DocumentId={DocumentId} 下无 QA，跳过。", documentId);
            return;
        }

        // 先查 Document → ProjectId，用于绑定项目级 LLMConfig
        var projectId = await _db.Documents
            .Where(d => d.Id == documentId)
            .Select(d => (int?)d.ProjectId)
            .FirstOrDefaultAsync();

        if (projectId == null)
        {
            _logger.LogWarning("ClusterDocumentAsync: DocumentId={DocumentId} 未找到对应项目，跳过。", documentId);
            return;
        }

        // 第一轮：自由打标
        var tagConfig = await ResolveActiveLLMConfigAsync(projectId.Value);
        if (tagConfig == null)
        {
            _logger.LogWarning("ClusterDocumentAsync: ProjectId={ProjectId} 未配置有效的 LLMConfig，跳过打标。", projectId);
            return;
        }

        try
        {
            await TagRawTopicsAsync(qaPairs, tagConfig);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ClusterDocumentAsync: 第一轮打标异常, DocumentId={DocumentId}", documentId);
            await MarkTaggingFailedAsync(documentId);
            return;
        }

        // 第二轮：聚类归一化
        try
        {
            await ClusterTopicsAsync(documentId, qaPairs, tagConfig);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ClusterDocumentAsync: 第二轮聚类异常, DocumentId={DocumentId}", documentId);
            await MarkTaggingFailedAsync(documentId);
            return;
        }
    }

    // ==================== 失败兜底：Document 状态回写 ====================

    /// <summary>
    /// 打标失败时回写 Document.Status=Done（让流程不会被无限挂起）。
    /// 不抛异常，仅 LogError + 跳过（避免 BackgroundService 被中断）。
    /// </summary>
    private async Task MarkTaggingFailedAsync(int documentId)
    {
        try
        {
            var doc = await _db.Documents.FirstOrDefaultAsync(d => d.Id == documentId);
            if (doc != null && doc.Status != DocumentStatus.Done)
            {
                doc.Status = DocumentStatus.Done;
                await _db.SaveChangesAsync();
                _logger.LogWarning("ClusterDocumentAsync: DocumentId={DocumentId} 打标失败，已回写 Status=Done。", documentId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ClusterDocumentAsync: 回写 DocumentId={DocumentId} 状态失败，跳过。", documentId);
        }
    }

    // ==================== 第一轮：自由打标 ====================

    private async Task TagRawTopicsAsync(IReadOnlyList<QAPair> qaPairs, LLMConfig config)
    {
        if (qaPairs == null || qaPairs.Count == 0)
        {
            _logger.LogDebug("TagRawTopicsAsync: 无可打标 QA 列表，跳过。");
            return;
        }

        // 仅对 Instruction 不为空且当前 TopicRaw 为空的 QA 进行打标（避免覆盖已有标签）
        var pending = qaPairs
            .Where(q => !string.IsNullOrWhiteSpace(q.Instruction) && string.IsNullOrWhiteSpace(q.TopicRaw))
            .ToList();

        if (pending.Count == 0)
        {
            _logger.LogDebug("TagRawTopicsAsync: 全部 QA 已存在 TopicRaw，跳过。");
            return;
        }

        _logger.LogInformation("开始第一轮打标: 待打标数={Count}", pending.Count);

        // 总数 ≤ 200 时一次性提交；> 200 时按 BatchSize 分批循环
        if (pending.Count <= LargeBatchThreshold)
        {
            await TagOneBatchAsync(pending, config);
        }
        else
        {
            for (int i = 0; i < pending.Count; i += BatchSize)
            {
                var batch = pending.Skip(i).Take(BatchSize).ToList();
                await TagOneBatchAsync(batch, config);
            }
        }

        await _db.SaveChangesAsync();
        _logger.LogInformation("第一轮打标完成: 总数={Count}", pending.Count);
    }

    /// <summary>
    /// 对单批（≤ 50 个）QA 调一次 LLM，解析回填 TopicRaw。
    /// </summary>
    private async Task TagOneBatchAsync(List<QAPair> batch, LLMConfig config)
    {
        var systemPrompt = BuildTagRawSystemPrompt();
        var userPrompt = BuildTagRawUserPrompt(batch);

        string response = string.Empty;
        try
        {
            response = await _llm.ChatAsync(systemPrompt, userPrompt, config, TagMaxTokens);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "第一轮打标 LLM 调用失败，该批次 TopicRaw 留空。");
            return;
        }

        // 解析 JSON，失败重试 1 次
        var parsed = TryParseTagMapping(response, batch.Count);
        if (parsed == null)
        {
            _logger.LogWarning("第一轮打标 JSON 解析失败，重试 1 次。");
            try
            {
                response = await _llm.ChatAsync(systemPrompt, userPrompt, config, TagMaxTokens);
                parsed = TryParseTagMapping(response, batch.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "第一轮打标 LLM 重试仍失败，TopicRaw 留空。");
                return;
            }
        }

        if (parsed == null)
        {
            _logger.LogError("第一轮打标重试后仍无法解析 JSON，TopicRaw 留空。");
            return;
        }

        // 回填（按 i 索引对齐）
        for (int i = 0; i < batch.Count && i < parsed.Count; i++)
        {
            var tags = parsed[i];
            if (!string.IsNullOrWhiteSpace(tags))
            {
                // 截断到字段上限 500（实际几乎不可能触达）
                batch[i].TopicRaw = tags.Length > 500 ? tags[..500] : tags;
            }
        }
    }

    private static string BuildTagRawSystemPrompt()
    {
        return """
你是主题分类助手。请为每个问题打 1-3 个简短中文标签（逗号分隔），每个标签不超过 10 个字。
标签应能概括问题的核心主题，遵循以下规则：
1. 使用名词或名词短语，避免动词和句子
2. 优先使用领域内通用术语（如"算法""数据结构""操作系统""数据库"）
3. 避免使用"问题""相关""其他"等无意义标签
4. 若问题包含多个主题，给出多个标签（最多 3 个）

请严格按 JSON 数组格式返回结果，不要包含任何解释或额外文字。
""";
    }

    private static string BuildTagRawUserPrompt(List<QAPair> batch)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"以下是 {batch.Count} 个问题，请为每个问题打标签。");
        sb.AppendLine();
        for (int i = 0; i < batch.Count; i++)
        {
            sb.AppendLine($"问题{i + 1}：{batch[i].Instruction}");
        }
        sb.AppendLine();
        sb.AppendLine("请按 JSON 格式返回：[{\"i\":1,\"tags\":\"算法,数据结构\"}, {\"i\":2,\"tags\":\"操作系统\"}, ...]");
        return sb.ToString();
    }

    /// <summary>
    /// 解析 LLM 返回的 JSON 数组，提取每项的 tags 字段，按位置顺序返回字符串列表。
    /// 解析失败返回 null。
    /// </summary>
    private static List<string>? TryParseTagMapping(string rawText, int expectedCount)
    {
        if (string.IsNullOrWhiteSpace(rawText)) return null;

        // 先抓 JSON 数组
        var match = LlmJsonRegex.Array.Match(rawText);
        if (!match.Success) return null;

        try
        {
            var items = JsonSerializer.Deserialize<List<TagItemDto>>(match.Value, JsonOptions);
            if (items == null) return null;

            var result = new List<string>(expectedCount);
            for (int i = 0; i < expectedCount; i++)
            {
                var idx = items.FindIndex(x => x.i == i + 1);
                result.Add(idx >= 0 ? (items[idx].tags ?? string.Empty) : string.Empty);
            }
            return result;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class TagItemDto
    {
        public int i { get; set; }
        public string? tags { get; set; }
    }

    // ==================== 第二轮：聚类归一化 ====================

    private async Task ClusterTopicsAsync(int documentId, IReadOnlyList<QAPair> qaPairs, LLMConfig config)
    {
        if (qaPairs.Count == 0)
        {
            _logger.LogDebug("ClusterTopicsAsync: DocumentId={DocumentId} 下无 QA，跳过聚类。", documentId);
            return;
        }

        // 仅收集 TopicRaw 非空的 QA 用于聚类
        var withRawTags = qaPairs
            .Where(q => !string.IsNullOrWhiteSpace(q.TopicRaw))
            .ToList();

        if (withRawTags.Count == 0)
        {
            _logger.LogDebug("ClusterTopicsAsync: DocumentId={DocumentId} 下无 TopicRaw，跳过聚类。", documentId);
            return;
        }

        _logger.LogInformation("开始第二轮聚类: DocumentId={DocumentId}, QA 数={Count}", documentId, withRawTags.Count);

        var systemPrompt = BuildClusterSystemPrompt();
        var userPrompt = BuildClusterUserPrompt(withRawTags);

        string response = string.Empty;
        try
        {
            response = await _llm.ChatAsync(systemPrompt, userPrompt, config, ClusterMaxTokens);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "第二轮聚类 LLM 调用失败，DocumentId={DocumentId} Topic 留空。", documentId);
            return;
        }

        var mapping = TryParseClusterResponse(response, withRawTags.Count);
        if (mapping == null)
        {
            _logger.LogWarning("第二轮聚类 JSON 解析失败，DocumentId={DocumentId} 重试 1 次。", documentId);
            try
            {
                response = await _llm.ChatAsync(systemPrompt, userPrompt, config, ClusterMaxTokens);
                mapping = TryParseClusterResponse(response, withRawTags.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "第二轮聚类 LLM 重试仍失败，DocumentId={DocumentId} Topic 留空。", documentId);
                return;
            }
        }

        if (mapping == null)
        {
            _logger.LogError("第二轮聚类重试后仍无法解析 JSON，DocumentId={DocumentId} Topic 留空。", documentId);
            return;
        }

        // 回填 Topic
        for (int i = 0; i < withRawTags.Count; i++)
        {
            var topic = mapping[i];
            if (!string.IsNullOrWhiteSpace(topic))
            {
                // 截断到字段上限 100
                withRawTags[i].Topic = topic.Length > 100 ? topic[..100] : topic;
            }
        }

        await _db.SaveChangesAsync();
        _logger.LogInformation("第二轮聚类完成: DocumentId={DocumentId}, 已归一化={Count}", documentId, withRawTags.Count);
    }

    private static string BuildClusterSystemPrompt()
    {
        return """
你是主题归一化助手。请将同义或近义标签归并成 5-15 个标准主题词，并给出每个问题对应的新主题。
要求：
1. 标准主题词应简洁、互斥、覆盖全部问题
2. 优先使用领域内通用术语，2-6 个汉字为宜
3. 映射关系必须覆盖所有问题（每条 new 字段都填一个标准主题名）
4. 只输出 JSON，不要包含任何解释或额外文字
""";
    }

    private static string BuildClusterUserPrompt(List<QAPair> qaPairs)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"以下是 {qaPairs.Count} 个问题的主题标签（自由发挥）：");
        sb.AppendLine();
        for (int i = 0; i < qaPairs.Count; i++)
        {
            sb.AppendLine($"Q{i + 1}: {qaPairs[i].TopicRaw}");
        }
        sb.AppendLine();
        sb.AppendLine("请输出归并后的标准主题表 + 每个问题对应的新主题（用最匹配的标准主题）：");
        sb.AppendLine("{\"themes\":[\"算法\",\"数据结构\",\"操作系统\",\"...\"], \"mapping\":[{\"i\":1,\"new\":\"算法\"}, {\"i\":2,\"new\":\"数据结构\"}, ...]}");
        return sb.ToString();
    }

    /// <summary>
    /// 解析 LLM 返回的 JSON 对象，提取 mapping 数组。
    /// 返回值：按位置顺序对齐的 string 列表（每个问题对应一个归一化主题）。
    /// </summary>
    private static List<string>? TryParseClusterResponse(string rawText, int expectedCount)
    {
        if (string.IsNullOrWhiteSpace(rawText)) return null;

        // 1) 剥离 LLM 推理模型常见的 <think>...</think> 块
        //    MiniMax-M3 / DeepSeek-R1 等推理模型会在 JSON 前后输出思考过程
        var cleaned = ThinkBlockRegex.Replace(rawText, "");

        // 2) 用 LlmJsonRegex.Object（平衡组）取最外层 {...}，自然跳过 markdown code fence 等
        var match = LlmJsonRegex.Object.Match(cleaned);
        if (!match.Success) return null;

        try
        {
            var obj = JsonSerializer.Deserialize<ClusterResponseDto>(match.Value, JsonOptions);
            if (obj?.mapping == null) return null;

            var result = new List<string>(expectedCount);
            for (int i = 0; i < expectedCount; i++)
            {
                var idx = obj.mapping.FindIndex(x => x.i == i + 1);
                if (idx >= 0 && !string.IsNullOrWhiteSpace(obj.mapping[idx].newTopic))
                {
                    result.Add(obj.mapping[idx].newTopic!);
                }
                else
                {
                    result.Add(string.Empty);
                }
            }
            return result;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class ClusterResponseDto
    {
        public List<string>? themes { get; set; }
        public List<ClusterMappingItemDto>? mapping { get; set; }
    }

    private sealed class ClusterMappingItemDto
    {
        public int i { get; set; }

        /// <summary>
        /// 映射到的新主题名。在原始 JSON 中字段名为 "new"（C# 关键字需改名）。
        /// </summary>
        [System.Text.Json.Serialization.JsonPropertyName("new")]
        public string? newTopic { get; set; }
    }

    // ==================== 辅助：项目级 LLMConfig 解析 ====================

    /// <summary>
    /// 优先按 Project.LLMConfigId 取具体 LLMConfig；若 Project 未配置则回退到全局第一个未软删除配置。
    /// 与 QAController.GenerateQuestions 的 LLM 路由策略保持一致。
    /// </summary>
    private async Task<LLMConfig?> ResolveActiveLLMConfigAsync(int projectId)
    {
        var projectConfigId = await _db.Projects
            .Where(p => p.Id == projectId)
            .Select(p => (int?)p.LLMConfigId)
            .FirstOrDefaultAsync();

        if (projectConfigId != null)
        {
            var cfg = await _db.LLMConfigs
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.Id == projectConfigId.Value);

            if (cfg != null && !cfg.IsDeleted)
            {
                return cfg;
            }

            _logger.LogWarning(
                "ResolveActiveLLMConfigAsync: ProjectId={ProjectId} 指定的 LLMConfigId={LLMConfigId} 无效或已软删除，回退到全局默认。",
                projectId, projectConfigId);
        }
        else
        {
            _logger.LogWarning(
                "ResolveActiveLLMConfigAsync: ProjectId={ProjectId} 未配置 LLMConfigId，回退到全局默认。",
                projectId);
        }

        // 兜底：全局第一个未软删除的 LLMConfig
        return await _db.LLMConfigs
            .IgnoreQueryFilters()
            .Where(c => !c.IsDeleted)
            .OrderBy(c => c.Id)
            .FirstOrDefaultAsync();
    }
}