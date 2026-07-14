using System;
using System.Linq;
using CP6.WebApi.Seed;
using Xunit;

namespace CP6.Tests;

public class JapaneseHolidaySeedTests
{
    [Fact]
    public void Items_Cover2026And2027_35Dates_AllDistinct()
    {
        Assert.Equal(35, JapaneseHolidaySeed.Items.Length);
        Assert.Equal(18, JapaneseHolidaySeed.Items.Count(x => x.Y == 2026));
        Assert.Equal(17, JapaneseHolidaySeed.Items.Count(x => x.Y == 2027));
        var dates = JapaneseHolidaySeed.Items.Select(x => new DateTime(x.Y, x.M, x.D)).ToList();
        Assert.Equal(dates.Count, dates.Distinct().Count());
    }

    [Fact]
    public void Items_ContainKeyComputedDates()
    {
        bool Has(int y, int m, int d, string note)
            => JapaneseHolidaySeed.Items.Any(x => x.Y == y && x.M == m && x.D == d && x.Note == note);

        Assert.True(Has(2026, 1, 12, "成人の日"));    // 1 月第 2 月曜
        Assert.True(Has(2026, 3, 20, "春分の日"));    // 2026 春分
        Assert.True(Has(2026, 5, 6, "振替休日"));     // 5/3(日)→振替
        Assert.True(Has(2026, 9, 22, "国民の休日"));  // 9/21(敬老,月) 与 9/23(秋分,水) 之间的挟まれ日
        Assert.True(Has(2027, 3, 21, "春分の日"));    // 2027 春分
        Assert.True(Has(2027, 3, 22, "振替休日"));    // 2027 春分 3/21(日)→振替
        Assert.True(Has(2027, 7, 19, "海の日"));      // 7 月第 3 月曜
    }

    [Fact]
    public void For_StampsTenant_AllHolidaysNonWorkday()
    {
        var tenant = Guid.NewGuid();
        var rows = JapaneseHolidaySeed.For(tenant);
        Assert.Equal(35, rows.Length);
        Assert.All(rows, r => Assert.Equal(tenant, r.TenantId));
        Assert.All(rows, r => Assert.False(r.IsWorkday));
    }
}
