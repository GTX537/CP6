using CP6.Client.Core;

namespace CP6.Client.Tests;

public sealed class ClientUtilityTests
{
    [Theory]
    [InlineData("1.0.0", "1.0.0", 0)]
    [InlineData("1.0.1", "1.0.0", 1)]
    [InlineData("1.0.0-beta", "1.1.0", -1)]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.10", -1)]
    [InlineData("1.0.0-beta.10", "1.0.0-beta.2", 1)]
    [InlineData("1.0.0", "1.0.0-rc.1", 1)]
    public void Compares_Client_Versions(string left, string right, int sign)
        => Assert.Equal(sign, Math.Sign(ClientBootstrapService.CompareVersions(left, right)));

    [Fact]
    public void Redacts_Secrets_Without_Removing_Normal_Context()
    {
        var text = SensitiveDataRedactor.Redact(
            "device=android-1 access_token=secret refreshToken:other warehouse=WH1");
        Assert.Contains("device=android-1", text);
        Assert.Contains("warehouse=WH1", text);
        Assert.DoesNotContain("secret", text);
        Assert.DoesNotContain("other", text);
    }

    [Fact]
    public async Task Pkce_Verifier_Is_Consumed_Once()
    {
        var store = new MemoryPkceVerifierStore();
        await store.WriteAsync("verifier");

        Assert.Equal("verifier", await store.TakeAsync());
        Assert.Null(await store.TakeAsync());
    }
}
