using System.Text.Json;
using Confluent.Kafka;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels;
using CP6.WebApi.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace CP6.WebApi.BackgroundServices;

/// <summary>
/// Kafka 操作日志消费者 — BackgroundService 持续拉取 cp6.operlog topic。
///
/// 操作ログは Kafka 専任。本消费者が唯一の落库担当（DB 書き込み + SignalR 推送）。
///
/// 要点：
/// 1. Confluent.Kafka 的 Consume 是阻塞调用 → 放到独立线程（Task.Run）跑拉取循环。
/// 2. EnableAutoCommit=false + 手动 Commit：处理成功才提交位移（至少一次语义）。
/// 3. IServiceScopeFactory → 后台服务里创建 Scoped 的 DbContext。
/// </summary>
public class KafkaOperLogConsumer : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<NotifyHub> _hubContext;
    private readonly IConfiguration _config;
    private readonly ILogger<KafkaOperLogConsumer> _logger;

    public KafkaOperLogConsumer(
        IServiceScopeFactory scopeFactory,
        IHubContext<NotifyHub> hubContext,
        IConfiguration config,
        ILogger<KafkaOperLogConsumer> logger)
    {
        _scopeFactory = scopeFactory;
        _hubContext = hubContext;
        _config = config;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var section = _config.GetSection("Kafka");
        var bootstrap = section["BootstrapServers"];
        if (string.IsNullOrWhiteSpace(bootstrap))
        {
            _logger.LogWarning("Kafka 未配置，Kafka OperLog Consumer 不启动");
            return Task.CompletedTask;
        }

        var topic = section["OperLogTopic"] ?? "cp6.operlog";
        var groupId = section["ConsumerGroup"] ?? "cp6-operlog-consumer";

        // 阻塞式拉取循环 → 独立长线程，避免占用线程池
        return Task.Factory.StartNew(
            () => ConsumeLoop(bootstrap, topic, groupId, stoppingToken),
            stoppingToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    private async Task ConsumeLoop(string bootstrap, string topic, string groupId, CancellationToken stoppingToken)
    {
        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = bootstrap,
            GroupId = groupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
        };

        using var consumer = new ConsumerBuilder<string, string>(consumerConfig)
            .SetErrorHandler((_, e) => _logger.LogDebug("Kafka consumer error: {Reason}", e.Reason))
            .Build();

        consumer.Subscribe(topic);
        _logger.LogInformation("Kafka OperLog Consumer 已启动，订阅 topic: {Topic} group={Group}", topic, groupId);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string>? cr;
                try
                {
                    cr = consumer.Consume(stoppingToken);
                }
                catch (ConsumeException ex)
                {
                    _logger.LogError(ex, "Kafka 消费异常");
                    continue;
                }

                if (cr?.Message?.Value == null)
                    continue;

                try
                {
                    await PersistAsync(cr.Message.Value);
                    consumer.Commit(cr);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "处理 Kafka 操作日志失败（不提交位移，下次重试）");
                    // 不 Commit → 下次重新拉取重试
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 应用正常停止
        }
        finally
        {
            consumer.Close();
            _logger.LogInformation("Kafka OperLog Consumer 已停止");
        }
    }

    private async Task PersistAsync(string json)
    {
        var log = JsonSerializer.Deserialize<Sys_OperLog>(json);
        if (log == null) return;

        // 反序列化会带回原 Id（=0 默认即可，让 DB 自增），保险起见清零
        log.Id = 0;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CP6Context>();
        db.Sys_OperLogs.Add(log);
        await db.SaveChangesAsync();

        await _hubContext.Clients.All.SendAsync("NewOperLog", new
        {
            log.UserName,
            log.HttpMethod,
            log.RequestUrl,
            log.Controller,
            log.Action,
            log.StatusCode,
            log.ElapsedMs,
            log.CreateDate
        });

        _logger.LogDebug("Kafka 操作日志已落库并推送: {User} {Method} {Url}",
            log.UserName, log.HttpMethod, log.RequestUrl);
    }
}
