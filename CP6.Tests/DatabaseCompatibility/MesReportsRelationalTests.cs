using System.Globalization;
using CP6.Core.Persistence;
using CP6.Core.Services.Mes;
using Dapper;
using Xunit.Abstractions;

namespace CP6.Tests.DatabaseCompatibility;

[Collection(Wp5ReportsRelationalCollection.Name)]
public sealed class MesReportsRelationalTests(Wp5ReportsRelationalFixture fixture, ITestOutputHelper output)
{
    [Wp5ReportsFact]
    public async Task Summary_empty_returns_zero()
    {
        output.WriteLine(fixture.SetupSummary);
        await fixture.RequireEmptyReportTablesAsync();
        await using var connection = fixture.CreateConnection();
        var actual = await new MesDashboardDapperService(connection).GetSummaryAsync();
        Assert.Equal(0, actual.InProgressCount);
        Assert.Equal(0, actual.CompletedCount);
        Assert.Equal(0m, actual.TotalGoodQty);
        Assert.Equal(0m, actual.TotalDefectQty);
        Assert.Equal(0m, actual.DefectRate);
        Assert.Equal(0, actual.DelayedCount);
    }

    [Wp5ReportsFact]
    public async Task Daily_trend_fills_three_zero_days()
    {
        output.WriteLine(fixture.SetupSummary);
        await fixture.RequireEmptyReportTablesAsync();
        await using var connection = fixture.CreateConnection();
        var todaySql = fixture.Database.Provider == DatabaseProvider.PostgreSql
            ? "SELECT CURRENT_DATE::text" : "SELECT CONVERT(varchar(10), GETDATE(), 23)";
        var before = DateOnly.ParseExact(await connection.QuerySingleAsync<string>(todaySql), "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var actual = await new MesDashboardDapperService(connection).GetDailyTrendAsync(3);
        var after = DateOnly.ParseExact(await connection.QuerySingleAsync<string>(todaySql), "yyyy-MM-dd", CultureInfo.InvariantCulture);
        Assert.Equal(3, actual.Count);
        var last = DateOnly.ParseExact(actual[2].Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        Assert.True(last == before || last == after, "The report must end on the database calendar day observed around the query.");
        Assert.Equal(Enumerable.Range(-2, 3).Select(offset => last.AddDays(offset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            actual.Select(row => row.Date));
        Assert.All(actual, row => { Assert.Equal(0m, row.GoodQty); Assert.Equal(0m, row.DefectQty); });
    }

    [Wp5ReportsFact]
    public async Task Process_progress_empty_returns_empty()
    {
        output.WriteLine(fixture.SetupSummary);
        await fixture.RequireEmptyReportTablesAsync();
        await using var connection = fixture.CreateConnection();
        Assert.Empty(await new MesDashboardDapperService(connection).GetProcessProgressAsync());
    }
}
