using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DataBuilder.Core.Entities;

public class QAPair
{
    public int Id { get; set; }

    public int ChunkId { get; set; }

    /// <summary>
    /// Alpaca 格式: instruction (任务描述)
    /// </summary>
    [Required]
    public string Instruction { get; set; } = string.Empty;

    /// <summary>
    /// Alpaca 格式: input (可选的上下文)
    /// </summary>
    public string? Input { get; set; }

    /// <summary>
    /// Alpaca 格式: output (期望的回答)
    /// </summary>
    [Required]
    public string Output { get; set; } = string.Empty;

    /// <summary>
    /// 问答类型: Factoid / Reasoning / Summary / etc.
    /// </summary>
    [MaxLength(50)]
    public string Type { get; set; } = "Factoid";

    /// <summary>
    /// 质量评分 (1-5, 默认3)
    /// </summary>
    public int QualityScore { get; set; } = 3;

    /// <summary>
    /// 是否已生成答案（两步法中标记答案生成状态）
    /// </summary>
    public bool Answered { get; set; } = false;

    /// <summary>
    /// 第一轮 LLM 自由打标的原始标签（多个标签以逗号分隔）
    /// </summary>
    [MaxLength(500)]
    public string? TopicRaw { get; set; }

    /// <summary>
    /// 第二轮 LLM 聚类归一化后的主主题
    /// </summary>
    [MaxLength(100)]
    public string? Topic { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(ChunkId))]
    public Chunk? Chunk { get; set; }
}
