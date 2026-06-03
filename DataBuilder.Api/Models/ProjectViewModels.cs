using System.ComponentModel.DataAnnotations;
using DataBuilder.Core.Entities;

namespace DataBuilder.Api.Models;

public class ProjectCreateViewModel
{
    [Required(ErrorMessage = "项目名称不能为空")]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class ProjectDetailViewModel
{
    public Project Project { get; set; } = null!;
    public List<Document> Documents { get; set; } = new();
}

public class ProjectSettingsViewModel
{
    public Project Project { get; set; } = null!;

    /// <summary>当前生效的问题生成 Prompt（已保存的自定义内容，或系统默认模板）</summary>
    public string EffectiveQuestionPrompt { get; set; } = string.Empty;

    /// <summary>当前生效的答案生成 Prompt（已保存的自定义内容，或系统默认模板）</summary>
    public string EffectiveAnswerPrompt { get; set; } = string.Empty;

    /// <summary>系统默认问题生成模板（含 {qaType}、{count} 占位符）</summary>
    public string DefaultQuestionPrompt { get; set; } = string.Empty;

    /// <summary>系统默认答案生成模板</summary>
    public string DefaultAnswerPrompt { get; set; } = string.Empty;
}
