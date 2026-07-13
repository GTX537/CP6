// CP6.Tests/Wf/WfApiKeyHelperTests.cs
using CP6.Core.Services.Wf;
using Xunit;

public class WfApiKeyHelperTests
{
    [Fact]
    public void NewRawKey_Is32ByteHighEntropy_Base64Url()
    {
        var a = WfApiKeyHelper.NewRawKey();
        var b = WfApiKeyHelper.NewRawKey();
        Assert.NotEqual(a, b);
        Assert.True(a.Length >= 43);                       // 32 字节 base64url ≈ 43 字符
        Assert.DoesNotContain("+", a); Assert.DoesNotContain("/", a); Assert.DoesNotContain("=", a);
    }

    [Fact]
    public void HashOf_Sha256Hex_64Chars_Deterministic()
    {
        var h1 = WfApiKeyHelper.HashOf("k");
        var h2 = WfApiKeyHelper.HashOf("k");
        Assert.Equal(h1, h2);
        Assert.Equal(64, h1.Length);
        Assert.NotEqual("k", h1);
    }

    [Fact]
    public void Verify_RoundTrip_True_WrongKey_False()
    {
        var raw = WfApiKeyHelper.NewRawKey();
        var hash = WfApiKeyHelper.HashOf(raw);
        Assert.True(WfApiKeyHelper.Verify(raw, hash));
        Assert.False(WfApiKeyHelper.Verify(raw + "x", hash));
        Assert.False(WfApiKeyHelper.Verify("", hash));
    }

    [Fact]
    public void Verify_NullOrEmptyStoredHash_False()
    {
        Assert.False(WfApiKeyHelper.Verify("any", null));
        Assert.False(WfApiKeyHelper.Verify("any", ""));
    }
}
