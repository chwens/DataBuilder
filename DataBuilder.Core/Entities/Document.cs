using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DataBuilder.Core.Entities;

public class Document
{
    public int Id { get; set; }

    public int ProjectId { get; set; }

    [Required, MaxLength(500)]
    public string FileName { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 解析状态: Uploaded → Parsing → Parsed → Generating → Done
    /// </summary>
    public DocumentStatus Status { get; set; } = DocumentStatus.Uploaded;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(ProjectId))]
    public Project? Project { get; set; }

    public ICollection<Chunk> Chunks { get; set; } = new List<Chunk>();
}
