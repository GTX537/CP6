using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using Xunit;

namespace CP6.Tests.Oa;
public sealed class NativeQueryDatesTests
{
    [Theory]
    [InlineData("2026-03-08", 23)]
    [InlineData("2026-11-01", 25)]
    [InlineData("2026-07-01", 24)]
    public void Selected_tenant_dates_resolve_real_DST_day_lengths(string day, int hours)
    {
        var window = NativeQueryDatesV2.Window(NativeQueryDatesV2.Zone("America/New_York"), day, day);
        Assert.Equal(TimeSpan.FromHours(hours), window.ToExclusiveUtc - window.FromInclusiveUtc);
    }
    [Theory]
    [InlineData("2026-02-30", "2026-03-01")]
    [InlineData("2026-03-02", "2026-03-01")]
    [InlineData("2026-03-01T00:00:00Z", null)]
    [InlineData(null, "9999-12-31")]
    public void Invalid_dates_fail_without_server_local_fallback(string? from, string? to)
        => Assert.Equal(422, Assert.Throws<ScopeAuthorizationExceptionV1>(() => NativeQueryDatesV2.Window(NativeQueryDatesV2.Zone("UTC"), from, to)).Status);
}
