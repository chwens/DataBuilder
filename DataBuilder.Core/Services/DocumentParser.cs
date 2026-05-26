using System.Text.RegularExpressions;
using DataBuilder.Core.Entities;
using DataBuilder.Core.Interfaces;

namespace DataBuilder.Core.Services;

public class DocumentParser : IDocumentParser
{
    public List<Chunk> Parse(string fileName, string content, string strategy = "Heading", int chunkSize = 2000)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (ext != ".md" && ext != ".txt")
            throw new NotSupportedException($"不支持的文件格式: {ext}，仅支持 .md 和 .txt");

        var chunks = strategy switch
        {
            "Heading" => ParseByHeading(content),
            "Paragraph" => ParseByParagraph(content),
            "FixedLength" => ParseByFixedLength(content, chunkSize),
            _ => ParseByHeading(content)
        };

        for (int i = 0; i < chunks.Count; i++)
        {
            chunks[i].Sequence = i;
            chunks[i].Strategy = strategy;
        }

        return chunks;
    }

    /// <summary>
    /// 按 ## 标题分段（适合 .md 文件）
    /// </summary>
    private static List<Chunk> ParseByHeading(string content)
    {
        var chunks = new List<Chunk>();
        var sections = Regex.Split(content, @"(?=^#{1,3}\s)", RegexOptions.Multiline);

        foreach (var section in sections)
        {
            var trimmed = section.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            chunks.Add(new Chunk { TextContent = trimmed });
        }

        // 如果没有标题，退化为按段落分段。
        // 注意：回退时返回的 Chunk.Strategy 仍为 null，由外层 Parse() 统一赋值为原始 strategy 参数值。
        if (chunks.Count <= 1)
            return ParseByParagraph(content);

        return chunks;
    }

    /// <summary>
    /// 按双换行分段
    /// </summary>
    private static List<Chunk> ParseByParagraph(string content)
    {
        var chunks = new List<Chunk>();
        var paragraphs = content.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries);

        var accumulator = new System.Text.StringBuilder();
        foreach (var para in paragraphs)
        {
            var trimmed = para.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            if (accumulator.Length + trimmed.Length > 3000 && accumulator.Length > 0)
            {
                chunks.Add(new Chunk { TextContent = accumulator.ToString().Trim() });
                accumulator.Clear();
            }

            if (accumulator.Length > 0) accumulator.Append("\n\n");
            accumulator.Append(trimmed);
        }

        if (accumulator.Length > 0)
            chunks.Add(new Chunk { TextContent = accumulator.ToString().Trim() });

        return chunks;
    }

    /// <summary>
    /// 按固定字符数分段
    /// </summary>
    private static List<Chunk> ParseByFixedLength(string content, int chunkSize)
    {
        var chunks = new List<Chunk>();
        int i = 0;
        while (i < content.Length)
        {
            var length = Math.Min(chunkSize, content.Length - i);
            var end = i + length;

            // 尝试在断句字符处断开，避免截断句子
            if (end < content.Length)
            {
                var breakChars = new[] { '。', '.', '？', '?', '！', '!', '\n' };
                var bestBreak = -1;
                foreach (var ch in breakChars)
                {
                    var pos = content.LastIndexOf(ch, end, Math.Min(200, length));
                    if (pos > bestBreak) bestBreak = pos;
                }
                if (bestBreak > i) end = bestBreak + 1;
            }

            chunks.Add(new Chunk
            {
                TextContent = content[i..end].Trim()
            });
            i = end;
        }
        return chunks;
    }
}
