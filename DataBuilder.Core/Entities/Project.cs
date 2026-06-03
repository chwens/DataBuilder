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

    public ICollection<Document> Documents { get; set; } = new List<Document>();
}
