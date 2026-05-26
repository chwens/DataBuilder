using System.Text.Json.Serialization;

namespace DataBuilder.Core.DTOs;

/// <summary>
/// OpenAI-compatible API 请求体
/// </summary>
public class ChatCompletionRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = "MiniMax-M2.5";

    [JsonPropertyName("messages")]
    public List<ChatMessage> Messages { get; set; } = new();

    [JsonPropertyName("temperature")]
    public float Temperature { get; set; } = 0.7f;

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; } = 2048;
}

public class ChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "user";

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
}

/// <summary>
/// OpenAI-compatible API 响应体（核心字段）
/// </summary>
public class ChatCompletionResponse
{
    [JsonPropertyName("choices")]
    public List<ChatChoice> Choices { get; set; } = new();
}

public class ChatChoice
{
    [JsonPropertyName("message")]
    public ChatResponseMessage Message { get; set; } = new();
}

public class ChatResponseMessage
{
    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
}
