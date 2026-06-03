using System.ComponentModel.DataAnnotations;

namespace DataBuilder.Core.Entities;

public class LLMConfig
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Provider { get; set; } = string.Empty;  // 提供商名称，如 OpenRouter

    [Required, MaxLength(500)]
    public string ApiUrl { get; set; } = string.Empty;    // 接口地址

    [Required]
    public string ApiKeyEncrypted { get; set; } = string.Empty;  // AES-256 加密后的 API Key

    [Required, MaxLength(200)]
    public string ModelId { get; set; } = string.Empty;   // 模型标识符，如 deepseek-chat

    [Required, MaxLength(200)]
    public string ModelName { get; set; } = string.Empty; // 自定义展示名称

    [MaxLength(100)]
    public string ModelLabel { get; set; } = "语言模型";  // 厂商名：deepseek, minimax, doubao, 智谱 等

    public float Temperature { get; set; } = 0.7f;
    public int MaxTokens { get; set; } = 8192;
    public float TopP { get; set; } = 0.9f;

    public bool IsDeleted { get; set; } = false;  // 软删除标记

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Project> Projects { get; set; } = new List<Project>();
}
