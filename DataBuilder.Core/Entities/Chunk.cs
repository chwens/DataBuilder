using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DataBuilder.Core.Entities;

public class Chunk
{
    public int Id { get; set; }

    public int DocumentId { get; set; }

    /// <summary>
    /// chunk 在原文档中的序号
    /// </summary>
    public int Sequence { get; set; }

    public string TextContent { get; set; } = string.Empty;

    /// <summary>
    /// 分段策略: Heading / Paragraph / FixedLength
    /// </summary>
    [MaxLength(50)]
    public string Strategy { get; set; } = "Paragraph";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(DocumentId))]
    public Document? Document { get; set; }

    public ICollection<QAPair> QAPairs { get; set; } = new List<QAPair>();
}
