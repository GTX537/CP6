using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace CP6.Crm.SourceFence.Tests;

public sealed class ActualSourceFreezeRequestTests
{
    [Theory]
    [InlineData("{\"Format\":\"unknown\"}")]
    [InlineData("{\"Format\":null}")]
    [InlineData("{\"Unexpected\":1}")]
    [InlineData("{\"RunId\":null,\"RunId\":null}")]
    [InlineData("null")]
    public async Task Matching_file_hash_does_not_authorize_ambiguous_or_unknown_json(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), "C04A_Request_Test_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            await File.WriteAllBytesAsync(path, bytes);
            var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var error = await Assert.ThrowsAsync<SourceFenceException>(() => ActualSourceFreezer.ReadRequestAsync(path, digest));
            Assert.Equal("C04A_REQUEST_INVALID_JSON", error.Code);
        }
        finally { File.Delete(path); }
    }
}
