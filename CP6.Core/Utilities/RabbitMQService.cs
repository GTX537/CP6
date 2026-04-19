using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CP6.Core.Utilities;

/// <summary>
/// RabbitMQ 消息服务 — 生产者（发送消息）
///
/// 面试要点：
/// 1. RabbitMQ 核心概念：
///    - Producer（生产者）：发送消息的程序
///    - Exchange（交换机）：路由消息到队列（Direct/Fanout/Topic）
///    - Queue（队列）：存储消息，FIFO
///    - Consumer（消费者）：接收并处理消息的程序
/// 2. 消息队列的价值：解耦、异步、削峰
///    - MOM 系统中：设备上报数据 → MQ → 多个消费者分别处理（存储/报警/统计）
/// 3. IConnection 是长连接，应该复用（注册为 Singleton）
///    IChannel 是轻量的，每个操作创建/复用
/// </summary>
public class RabbitMQService : IDisposable
{
    private readonly IConnection? _connection;
    private readonly ILogger<RabbitMQService> _logger;
    private bool _isConnected;

    // 队列名称常量
    public const string OperLogQueue = "cp6.operlog";

    public RabbitMQService(IConfiguration config, ILogger<RabbitMQService> logger)
    {
        _logger = logger;

        var section = config.GetSection("RabbitMQ");
        var hostName = section["HostName"];

        if (string.IsNullOrEmpty(hostName))
        {
            _logger.LogWarning("RabbitMQ 未配置，消息队列功能跳过");
            return;
        }

        try
        {
            // 创建连接工厂
            var factory = new ConnectionFactory
            {
                HostName = hostName,
                Port = int.Parse(section["Port"] ?? "5672"),
                UserName = section["UserName"] ?? "guest",
                Password = section["Password"] ?? "guest"
            };

            // 创建长连接（应用生命周期内复用）
            _connection = factory.CreateConnectionAsync().GetAwaiter().GetResult();
            _isConnected = true;
            _logger.LogInformation("RabbitMQ 连接成功: {Host}:{Port}", hostName, factory.Port);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("RabbitMQ 连接失败（消息队列功能降级为同步模式）: {Error}", ex.Message);
        }
    }

    /// <summary>
    /// virtual 让 Moq 可以 Override（面试要点：可测试性设计）
    /// </summary>
    public virtual bool IsConnected => _isConnected;

    /// <summary>
    /// 发送消息到指定队列
    ///
    /// 流程：创建 Channel → 声明队列 → 发布消息
    /// - Channel 是线程不安全的，每次操作创建新的
    /// - 队列声明是幂等的（已存在就跳过）
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
            // 创建 Channel（轻量级，用完关闭）
            await using var channel = await _connection.CreateChannelAsync();

            // 声明队列（幂等操作）
            // durable: true → 队列持久化（RabbitMQ 重启后不丢失）
            // exclusive: false → 允许多个消费者
            // autoDelete: false → 没有消费者时不自动删除
            await channel.QueueDeclareAsync(
                queue: queueName,
                durable: true,
                exclusive: false,
                autoDelete: false);

            // 序列化消息为 JSON
            var json = JsonSerializer.Serialize(message);
            var body = Encoding.UTF8.GetBytes(json);

            // 设置消息持久化（RabbitMQ 重启后消息不丢失）
            var props = new BasicProperties
            {
                DeliveryMode = DeliveryModes.Persistent
            };

            // 发布消息
            await channel.BasicPublishAsync(
                exchange: "",           // 默认交换机（Direct 模式）
                routingKey: queueName,  // 路由键 = 队列名
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
