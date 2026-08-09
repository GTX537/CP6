using Amazon.S3;
using CP6.Core.Services.Pub;
using Moq;

namespace CP6.Tests.Sys;

public sealed class S3FileStoreTests
{
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
