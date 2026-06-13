using System.Text.RegularExpressions;

namespace DataBuilder.Core.Services;

/// <summary>
/// LLM 响应中 JSON 片段提取正则的集中定义。
/// LLMService 与 TopicTaggerService 共用，避免重复声明。
/// </summary>
internal static class LlmJsonRegex
{
    /// <summary>
    /// 匹配 JSON 数组（[ ... ]，非贪婪）。
    /// </summary>
    public static readonly Regex Array = new(
        @"\[[\s\S]*?\]", RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>
    /// 匹配最外层 JSON 对象（{ ... }，处理任意层嵌套）。
    /// 使用 .NET 平衡组语法：(?:[^{}]|(?&o)|(?<-o>))*(?(o)(?!))，从第一个 { 配对到对应的 }。
    /// 解决 LLM 返回内容包含嵌套对象时被非贪婪匹配截断的问题。
    /// </summary>
    public static readonly Regex Object = new(
        @"\{(?:[^{}]|(?<o>\{)|(?<-o>\}))*(?(o)(?!))\}",
        RegexOptions.Compiled);
}