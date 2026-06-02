using CP6.Entity.DomainModels;

namespace CP6.Core.Utilities;

/// <summary>
/// 操作日志「传输中间件」抽象。
///
/// 设计意图：把"用什么消息中间件投递操作日志"从生产者（OperLogFilter）里解耦出来。
/// 这样 Kafka / RabbitMQ 可以并存，生产者按配置 OperLog:Transports 决定双写到哪些通道，
/// 落库责任由 OperLog:PrimaryTransport 指定的那个消费者承担（避免重复写 DB）。
/// </summary>
public interface IOperLogTransport
{
    /// <summary>通道名（"Kafka" / "RabbitMQ"），与配置项大小写无关匹配。</summary>
    string Name { get; }

    /// <summary>通道是否就绪（已配置且连接成功）。未就绪则生产者跳过该通道。</summary>
    bool IsConnected { get; }

    /// <summary>投递一条操作日志到该中间件。</summary>
    Task PublishAsync(Sys_OperLog log);
}
