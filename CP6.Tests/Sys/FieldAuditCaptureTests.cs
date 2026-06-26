using System.Linq;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Fin;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CP6.Tests.Sys;

/// <summary>
/// 字段审计（#4 T3）捕获核心单测：InMemory + 假当前用户。
/// load→modify→save 天然先查后改（int 键 Modified 在单测可验，控制器断连问题由 T4 解决）。
/// InMemory 无事务 → 仅验"键落定 + 审计行随业务行同周期写入"。
/// </summary>
public class FieldAuditCaptureTests
{
    /// <summary>假当前用户桩。</summary>
    private sealed class FakeUser : ICurrentUserAccessor
    {
        public FakeUser(Guid? id, string? name) { UserId = id; UserName = name; }
        public Guid? UserId { get; }
        public string? UserName { get; }
    }

    /// <summary>可写租户上下文桩（用于双租户测试）。</summary>
    private sealed class FakeTenant : ITenantContext
    {
        public Guid CurrentTenantId { get; set; } = TenantContext.DefaultTenant;
    }

    // diff 行的轻量 DTO（System.Text.Json 默认属性名 Field/Old/New）。
    private sealed record DiffRow(string Field, string? Old, string? New);

    private static List<DiffRow> ParseChanges(string json)
        => JsonSerializer.Deserialize<List<DiffRow>>(json) ?? new();

    private static readonly Guid _userId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static CP6Context Ctx(ICurrentUserAccessor? user = null, ITenantContext? tenant = null)
        => TestHelper.CreateInMemoryContext(user ?? new FakeUser(_userId, "alice"), tenant);

    // ── 1. Modified：只记改动的列 ──────────────────────────────────────────
    [Fact]
    public void Modified_marked_entity_writes_one_row_with_only_changed_fields()
    {
        using var db = Ctx();
        var acc = new GlAccount { Code = "1001", Name = "现金", StandardScheme = "CN-GAAP" };
        db.Set<GlAccount>().Add(acc);
        db.SaveChanges();

        // 改名 → 只 Name 一列变
        acc.Name = "库存现金";
        db.SaveChanges();

        var rows = db.Sys_FieldAuditLogs.Where(x => x.Operation == 2).ToList();
        Assert.Single(rows);
        var row = rows[0];
        Assert.Equal(nameof(GlAccount), row.EntityName);
        Assert.Equal(acc.Id.ToString(), row.EntityKey);

        var diffs = ParseChanges(row.Changes);
        Assert.Single(diffs);
        Assert.Equal("Name", diffs[0].Field);
        Assert.Equal("现金", diffs[0].Old);
        Assert.Equal("库存现金", diffs[0].New);
    }

    // ── 2. Added(Guid)：键为落定真值 ───────────────────────────────────────
    [Fact]
    public void Added_marked_entity_writes_op1_with_resolved_guid_key()
    {
        using var db = Ctx();
        var acc = new GlAccount { Code = "2001", Name = "应付账款", StandardScheme = "CN-GAAP" };
        db.Set<GlAccount>().Add(acc);
        db.SaveChanges();

        var rows = db.Sys_FieldAuditLogs.Where(x => x.Operation == 1).ToList();
        Assert.Single(rows);
        Assert.NotEqual(Guid.Empty.ToString(), rows[0].EntityKey);
        Assert.Equal(acc.Id.ToString(), rows[0].EntityKey);
        Assert.NotEqual(Guid.Empty, acc.Id);
    }

    // ── 3. Deleted：键为删除前值 ───────────────────────────────────────────
    [Fact]
    public void Deleted_marked_entity_writes_op3_with_key_before_save()
    {
        using var db = Ctx();
        var acc = new GlAccount { Code = "3001", Name = "实收资本", StandardScheme = "CN-GAAP" };
        db.Set<GlAccount>().Add(acc);
        db.SaveChanges();
        var key = acc.Id.ToString();

        db.Set<GlAccount>().Remove(acc);
        db.SaveChanges();

        var rows = db.Sys_FieldAuditLogs.Where(x => x.Operation == 3).ToList();
        Assert.Single(rows);
        Assert.Equal(key, rows[0].EntityKey);
    }

