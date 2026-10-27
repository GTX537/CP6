using Amazon.S3;
using CP6.Core.Services.Pub;
using Moq;

namespace CP6.Tests.Sys;

public sealed class S3FileStoreTests
{
    [Theory]
    [InlineData(4, 3)]
    [InlineData(3, 4)]
    [InlineData(3, 2)]
    public async Task NativeRead_RejectsDeclaredOrActualLengthMismatch(int declared, int actual)
    {
        var client = new Mock<IAmazonS3>(MockBehavior.Strict);
        client.Setup(x => x.GetObjectAsync("cp6-runtime", "native-content-v1/original", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Amazon.S3.Model.GetObjectResponse { ContentLength = declared, ResponseStream = new MemoryStream(new byte[actual]) });
        var store = new S3FileStore(client.Object, "cp6-runtime");
        var error = await Record.ExceptionAsync(() => store.OpenReadBoundedAsync("native-content-v1/original", 3, CancellationToken.None));
        Assert.NotNull(error);
        Assert.True(error is IOException || error is CP6.Core.Services.Erp.OrderPriceCorrectionException { Message: "FILE_LENGTH_MISMATCH" });
    }
    [Fact]
    public async Task NativeRead_PreservesExactBoundedBytes()
    {
        byte[] original = [1, 2, 3]; var client = new Mock<IAmazonS3>(MockBehavior.Strict);
        client.Setup(x => x.GetObjectAsync("cp6-runtime", "native-content-v1/original", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Amazon.S3.Model.GetObjectResponse { ContentLength = 3, ResponseStream = new MemoryStream(original) });
        var store = new S3FileStore(client.Object, "cp6-runtime");
        await using var stream = await store.OpenReadBoundedAsync("native-content-v1/original", 3, CancellationToken.None);
        using var copy = new MemoryStream(); await stream.CopyToAsync(copy); Assert.Equal(original, copy.ToArray());
    }
    [Theory]
    [InlineData("../secret")]
    [InlineData("i18n/../../secret")]
    [InlineData("./manifest.json")]
    [InlineData("")]
    public async Task SaveAsync_RejectsUnsafeObjectKeys(string key)
    {
        var client = new Mock<IAmazonS3>(MockBehavior.Strict);
        var store = new S3FileStore(client.Object, "cp6-runtime");
        await using var content = new MemoryStream([1, 2, 3]);

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.SaveAsync(content, key));
        client.VerifyNoOtherCalls();
    }
}
