using DataBuilder.Core.Entities;

namespace DataBuilder.Core.Interfaces;

/// <summary>
/// Alpaca JSONL 导出服务
/// </summary>
public interface IAlpacaExporter
{
    /// <summary>
    /// 将问答对列表导出为 Alpaca 格式的 JSONL 字节流
    /// </summary>
    Task<byte[]> ExportAsync(IEnumerable<QAPair> qaPairs);

    /// <summary>
    /// 导出项目中所有问答对到文件
    /// </summary>
    Task<string> ExportToFileAsync(int projectId, string outputPath);
}
