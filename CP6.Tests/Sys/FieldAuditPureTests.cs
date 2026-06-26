using System.Globalization;
using System.Threading;
using CP6.Core.EFDbContext;
using Xunit;

namespace CP6.Tests.Sys;

/// <summary>
/// 字段审计（#4 T3）纯函数单测：拒名单 <c>IsSecretField</c> + 值化 <c>Stringify</c>，无 DB。
/// 这两个方法在 CP6Context 内为 <c>internal static</c>，经 InternalsVisibleTo 暴露给测试工程。
/// </summary>
public class FieldAuditPureTests
{
    // ── IsSecretField：拒名单兜底 ─────────────────────────────────────────
    [Theory]
    [InlineData("Password")]
    [InlineData("password")]
    [InlineData("PASSWORD")]
    [InlineData("TwoFactorSecret")]
    [InlineData("twofactorsecret")]
    [InlineData("ClientSecretProtected")]
    [InlineData("clientsecretprotected")]
    [InlineData("ClientSecret")]          // EndsWith("secret")
    [InlineData("PasswordHash")]          // EndsWith("hash")
    [InlineData("TokenHash")]
    [InlineData("tokenhash")]
    [InlineData("Salt")]
    [InlineData("salt")]
    public void IsSecretField_true_for_secret_named_fields(string name)
    {
        Assert.True(CP6Context.IsSecretField(name));
    }

    [Theory]
    [InlineData("Email")]
    [InlineData("UserName")]
    [InlineData("Name")]
    [InlineData("Code")]
    public void IsSecretField_false_for_normal_fields(string name)
    {
        Assert.False(CP6Context.IsSecretField(name));
    }

    // ── Stringify：null / 截断 / InvariantCulture ─────────────────────────
    [Fact]
    public void Stringify_null_returns_null()
    {
        Assert.Null(CP6Context.Stringify(null));
    }

    [Fact]
    public void Stringify_truncates_over_1000_chars()
    {
        var big = new string('x', 5000);
        var s = CP6Context.Stringify(big);
        Assert.NotNull(s);
        Assert.Equal(1000, s!.Length);
    }

    [Fact]
    public void Stringify_keeps_short_string_intact()
    {
        Assert.Equal("hello", CP6Context.Stringify("hello"));
    }

    [Fact]
    public void Stringify_decimal_uses_invariant_culture_dot()
    {
        var prev = Thread.CurrentThread.CurrentCulture;
        try
        {
            // 德语区域用逗号作小数点；Stringify 必须恒用 '.'（InvariantCulture）
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            var s = CP6Context.Stringify(1234.56m);
            Assert.Equal("1234.56", s);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = prev;
        }
    }

    [Fact]
    public void Stringify_datetime_uses_invariant_culture()
    {
        var prev = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            var dt = new DateTime(2026, 6, 25, 13, 45, 0);
            var expected = Convert.ToString(dt, CultureInfo.InvariantCulture);
            Assert.Equal(expected, CP6Context.Stringify(dt));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = prev;
        }
    }
}
