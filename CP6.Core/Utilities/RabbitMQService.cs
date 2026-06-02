using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CP6.Core.Utilities;

/// <summary>
/// RabbitMQ メッセージサービス — 業務イベント通知/アラートの生産者。
///
/// ■ 役割分担（機能適性で使い分け）
///   - 操作ログ（高スループット・append-only・保持/再生）→ Kafka 専任。
///   - 業務イベント通知/アラート（低頻度・確実配信・ルーティング・リトライ）→ 本サービス(RabbitMQ)。
///
/// RabbitMQ が通知に向く理由：per-message ack、柔軟なルーティング、再試行、dead-letter。
///
/// ■ 接続方針
///   - IConnection は長連接 → Singleton で複用。
///   - IChannel は軽量 → 発行ごとに作成/破棄。
///   - 未配置/接続失敗時は IsConnected=false で安全に縮退（業務は止めない）。
/// </summary>
public class RabbitMQService : INotificationPublisher, IDisposable
{
    private readonly IConnection? _connection;
    private readonly ILogger<RabbitMQService> _logger;
    private bool _isConnected;

    /// <summary>業務イベント通知キュー。</summary>
    public const string NotificationQueue = "cp6.notification";

    public RabbitMQService(IConfiguration config, ILogger<RabbitMQService> logger)
    {
        _logger = logger;

        var section = config.GetSection("RabbitMQ");
        var hostName = section["HostName"];

        if (string.IsNullOrEmpty(hostName))
        {
            _logger.LogWarning("RabbitMQ 未配置，通知功能跳过");
            return;
        }

        try
        {
            var factory = new ConnectionFactory
            {
                HostName = hostName,
                Port = int.Parse(section["Port"] ?? "5672"),
                UserName = section["UserName"] ?? "guest",
                Password = section["Password"] ?? "guest"
            };

            // 长连接（应用生命周期内复用）
            _connection = factory.CreateConnectionAsync().GetAwaiter().GetResult();
            _isConnected = true;
            _logger.LogInformation("RabbitMQ 连接成功: {Host}:{Port}", hostName, factory.Port);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("RabbitMQ 连接失败（通知功能降级跳过）: {Error}", ex.Message);
        }
    }

    /// <summary>virtual：Moq でオーバーライド可能（テスト容易性）。</summary>
    public virtual bool IsConnected => _isConnected;

    /// <summary>INotificationPublisher：業務イベント通知を通知キューへ発行。</summary>
    public virtual Task PublishNotificationAsync(BusinessNotification notification)
        => PublishAsync(NotificationQueue, notification);

    /// <summary>
    /// 指定キューへメッセージを発行する汎用メソッド。
    /// Channel は線程不安全 → 毎回作成。キュー宣言は冪等。
    /// </summary>
    public virtual async Task PublishAsync<T>(string queueName, T message)
    {
        if (!_isConnected || _connection == null)
        {
            _logger.LogDebug("RabbitMQ 未连接，消息跳过");
            return;
        }

        try
        {
            await using var channel = await _connection.CreateChannelAsync();

            // durable: true → 队列持久化（重启不丢）
            await channel.QueueDeclareAsync(
                queue: queueName,
                durable: true,
                exclusive: false,
                autoDelete: false);

            var json = JsonSerializer.Serialize(message);
            var body = Encoding.UTF8.GetBytes(json);

            // 消息持久化（重启不丢）
            var props = new BasicProperties
            {
                DeliveryMode = DeliveryModes.Persistent
            };

            await channel.BasicPublishAsync(
                exchange: "",
                routingKey: queueName,
                mandatory: false,
                basicProperties: props,
                body: body);

            _logger.LogDebug("消息已发送到队列 {Queue}", queueName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送消息到 {Queue} 失败", queueName);
        }
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }
}
