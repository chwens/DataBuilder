using System.Collections.Concurrent;

namespace DataBuilder.Core.Services;

/// <summary>
/// 内存级生成进度跟踪器（Singleton），供 QAController 更新进度、前端轮询读取。
/// 数据按 documentId 隔离，同一文档同时只允许一个生成任务。
/// </summary>
public class GenerationProgressTracker
{
    private readonly ConcurrentDictionary<int, GenerationProgress> _progress = new();

    /// <summary>
    /// 获取或创建指定文档的进度记录
    /// </summary>
    public GenerationProgress GetOrCreate(int documentId)
    {
        return _progress.GetOrAdd(documentId, _ => new GenerationProgress());
    }

    /// <summary>
    /// 读取指定文档的进度（可能为 null）
    /// </summary>
    public GenerationProgress? Get(int documentId)
    {
        _progress.TryGetValue(documentId, out var p);
        return p;
    }

    /// <summary>
    /// 开始新任务：重置进度并标记运行中
    /// </summary>
    public void Start(int documentId, int total, string stage)
    {
        var p = GetOrCreate(documentId);
        p.IsRunning = true;
        p.IsCompleted = false;
        p.IsError = false;
        p.ErrorMessage = null;
        p.Current = 0;
        p.Total = total;
        p.Stage = stage;
    }

    /// <summary>
    /// 更新当前进度
    /// </summary>
    public void Update(int documentId, int current)
    {
        if (_progress.TryGetValue(documentId, out var p))
        {
            p.Current = current;
        }
    }

    /// <summary>
    /// 标记任务成功完成
    /// </summary>
    public void Complete(int documentId)
    {
        if (_progress.TryGetValue(documentId, out var p))
        {
            p.IsRunning = false;
            p.IsCompleted = true;
            p.Current = p.Total; // 确保进度条满
        }
    }

    /// <summary>
    /// 标记任务出错
    /// </summary>
    public void Error(int documentId, string errorMessage)
    {
        if (_progress.TryGetValue(documentId, out var p))
        {
            p.IsRunning = false;
            p.IsError = true;
            p.ErrorMessage = errorMessage;
        }
    }

    /// <summary>
    /// 清理指定文档的进度记录（用于重置）
    /// </summary>
    public void Clear(int documentId)
    {
        _progress.TryRemove(documentId, out _);
    }
}

/// <summary>
/// 单个文档的生成进度快照
/// </summary>
public class GenerationProgress
{
    /// <summary>是否正在运行</summary>
    public bool IsRunning { get; set; }
    /// <summary>是否已完成</summary>
    public bool IsCompleted { get; set; }
    /// <summary>是否出错</summary>
    public bool IsError { get; set; }
    /// <summary>错误信息</summary>
    public string? ErrorMessage { get; set; }
    /// <summary>当前进度（已处理数）</summary>
    public int Current { get; set; }
    /// <summary>总数</summary>
    public int Total { get; set; }
    /// <summary>当前阶段描述（如"生成问题"、"生成答案"）</summary>
    public string? Stage { get; set; }
    /// <summary>百分比（0-100），前端可直接使用</summary>
    public int Percent => Total > 0 ? Math.Min(100, (int)((double)Current / Total * 100)) : 0;
}
