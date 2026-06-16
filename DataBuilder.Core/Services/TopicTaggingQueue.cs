using System.Threading.Channels;
using DataBuilder.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DataBuilder.Core.Services;

/// <summary>
/// 主题打标任务队列接口 — Controller 把 documentId 推入队列后立即返回。
/// </summary>
public interface ITopicTaggingQueue
{
    /// <summary>
    /// 把一个 documentId 推入后台打标队列。Channel 满时异步等待腾出位置，
    /// 不会静默丢失；channel 已关闭或被取消时返回 false。
    /// </summary>
    /// <returns>true 表示成功入队；false 表示 channel 已关闭/被取消或写入失败。</returns>
    Task<bool> EnqueueAsync(int documentId);
}

/// <summary>
/// 主题打标后台任务 — 消费 Channel 中的 documentId 列表，
/// 异步调用 TopicTaggerService.ClusterDocumentAsync() 完成两轮 LLM 打标。
///
/// 设计要点：
/// - 实现 IHostedService（通过继承 BackgroundService），由 ASP.NET Core 启动时自动运行。
/// - 内部用 System.Threading.Channels（有界，容量 100）缓冲任务，FullMode=Wait 防止任务被静默丢弃。
/// - 每个 documentId 在独立的 DI Scope 中执行（因为 AppDbContext/TopicTaggerService 是 Scoped）。
/// - LLM 调用失败时 NLog 记录错误，但不影响后续任务消费。
/// </summary>
public class TopicTaggingQueue : BackgroundService, ITopicTaggingQueue
{
    private const int QueueCapacity = 100;

    private readonly Channel<int> _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TopicTaggingQueue> _logger;

    public TopicTaggingQueue(
        IServiceScopeFactory scopeFactory,
        ILogger<TopicTaggingQueue> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        // 有界 channel（容量 100）：FullMode=Wait — 满时 WriteAsync 异步等待消费者腾出位置，
        // 绝不静默丢失 documentId；调用方通过 await 接收真实结果。
        _channel = Channel.CreateBounded<int>(new BoundedChannelOptions(QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    /// <summary>
    /// 异步写入 Channel。Channel 满时 WriteAsync 会异步等待腾出位置（不阻塞线程）。
    /// </summary>
    /// <returns>true 表示成功入队；false 表示 channel 已关闭或被取消。</returns>
    public async Task<bool> EnqueueAsync(int documentId)
    {
        try
        {
            await _channel.Writer.WriteAsync(documentId);
            _logger.LogInformation("TopicTaggingQueue.EnqueueAsync 成功: DocumentId={DocumentId}", documentId);
            return true;
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("TopicTaggingQueue.EnqueueAsync 被取消: DocumentId={DocumentId}", documentId);
            return false;
        }
        catch (ChannelClosedException)
        {
            _logger.LogError("TopicTaggingQueue.EnqueueAsync 失败：channel 已关闭。DocumentId={DocumentId}", documentId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TopicTaggingQueue.EnqueueAsync 异常: DocumentId={DocumentId}", documentId);
            return false;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TopicTaggingQueue 后台服务已启动。");

        // 循环消费 Channel，直到收到取消信号
        try
        {
            await foreach (var documentId in _channel.Reader.ReadAllAsync(stoppingToken))
            {
                await ProcessOneAsync(documentId, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // 正常停机
        }
        catch (ChannelClosedException)
        {
            // Channel 主动 TryComplete 后正常退出循环
        }
        finally
        {
            _logger.LogInformation("TopicTaggingQueue 后台服务已停止。");
        }
    }

    /// <summary>
    /// 处理一个 documentId：在独立 Scope 中调用 TopicTaggerService.ClusterDocumentAsync。
    /// 任何异常都被捕获并记录，避免 BackgroundService 崩溃。
    /// </summary>
    private async Task ProcessOneAsync(int documentId, CancellationToken stoppingToken)
    {
        try
        {
            // 在独立 Scope 中解析 Scoped 服务（AppDbContext、TopicTaggerService）
            using var scope = _scopeFactory.CreateScope();
            var tagger = scope.ServiceProvider.GetRequiredService<ITopicTaggerService>();

            _logger.LogInformation("TopicTaggingQueue 开始处理: DocumentId={DocumentId}", documentId);
            await tagger.ClusterDocumentAsync(documentId);
            _logger.LogInformation("TopicTaggingQueue 处理完成: DocumentId={DocumentId}", documentId);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 停机时让出，不记录为错误
            _logger.LogInformation("TopicTaggingQueue 任务被取消: DocumentId={DocumentId}", documentId);
        }
        catch (Exception ex)
        {
            // LLM/网络/解析等任何异常都吞掉，记录后继续消费下一个任务
            _logger.LogError(ex, "TopicTaggingQueue 处理失败: DocumentId={DocumentId}", documentId);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // 标记 channel 为完成，让 ExecuteAsync 的 foreach 正常退出
        _channel.Writer.TryComplete();
        await base.StopAsync(cancellationToken);
    }
}
