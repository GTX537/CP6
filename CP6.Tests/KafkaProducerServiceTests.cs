using Confluent.Kafka;
using CP6.Core.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CP6.Tests;

public class KafkaProducerServiceTests
{
    [Fact]
    public void Dispose_WhenFlushThrows_StillDisposesProducer()
    {
        var producer = new Mock<IProducer<string, string>>();
        var logger = new Mock<ILogger<KafkaProducerService>>();
        producer
            .Setup(x => x.Flush(It.IsAny<TimeSpan>()))
            .Throws(new InvalidOperationException("producer handle is closed"));
        var service = new KafkaProducerService(producer.Object, logger.Object);

        var exception = Record.Exception(service.Dispose);

        Assert.Null(exception);
        producer.Verify(x => x.Dispose(), Times.Once);
        VerifyLogged(logger, LogLevel.Warning, "刷新失败");
    }

    [Fact]
    public void Dispose_WhenMessagesRemain_LogsWarningAndDisposesProducer()
    {
        var producer = new Mock<IProducer<string, string>>();
        var logger = new Mock<ILogger<KafkaProducerService>>();
        producer.Setup(x => x.Flush(TimeSpan.FromSeconds(5))).Returns(3);
        var service = new KafkaProducerService(producer.Object, logger.Object);

        service.Dispose();

        producer.Verify(x => x.Dispose(), Times.Once);
        VerifyLogged(logger, LogLevel.Warning, "3");
    }

    [Fact]
    public void Dispose_WhenProducerDisposeThrows_DoesNotFailHostShutdown()
    {
        var producer = new Mock<IProducer<string, string>>();
        var logger = new Mock<ILogger<KafkaProducerService>>();
        producer.Setup(x => x.Dispose()).Throws(new ObjectDisposedException("producer"));
        var service = new KafkaProducerService(producer.Object, logger.Object);

        var exception = Record.Exception(service.Dispose);

        Assert.Null(exception);
        VerifyLogged(logger, LogLevel.Warning, "资源释放失败");
    }

    [Fact]
    public void Dispose_WhenCalledTwice_ReleasesProducerOnlyOnce()
    {
        var producer = new Mock<IProducer<string, string>>();
        var service = new KafkaProducerService(
            producer.Object,
            NullLogger<KafkaProducerService>.Instance);

        service.Dispose();
        service.Dispose();

        producer.Verify(x => x.Flush(TimeSpan.FromSeconds(5)), Times.Once);
        producer.Verify(x => x.Dispose(), Times.Once);
    }

    private static void VerifyLogged(
        Mock<ILogger<KafkaProducerService>> logger,
        LogLevel level,
        string contains)
    {
        logger.Verify(
            x => x.Log(
                level,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((value, _) => value.ToString()!.Contains(contains)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }
}