    // ── 4. int 键实体：键=主键值（R8 键形无关）───────────────────────────
    [Fact]
    public void Int_key_entity_added_and_modified_uses_pk_value()
    {
        using var db = Ctx();
        var menu = new Sys_Menu { MenuId = 9001, MenuName = "测试菜单", RoutePath = "/test" };
        db.Set<Sys_Menu>().Add(menu);
        db.SaveChanges();

        var added = db.Sys_FieldAuditLogs.Single(x => x.Operation == 1 && x.EntityName == nameof(Sys_Menu));
        Assert.Equal("9001", added.EntityKey);
        Assert.DoesNotContain(ParseChanges(added.Changes), d => d.Field == "MenuId");

        menu.MenuName = "改名菜单";
        db.SaveChanges();

        var modified = db.Sys_FieldAuditLogs.Single(x => x.Operation == 2 && x.EntityName == nameof(Sys_Menu));
        Assert.Equal("9001", modified.EntityKey);
        var diffs = ParseChanges(modified.Changes);
        Assert.Single(diffs);
        Assert.Equal("MenuName", diffs[0].Field);
    }

    // ── 5. 非 IAuditable 实体不记 ──────────────────────────────────────────
    [Fact]
    public void Non_auditable_entity_change_writes_no_row()
    {
        using var db = Ctx();
        var lang = new Sys_Lang { LangKey = "test.key", ZhCN = "原值" };
        db.Set<Sys_Lang>().Add(lang);
        db.SaveChanges();

        lang.ZhCN = "新值";
        db.SaveChanges();

        Assert.Empty(db.Sys_FieldAuditLogs.ToList());
    }

    // ── 6. [AuditIgnore] 字段排除（Password）─────────────────────────────
    [Fact]
    public void Password_excluded_by_AuditIgnore()
    {
        using var db = Ctx();
        var u = new Sys_User { UserName = "bob", Password = "hash-old", Email = "b@x.com" };
        db.Set<Sys_User>().Add(u);
        db.SaveChanges();

        u.Password = "hash-new";
        u.Email = "b2@x.com";
        db.SaveChanges();

        var modified = db.Sys_FieldAuditLogs.Single(x => x.Operation == 2 && x.EntityName == nameof(Sys_User));
        var diffs = ParseChanges(modified.Changes);
        Assert.DoesNotContain(diffs, d => d.Field == "Password");
        Assert.Contains(diffs, d => d.Field == "Email");   // 非密字段正常记
    }

    // ── 7. 拒名单兜底（TwoFactorSecret 无 [AuditIgnore] 仍被跳过）─────────
    [Fact]
    public void Secret_field_excluded_by_denylist_even_without_attribute()
    {
        using var db = Ctx();
        var u = new Sys_User { UserName = "carol", Password = "h", TwoFactorSecret = "OLDSECRET" };
        db.Set<Sys_User>().Add(u);
        db.SaveChanges();

        u.TwoFactorSecret = "NEWSECRET";
        u.NickName = "Carol";
        db.SaveChanges();

        var modified = db.Sys_FieldAuditLogs.Single(x => x.Operation == 2 && x.EntityName == nameof(Sys_User));
        var diffs = ParseChanges(modified.Changes);
        // TwoFactorSecret 以 "secret" 结尾 → 拒名单兜底跳过（无 [AuditIgnore]）
        Assert.DoesNotContain(diffs, d => d.Field == "TwoFactorSecret");
        Assert.Contains(diffs, d => d.Field == "NickName");
    }

    // ── 8. 仅改元字段 → 空 diff 不记 ──────────────────────────────────────
    [Fact]
    public void Only_meta_field_change_writes_no_row()
    {
        using var db = Ctx();
        var acc = new GlAccount { Code = "4001", Name = "盈余公积", StandardScheme = "CN-GAAP" };
        db.Set<GlAccount>().Add(acc);
        db.SaveChanges();

        // 仅改 Modifier/ModifyDate（行级元字段，捕获跳过）
        acc.Modifier = "system";
        acc.ModifyDate = DateTime.Now;
        db.SaveChanges();

        Assert.Empty(db.Sys_FieldAuditLogs.Where(x => x.Operation == 2).ToList());
    }

