using DataBuilder.Core.Entities;

namespace DataBuilder.Core.Interfaces;

/// <summary>
/// 文档解析服务 — 解析 .md/.txt 文件并按策略分段
/// </summary>
public interface IDocumentParser
{
    /// <summary>
    /// 解析文档内容为 chunk 列表
    /// </summary>
    /// <param name="fileName">文件名（用于判断格式）</param>
    /// <param name="content">原始文本内容</param>
    /// <param name="strategy">分段策略: Heading / Paragraph / FixedLength</param>
    /// <param name="chunkSize">FixedLength 策略时每段字符数</param>
    List<Chunk> Parse(string fileName, string content, string strategy = "Heading", int chunkSize = 2000);
}
