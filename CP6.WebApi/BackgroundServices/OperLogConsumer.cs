using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Utilities;
using CP6.Entity.DomainModels;
using CP6.WebApi.Hubs;
using Microsoft.AspNetCore.SignalR;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace CP6.WebApi.BackgroundServices;

/// <summary>
/// 操作日志消费者 — BackgroundService 持续监听 RabbitMQ 队列
///
/// 面试要点：
/// 1. BackgroundService 是 .NET 的后台任务机制，随应用启动/停止
/// 2. Consumer 持续监听队列，有消息就处理（事件驱动，不轮询）
/// 3. BasicAck = 手动确认模式（处理成功才确认，失败可以重试）
/// 4. IServiceScopeFactory → 在后台服务中创建 Scoped 依赖（如 DbContext）
///
/// MOM 系统场景：
/// - 设备上报队列 → 消费者解析数据 → 存入时序数据库
/// - 报警队列 → 消费者判断规则 → 触发通知
/// </summary>
public class OperLogConsumer : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<NotifyHub> _hubContext;
    private readonly IConfiguration _config;
    private readonly ILogger<OperLogConsumer> _logger;
    private IConnection? _connection;
    private IChannel? _channel;

    public OperLogConsumer(
        IServiceScopeFactory scopeFactory,
        IHubContext<NotifyHub> hubContext,
        IConfiguration config,
        ILogger<OperLogConsumer> logger)
    {
        _scopeFactory = scopeFactory;
        _hubContext = hubContext;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var section = _config.GetSection("RabbitMQ");
        var hostName = section["HostName"];

        if (string.IsNullOrEmpty(hostName))
        {
            _logger.LogWarning("RabbitMQ 未配置，OperLog Consumer 不启动");
            return;
        }

        // 等待 RabbitMQ 就绪（容器启动可能有延迟）
        await Task.Delay(3000, stoppingToken);

        try
        {
            var factory = new ConnectionFactory
            {
                HostName = hostName,
                Port = int.Parse(section["Port"] ?? "5672"),
                UserName = section["UserName"] ?? "guest",
                Password = section["Password"] ?? "guest"
            };

            _connection = await factory.CreateConnectionAsync(stoppingToken);
            _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);

            // 声明队列（与 Producer 一致）
            await _channel.QueueDeclareAsync(
                queue: RabbitMQService.OperLogQueue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                cancellationToken: stoppingToken);

            // prefetchCount = 10：一次最多预取 10 条消息，防止消费者过载
            await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 10, global: false, cancellationToken: stoppingToken);

            // 创建异步消费者
            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.ReceivedAsync += async (sender, ea) =>
            {
                try
                {
                    var json = Encoding.UTF8.GetString(ea.Body.ToArray());
                    var log = JsonSerializer.Deserialize<Sys_OperLog>(json);

                    if (log != null)
                    {
                        // 创建 Scope 获取 DbContext（BackgroundService 不能直接注入 Scoped 服务）
                        using var scope = _scopeFactory.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<CP6Context>();

                        db.Sys_OperLogs.Add(log);
                        await db.SaveChangesAsync();

                        // SignalR 实时推送：通知所有连接的客户端有新操作日志
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

                        _logger.LogDebug("操作日志已消费并推送: {User} {Method} {Url}",
                            log.UserName, log.HttpMethod, log.RequestUrl);
                    }

                    // 手动确认（消息处理成功后才从队列删除）
                    await _channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "处理操作日志消息失败");
                    // Nack + requeue = 重新放回队列等待重试
                    await _channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true);
                }
            };

            // 开始消费（autoAck: false = 手动确认模式）
            await _channel.BasicConsumeAsync(
                queue: RabbitMQService.OperLogQueue,
                autoAck: false,
                consumer: consumer,
                cancellationToken: stoppingToken);

            _logger.LogInformation("OperLog Consumer 已启动，监听队列: {Queue}", RabbitMQService.OperLogQueue);

            // 保持运行直到应用停止
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // 应用正常停止
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OperLog Consumer 异常退出");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel != null) await _channel.CloseAsync(cancellationToken);
        if (_connection != null) await _connection.CloseAsync(cancellationToken);
        _logger.LogInformation("OperLog Consumer 已停止");
        await base.StopAsync(cancellationToken);
    }
}
