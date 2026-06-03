using System.ComponentModel.DataAnnotations;

namespace DataBuilder.Api.Models;

public class LLMConfigListViewModel
{
    public List<LLMConfigItemViewModel> Configs { get; set; } = new();
}

public class LLMConfigItemViewModel
{
    public int Id { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string ModelLabel { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public int ProjectCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class LLMConfigEditViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "提供商不能为空")]
    [MaxLength(100)]
    public string Provider { get; set; } = string.Empty;

    [Required(ErrorMessage = "接口地址不能为空")]
    [MaxLength(500)]
    [Url(ErrorMessage = "请输入有效的 URL 地址，需包含 https://")]
    public string ApiUrl { get; set; } = string.Empty;

    public string? ApiKey { get; set; }

    [Required(ErrorMessage = "模型 ID 不能为空")]
    [MaxLength(200)]
    public string ModelId { get; set; } = string.Empty;

    [Required(ErrorMessage = "模型名称不能为空")]
    [MaxLength(200)]
    public string ModelName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string ModelLabel { get; set; } = "语言模型";

    [Range(0, 2, ErrorMessage = "Temperature 取值范围 0-2")]
    public float Temperature { get; set; } = 0.7f;

    [Range(1, 131072, ErrorMessage = "MaxTokens 取值范围 1-131072")]
    public int MaxTokens { get; set; } = 8192;

    [Range(0, 1, ErrorMessage = "TopP 取值范围 0-1")]
    public float TopP { get; set; } = 0.9f;

    public bool IsNew => Id == 0;
}
