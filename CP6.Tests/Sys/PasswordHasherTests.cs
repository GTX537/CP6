using CP6.Core.Services.Sys;
using Xunit;

namespace CP6.Tests.Sys;

public class PasswordHasherTests
{
    private readonly IPasswordHasher _h = new BCryptPasswordHasher();

    [Fact] public void Hash_is_not_plaintext_and_verifies()
    {
        var hash = _h.Hash("S3cret!23");
        Assert.NotEqual("S3cret!23", hash);
        Assert.True(_h.Verify("S3cret!23", hash));
        Assert.False(_h.Verify("wrong", hash));
    }

    [Theory]
    [InlineData("plainText", false)]
    [InlineData("$2a$11$abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUV01234", true)]
    public void IsHashed_detects_bcrypt_format(string value, bool expected)
        => Assert.Equal(expected, _h.IsHashed(value));
}
