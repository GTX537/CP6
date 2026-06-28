using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

public class PrefServiceTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static IPrefService Svc(CP6Context db) => new PrefService(db);

    [Fact]
    public async Task Get_DefaultsEmpty_Save_Upserts()
    {
        using var db = NewDb();
        var me = Guid.NewGuid();
        Assert.Equal("{}", await Svc(db).GetAsync(me));            // 无则默认 {}
        await Svc(db).SaveAsync(me, """{"pageSize":50}""");
        Assert.Equal("""{"pageSize":50}""", await Svc(db).GetAsync(me));
        await Svc(db).SaveAsync(me, """{"pageSize":20}""");        // upsert 覆盖（不重复行）
        Assert.Equal("""{"pageSize":20}""", await Svc(db).GetAsync(me));
        Assert.Equal(1, await db.Wf_InboxPrefs.CountAsync(p => p.UserId == me));
    }

    // ── N-T3: GetNotifyPrefsAsync ──────────────────────────────────────────

    /// <summary>无 Wf_InboxPref 行 → 缺省全 true</summary>
    [Fact]
    public async Task GetNotifyPrefs_NoPrefRow_AllDefaultTrue()
    {
        using var db = NewDb();
        var prefs = await Svc(db).GetNotifyPrefsAsync(Guid.NewGuid());
        Assert.True(prefs.Todo);
        Assert.True(prefs.Approved);
        Assert.True(prefs.Rejected);
        Assert.True(prefs.Timeout);
        Assert.True(prefs.Email);
    }

    /// <summary>PrefsJson="{}" → 缺省全 true（无 notify 子键）</summary>
    [Fact]
    public async Task GetNotifyPrefs_EmptyJson_AllDefaultTrue()
    {
        using var db = NewDb();
        var me = Guid.NewGuid();
        db.Wf_InboxPrefs.Add(new Wf_InboxPref { Id = Guid.NewGuid(), UserId = me, PrefsJson = "{}" });
        await db.SaveChangesAsync();

        var prefs = await Svc(db).GetNotifyPrefsAsync(me);
        Assert.True(prefs.Todo);
        Assert.True(prefs.Approved);
        Assert.True(prefs.Rejected);
        Assert.True(prefs.Timeout);
        Assert.True(prefs.Email);
    }

    /// <summary>notify 子键全部为 true → 全 true</summary>
    [Fact]
    public async Task GetNotifyPrefs_AllEnabled_AllTrue()
    {
        using var db = NewDb();
        var me = Guid.NewGuid();
        db.Wf_InboxPrefs.Add(new Wf_InboxPref
        {
            Id = Guid.NewGuid(), UserId = me,
            PrefsJson = """{"notify":{"todo":true,"approved":true,"rejected":true,"timeout":true,"email":true}}"""
        });
        await db.SaveChangesAsync();

        var prefs = await Svc(db).GetNotifyPrefsAsync(me);
        Assert.True(prefs.Todo);
        Assert.True(prefs.Approved);
        Assert.True(prefs.Rejected);
        Assert.True(prefs.Timeout);
        Assert.True(prefs.Email);
    }

    /// <summary>关闭 email → Email=false，其余 true</summary>
    [Fact]
    public async Task GetNotifyPrefs_EmailDisabled_EmailFalse()
    {
        using var db = NewDb();
        var me = Guid.NewGuid();
        db.Wf_InboxPrefs.Add(new Wf_InboxPref
        {
            Id = Guid.NewGuid(), UserId = me,
            PrefsJson = """{"notify":{"todo":true,"approved":true,"rejected":true,"timeout":true,"email":false}}"""
        });
        await db.SaveChangesAsync();

        var prefs = await Svc(db).GetNotifyPrefsAsync(me);
        Assert.True(prefs.Todo);
        Assert.True(prefs.Approved);
        Assert.True(prefs.Rejected);
        Assert.True(prefs.Timeout);
        Assert.False(prefs.Email);
    }

    /// <summary>关闭单个事件（rejected=false）→ 仅 Rejected=false，其余 true</summary>
    [Fact]
    public async Task GetNotifyPrefs_RejectedDisabled_OnlyRejectedFalse()
    {
        using var db = NewDb();
        var me = Guid.NewGuid();
        db.Wf_InboxPrefs.Add(new Wf_InboxPref
        {
            Id = Guid.NewGuid(), UserId = me,
            PrefsJson = """{"notify":{"todo":true,"approved":true,"rejected":false,"timeout":true,"email":true}}"""
        });
        await db.SaveChangesAsync();

        var prefs = await Svc(db).GetNotifyPrefsAsync(me);
        Assert.True(prefs.Todo);
        Assert.True(prefs.Approved);
        Assert.False(prefs.Rejected);
        Assert.True(prefs.Timeout);
        Assert.True(prefs.Email);
    }

    /// <summary>notify 子键缺少某字段（缺 timeout）→ 缺字段默认 true</summary>
    [Fact]
    public async Task GetNotifyPrefs_MissingField_DefaultsToTrue()
    {
        using var db = NewDb();
        var me = Guid.NewGuid();
        db.Wf_InboxPrefs.Add(new Wf_InboxPref
        {
            Id = Guid.NewGuid(), UserId = me,
            PrefsJson = """{"notify":{"todo":false,"approved":true,"rejected":true,"email":false}}"""
            // "timeout" 字段缺失 → 默认 true
        });
        await db.SaveChangesAsync();

        var prefs = await Svc(db).GetNotifyPrefsAsync(me);
        Assert.False(prefs.Todo);
        Assert.True(prefs.Approved);
        Assert.True(prefs.Rejected);
        Assert.True(prefs.Timeout);     // 缺省 true
        Assert.False(prefs.Email);
    }

    /// <summary>畸形 JSON（无法解析）→ 回落全 true，不抛</summary>
    [Fact]
    public async Task GetNotifyPrefs_MalformedJson_FallsBackToAllTrue()
    {
        using var db = NewDb();
        var me = Guid.NewGuid();
        db.Wf_InboxPrefs.Add(new Wf_InboxPref
        {
            Id = Guid.NewGuid(), UserId = me,
            PrefsJson = "NOT_VALID_JSON{{{"
        });
        await db.SaveChangesAsync();

        var ex = await Record.ExceptionAsync(() => Svc(db).GetNotifyPrefsAsync(me));
        Assert.Null(ex);

        var prefs = await Svc(db).GetNotifyPrefsAsync(me);
        Assert.True(prefs.Todo);
        Assert.True(prefs.Approved);
        Assert.True(prefs.Rejected);
        Assert.True(prefs.Timeout);
        Assert.True(prefs.Email);
    }
}
