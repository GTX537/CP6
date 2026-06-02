using System.Text;
using System.Text.Json;
using CP6.Core.Utilities;
using CP6.WebApi.Hubs;
using Microsoft.AspNetCore.SignalR;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace CP6.WebApi.BackgroundServices;

/// <summary>
/// 業務イベント通知コンシューマ — RabbitMQ の cp6.notification キューを購読し、
/// SignalR(NotifyHub) で全クライアントへ「BusinessNotification」を fanout する。
///
/// ■ なぜ RabbitMQ か（機能適性）
///   業務通知/アラートは「低頻度・確実に届けたい・複数宛先へ振り分けたい・失敗時リトライ」。
///   per-message ack / dead-letter / ルーティングを持つ RabbitMQ が適任。
///   （高スループットな操作ログは Kafka 専任、と役割を分離。）
///
/// ■ 拡張ポイント
///   現状は SignalR への配信のみ。将来メール/外部 Webhook への配信もこの consumer に足せる。
/// </summary>
public class NotificationConsumer : BackgroundService
{
    private readonly IHubContext<NotifyHub> _hubContext;
    private readonly IConfiguration _config;
    private readonly ILogger<NotificationConsumer> _logger;
    private IConnection? _connection;
    private IChannel? _channel;

    public NotificationConsumer(
        IHubContext<NotifyHub> hubContext,
        IConfiguration config,
        ILogger<NotificationConsumer> logger)
    {
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
            _logger.LogWarning("RabbitMQ 未配置，通知 Consumer 不启动");
            return;
        }

        // 等待 RabbitMQ 就绪（容器启动可能有延迟）
        try { await Task.Delay(3000, stoppingToken); }
        catch (OperationCanceledException) { return; }

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

            await _channel.QueueDeclareAsync(
                queue: RabbitMQService.NotificationQueue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                cancellationToken: stoppingToken);

            await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 10, global: false, cancellationToken: stoppingToken);

            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.ReceivedAsync += async (sender, ea) =>
            {
                try
                {
                    var json = Encoding.UTF8.GetString(ea.Body.ToArray());
                    var notice = JsonSerializer.Deserialize<BusinessNotification>(json);

                    if (notice != null)
                    {
                        // 全クライアントへ業務通知を配信
                        await _hubContext.Clients.All.SendAsync("BusinessNotification", new
                        {
                            notice.EventType,
                            notice.Level,
                            notice.Title,
                            notice.Message,
                            notice.Source,
                            notice.RefNo,
                            notice.CreateDate
                        });

                        // 将来：notice.Level == "Error"/"Warning" ならメール/Webhook へも配信、など
                        _logger.LogDebug("業務通知を配信: {Event} {Title}", notice.EventType, notice.Title);
                    }

                    await _channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "処理業務通知消息失败");
                    // Nack + requeue = 重新放回队列等待重试
                    await _channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true);
                }
            };

            await _channel.BasicConsumeAsync(
                queue: RabbitMQService.NotificationQueue,
                autoAck: false,
                consumer: consumer,
                cancellationToken: stoppingToken);

            _logger.LogInformation("通知 Consumer 已启动，监听队列: {Queue}", RabbitMQService.NotificationQueue);

            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // 应用正常停止
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "通知 Consumer 异常退出");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel != null) await _channel.CloseAsync(cancellationToken);
        if (_connection != null) await _connection.CloseAsync(cancellationToken);
        _logger.LogInformation("通知 Consumer 已停止");
        await base.StopAsync(cancellationToken);
    }
}
