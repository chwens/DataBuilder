using DataBuilder.Core.Entities;

namespace DataBuilder.Api.Models;

public class QAPreviewViewModel
{
    public Document Document { get; set; } = null!;
    public List<QAPair> QAPairs { get; set; } = new();
    public string? QaType { get; set; }
    public int CountPerChunk { get; set; } = 3;
}

public class QAEditViewModel
{
    public QAPair QAPair { get; set; } = null!;
    public string? ReturnUrl { get; set; }
}
