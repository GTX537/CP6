using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Space;
using Xunit;

namespace CP6.Tests.Space;

/// <summary>
/// Space 主数据字段级审计接线行为测试（波4 T1）。
/// 照 <see cref="CP6.Tests.Sys.FieldAuditCaptureTests"/> 范式：InMemory + 假当前用户。
/// 验证 11 实体接入 IAuditable 后，create/update 随业务行同周期写入 Sys_FieldAuditLogs。
/// </summary>
public class SpaceAuditTests
{
    /// <summary>假当前用户桩。</summary>
    private sealed class FakeUser : ICurrentUserAccessor
    {
        public FakeUser(Guid? id, string? name) { UserId = id; UserName = name; }
        public Guid? UserId { get; }
        public string? UserName { get; }
    }

    // diff 行的轻量 DTO（System.Text.Json 默认属性名 Field/Old/New）。
    private sealed record DiffRow(string Field, string? Old, string? New);

    private static List<DiffRow> ParseChanges(string json)
        => JsonSerializer.Deserialize<List<DiffRow>>(json) ?? new();

    private static readonly Guid _userId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static CP6Context Ctx()
        => TestHelper.CreateInMemoryContext(new FakeUser(_userId, "alice"));

    [Fact]
    public void Create_Space_Site_writes_op1_audit_row()
    {
        using var db = Ctx();
        var site = new Space_Site { SiteCode = "S001", SiteName = "华东仓" };
        db.Set<Space_Site>().Add(site);
        db.SaveChanges();

        var rows = db.Sys_FieldAuditLogs.Where(x => x.Operation == 1).ToList();
        Assert.Single(rows);
        Assert.Equal(nameof(Space_Site), rows[0].EntityName);
        Assert.Equal(site.Id.ToString(), rows[0].EntityKey);
    }

    [Fact]
    public void Update_Space_Site_name_writes_op2_diff()
    {
        using var db = Ctx();
        var site = new Space_Site { SiteCode = "S002", SiteName = "旧名" };
        db.Set<Space_Site>().Add(site);
        db.SaveChanges();

        site.SiteName = "新名";
        db.SaveChanges();

        var rows = db.Sys_FieldAuditLogs.Where(x => x.Operation == 2).ToList();
        Assert.Single(rows);
        Assert.Equal(nameof(Space_Site), rows[0].EntityName);

        var diffs = ParseChanges(rows[0].Changes);
        Assert.Single(diffs);
        Assert.Equal("SiteName", diffs[0].Field);
        Assert.Equal("旧名", diffs[0].Old);
        Assert.Equal("新名", diffs[0].New);
    }
}