    // ── 9. 主键/TenantId 不入 diff ────────────────────────────────────────
    [Fact]
    public void PrimaryKey_and_TenantId_not_in_diff()
    {
        using var db = Ctx();
        var acc = new GlAccount { Code = "5001", Name = "本年利润", StandardScheme = "CN-GAAP" };
        db.Set<GlAccount>().Add(acc);
        db.SaveChanges();

        var menu = new Sys_Menu { MenuId = 9002, MenuName = "M2" };
        db.Set<Sys_Menu>().Add(menu);
        db.SaveChanges();

        var accAdd = db.Sys_FieldAuditLogs.Single(x => x.EntityName == nameof(GlAccount) && x.Operation == 1);
        var accDiffs = ParseChanges(accAdd.Changes);
        Assert.DoesNotContain(accDiffs, d => d.Field == "Id");
        Assert.DoesNotContain(accDiffs, d => d.Field == "TenantId");

        var menuAdd = db.Sys_FieldAuditLogs.Single(x => x.EntityName == nameof(Sys_Menu) && x.Operation == 1);
        var menuDiffs = ParseChanges(menuAdd.Changes);
        Assert.DoesNotContain(menuDiffs, d => d.Field == "MenuId");
    }

    // ── 10. 操作人归属：注入 → 落；null → null（后台写入）─────────────────
    [Fact]
    public void User_attribution_filled_and_null_for_background()
    {
        // 注入用户
        using (var db = Ctx(new FakeUser(_userId, "alice")))
        {
            var acc = new GlAccount { Code = "6001", Name = "A", StandardScheme = "CN-GAAP" };
            db.Set<GlAccount>().Add(acc);
            db.SaveChanges();
            var row = db.Sys_FieldAuditLogs.Single();
            Assert.Equal(_userId, row.UserId);
            Assert.Equal("alice", row.UserName);
        }

        // 无用户（后台）
        using (var db = Ctx(new FakeUser(null, null)))
        {
            var acc = new GlAccount { Code = "6002", Name = "B", StandardScheme = "CN-GAAP" };
            db.Set<GlAccount>().Add(acc);
            db.SaveChanges();
            var row = db.Sys_FieldAuditLogs.Single();
            Assert.Null(row.UserId);
            Assert.Null(row.UserName);
        }
    }

    // ── 11. 审计行租户 = 业务实体租户（R4）─────────────────────────────────
    [Fact]
    public void Audit_row_tenant_mirrors_business_entity()
    {
        var tenantA = TenantContext.DefaultTenant;
        var tenantB = Guid.Parse("BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB");

        var tctx = new FakeTenant { CurrentTenantId = tenantA };
        using var db = Ctx(new FakeUser(_userId, "alice"), tctx);

        // 租户 A 的实体（盖章为 A）
        var accA = new GlAccount { Code = "7001", Name = "A-acc", StandardScheme = "CN-GAAP" };
        db.Set<GlAccount>().Add(accA);
        db.SaveChanges();

        // 切换上下文到租户 B 并显式赋租户
        tctx.CurrentTenantId = tenantB;
        var accB = new GlAccount { Code = "7002", Name = "B-acc", StandardScheme = "CN-GAAP", TenantId = tenantB };
        db.Set<GlAccount>().Add(accB);
        db.SaveChanges();

        // 审计行租户须镜像各自业务实体的租户（IgnoreQueryFilters 跨租户读取）
        var rowA = db.Sys_FieldAuditLogs.IgnoreQueryFilters()
            .Single(x => x.EntityKey == accA.Id.ToString());
        Assert.Equal(tenantA, rowA.TenantId);

        var rowB = db.Sys_FieldAuditLogs.IgnoreQueryFilters()
            .Single(x => x.EntityKey == accB.Id.ToString());
        Assert.Equal(tenantB, rowB.TenantId);
    }
}
