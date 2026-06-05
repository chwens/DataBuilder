using System.ComponentModel.DataAnnotations;

namespace DataBuilder.Core.Entities;

public class Project
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    /// <summary>
    /// 自定义"问题生成专家"System Prompt（null 时使用默认模板）
    /// </summary>
    public string? QuestionPrompt { get; set; }

    /// <summary>
    /// 自定义"答案生成专家"System Prompt（null 时使用默认模板）
    /// </summary>
    public string? AnswerPrompt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public int? LLMConfigId { get; set; }
    public LLMConfig? LLMConfig { get; set; }

    // ====== Phase 3D: 任务配置（分块参数） ======

    /// <summary>
    /// 最小段落长度（字符数）。按段落分段时，若累计段落长度小于该值则继续累积。
    /// </summary>
    public int ChunkMinLength { get; set; } = 2500;

    /// <summary>
    /// 最大段落长度（字符数）。按段落分段时，累计段落达到该值时强制切分；按固定长度分段时，作为 chunkSize 默认值。
    /// </summary>
    public int ChunkMaxLength { get; set; } = 4000;

    /// <summary>
    /// 字符/问题比：每多少个字符生成一个问题。值越小生成的问题越多。
    /// </summary>
    public int CharsPerQuestion { get; set; } = 240;

    /// <summary>
    /// 问号去除率（%）：去除问题末尾问号的比例。0 表示不去除，100 表示全部去除。
    /// </summary>
    public int QuestionMarkRemovalRate { get; set; } = 60;

    public ICollection<Document> Documents { get; set; } = new List<Document>();
}
