using DataBuilder.Core.Entities;

namespace DataBuilder.Api.Models;

public class QAPreviewViewModel
{
    public Document Document { get; set; } = null!;
    public List<QAPair> QAPairs { get; set; } = new();
    public string? QaType { get; set; }
    public int CountPerChunk { get; set; } = 3;
    public string? FilterType { get; set; }
    public List<string> TypeList { get; set; } = new();
    public int TotalCount { get; set; }
    public int AnsweredCount { get; set; }
}

public class QAEditViewModel
{
    public QAPair QAPair { get; set; } = null!;
    public string? ReturnUrl { get; set; }
}

/// <summary>
/// 项目级 QA 视图共享 ViewModel。
/// 用于 QA/ProjectQuestions 和 QA/ProjectQAPairs 两个 Action 共享结构。
/// </summary>
public class ProjectQAViewModel
{
    /// <summary>所属项目（用于显示项目名等元信息）</summary>
    public Project Project { get; set; } = null!;

    /// <summary>QA 列表（已被 FilterType 过滤）</summary>
    public List<QAPair> QAPairs { get; set; } = new();

    /// <summary>当前类型筛选（null 表示全部）</summary>
    public string? FilterType { get; set; }

    /// <summary>可用类型列表（Factoid / Reasoning / Summary）</summary>
    public List<string> TypeList { get; set; } = new() { "Factoid", "Reasoning", "Summary" };

    /// <summary>问题总数（已应用 FilterType）</summary>
    public int TotalCount { get; set; }

    /// <summary>已生成答案的数量</summary>
    public int AnsweredCount { get; set; }

    /// <summary>仅显示问题视图</summary>
    public bool QuestionsOnly { get; set; }
}
