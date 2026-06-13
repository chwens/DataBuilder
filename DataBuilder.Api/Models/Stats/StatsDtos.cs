namespace DataBuilder.Api.Models.Stats;

/// <summary>
/// 类型分布数据点（饼图）：SP sp_stat_qa_type 输出 Name + Value。
/// </summary>
public record CategoryValueDto(string Name, int Value);

/// <summary>
/// 质量分布数据点（柱图）：SP sp_stat_qa_quality 输出 Score + Count。
/// </summary>
public record QualityScoreDto(int Score, int Count);