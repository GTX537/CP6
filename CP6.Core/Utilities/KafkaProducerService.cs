using System.Text.Json;
using Confluent.Kafka;
using CP6.Entity.DomainModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CP6.Core.Utilities;

/// <summary>
/// Kafka 操作日志生产者 — 实现 IOperLogTransport，与 RabbitMQService 并存。
///
/// 要点：
/// 1. IProducer 是线程安全的长生命周期对象 → 注册为 Singleton，复用。
/// 2. librdkafka 内部维护连接/重试，ProduceAsync 失败由调用方（OperLogFilter）吞掉，
///    不影响业务接口；其它通道（RabbitMQ）仍可投递。
/// 3. 未配置 Kafka:BootstrapServers → 构造安全退出，IsConnected=false，生产者跳过本通道。
/// 4. Key 用 UserName，保证同一用户的操作进同一分区，保留先后顺序。
/// </summary>
public class KafkaProducerService : IOperLogTransport, IDisposable
{
    private readonly IProducer<string, string>? _producer;
    private readonly ILogger<KafkaProducerService> _logger;
    private readonly string _topic;
    private readonly bool _configured;

    public KafkaProducerService(IConfiguration config, ILogger<KafkaProducerService> logger)
    {
        _logger = logger;

        var section = config.GetSection("Kafka");
        var bootstrap = section["BootstrapServers"];
        _topic = section["OperLogTopic"] ?? "cp6.operlog";

        if (string.IsNullOrWhiteSpace(bootstrap))
        {
            _logger.LogWarning("Kafka 未配置（Kafka:BootstrapServers 为空），Kafka 通道跳过");
            return;
        }

        try
        {
            var producerConfig = new ProducerConfig
            {
                BootstrapServers = bootstrap,
                // 连接失败时不要无限阻塞；交付超时后 ProduceAsync 抛出，调用方吞掉
                MessageTimeoutMs = 5000,
                // 投递确认：leader 落盘即确认（日志场景吞吐优先，可接受）
                Acks = Acks.Leader,
                EnableIdempotence = false,
            };

            _producer = new ProducerBuilder<string, string>(producerConfig)
                .SetErrorHandler((_, e) =>
                    _logger.LogDebug("Kafka producer error: {Reason}", e.Reason))
                .Build();

            _configured = true;
            _logger.LogInformation("Kafka 生产者已初始化: {Bootstrap} topic={Topic}", bootstrap, _topic);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Kafka 生产者初始化失败（Kafka 通道降级跳过）: {Error}", ex.Message);
        }
    }

    public string Name => "Kafka";

    /// <summary>已配置且生产者创建成功即视为就绪。</summary>
    public virtual bool IsConnected => _configured && _producer != null;

    public virtual async Task PublishAsync(Sys_OperLog log)
    {
        if (!IsConnected)
        {
            _logger.LogDebug("Kafka 未就绪，消息跳过");
            return;
        }

        try
        {
            var json = JsonSerializer.Serialize(log);
            await _producer!.ProduceAsync(_topic, new Message<string, string>
            {
                Key = log.UserName ?? "anonymous",
                Value = json
            });
            _logger.LogDebug("操作日志已投递到 Kafka topic {Topic}", _topic);
        }
        catch (Exception ex)
        {
            // 旁路逻辑：投递失败不抛给业务接口
            _logger.LogError(ex, "投递操作日志到 Kafka 失败");
        }
    }

    public void Dispose()
    {
        try
        {
            // 等待在途消息发完（最多 5s），再释放
            _producer?.Flush(TimeSpan.FromSeconds(5));
            _producer?.Dispose();
        }
        catch
        {
            // 忽略销毁时的瞬态与 handle closed 异常，保证 host 安全退出
        }
    }
}
