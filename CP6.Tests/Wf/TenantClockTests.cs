using System;
using System.Threading.Tasks;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Sys;
using Microsoft.Data.Sqlite;
using Xunit;
using static CP6.Tests.WfsInfraTestHarness;

namespace CP6.Tests;

/// <summary>E-T2 ITenantClock 时区解析链：租户 TimeZoneId → app 默认（Wfs:DefaultTimeZone）→ 服务器本地。
/// null 全等回归（现状字节等价）+ 东京解析 + app 默认回落 + 不可解析回落。</summary>
public class TenantClockTests
{
    [Fact]
    public async Task NullTimeZoneId_FallsBackToServerLocal_Regression()
    {
        using var conn = NewSqliteWithSchema();
        using (var db = Ctx(conn)) { db.Sys_Tenants.Add(new Sys_Tenant { TenantCode = "t", TenantName = "T", TimeZoneId = null }); await db.SaveChangesAsync(); }
        using var db2 = Ctx(conn);
        var clock = new TenantClock(db2, FakeTenantContext(conn), new WfsInfraOptions());
        Assert.Equal(TimeZoneInfo.Local.Id, clock.GetTenantTimeZone().Id);   // null → 服务器本地（现状全等）
    }

    [Fact]
    public async Task TokyoTimeZoneId_Resolves()
    {
        using var conn = NewSqliteWithSchema();
        Guid tid;
        using (var db = Ctx(conn)) { var t = new Sys_Tenant { TenantCode = "jp", TenantName = "JP", TimeZoneId = "Asia/Tokyo" }; db.Sys_Tenants.Add(t); await db.SaveChangesAsync(); tid = t.Id; }
        using var db2 = Ctx(conn);
        var clock = new TenantClock(db2, FakeTenantContext(conn, tid), new WfsInfraOptions());
        var tz = clock.GetTenantTimeZone();
        Assert.Equal(TimeSpan.FromHours(9), tz.GetUtcOffset(new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc)));   // JST +9
    }

    [Fact]
    public async Task NullTimeZoneId_FallsBackToDefaultTimeZone_WhenConfigured()
    {
        using var conn = NewSqliteWithSchema();
        Guid tid;
        using (var db = Ctx(conn)) { var t = new Sys_Tenant { TenantCode = "d", TenantName = "D", TimeZoneId = null }; db.Sys_Tenants.Add(t); await db.SaveChangesAsync(); tid = t.Id; }
        using var db2 = Ctx(conn);
        // 租户无 TimeZoneId → 回落 app 默认（Wfs:DefaultTimeZone=Asia/Tokyo）
        var clock = new TenantClock(db2, FakeTenantContext(conn, tid), new WfsInfraOptions { DefaultTimeZone = "Asia/Tokyo" });
        Assert.Equal(TimeSpan.FromHours(9), clock.GetTenantTimeZone().GetUtcOffset(new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public async Task UnresolvableTimeZoneId_FallsBackToServerLocal()
    {
        using var conn = NewSqliteWithSchema();
        Guid tid;
        using (var db = Ctx(conn)) { var t = new Sys_Tenant { TenantCode = "x", TenantName = "X", TimeZoneId = "Mars/Olympus" }; db.Sys_Tenants.Add(t); await db.SaveChangesAsync(); tid = t.Id; }
        using var db2 = Ctx(conn);
        // 不可解析 id（既非 IANA 亦非 Windows）→ 无 DefaultTimeZone → 服务器本地（不炸）
        var clock = new TenantClock(db2, FakeTenantContext(conn, tid), new WfsInfraOptions());
        Assert.Equal(TimeZoneInfo.Local.Id, clock.GetTenantTimeZone().Id);
    }

    // FakeTenantContext：返回设定 CurrentTenantId 的 ITenantContext 桩（照仓库既有测试桩口径）。
    private static CP6.Core.Services.Common.ITenantContext FakeTenantContext(SqliteConnection conn, Guid? tid = null)
        => new StubTenantContext { CurrentTenantId = tid ?? Guid.Empty };
}
