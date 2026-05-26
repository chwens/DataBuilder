using DataBuilder.Core.Entities;

namespace DataBuilder.Api.Models;

public class DocumentUploadViewModel
{
    public int ProjectId { get; set; }
    public string ProjectName { get; set; } = string.Empty;
}

public class DocumentChunksViewModel
{
    public Document Document { get; set; } = null!;
    public List<Chunk> Chunks { get; set; } = new();
}
