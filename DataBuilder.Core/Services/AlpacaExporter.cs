using System.Text;
using System.Text.Json;
using DataBuilder.Core.Entities;
using DataBuilder.Core.Interfaces;

namespace DataBuilder.Core.Services;

public class AlpacaExporter : IAlpacaExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public Task<byte[]> ExportAsync(IEnumerable<QAPair> qaPairs)
    {
        var sb = new StringBuilder();
        foreach (var qa in qaPairs)
        {
            var obj = new Dictionary<string, object?>
            {
                ["instruction"] = qa.Instruction,
                ["input"] = string.IsNullOrWhiteSpace(qa.Input) ? "" : qa.Input,
                ["output"] = qa.Output
            };
            sb.AppendLine(JsonSerializer.Serialize(obj, JsonOptions));
        }

        return Task.FromResult(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    public async Task<string> ExportToFileAsync(int projectId, string outputPath)
    {
        // 需要依赖 AppDbContext，此处仅提供骨架
        await Task.CompletedTask;
        return outputPath;
    }
}
