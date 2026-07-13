using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;
using Xunit;

namespace CP6.Tests.Oa;

public class PrefMergeTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static PrefService Svc(CP6Context db) => new(db);

    // ── 合并写不覆盖他键（spec §7）──
    [Fact]
    public async Task SaveMerge_PatchesTopLevelKey_PreservesOthers()
    {
        using var db = NewDb();
        var me = Guid.NewGuid();
        await Svc(db).SaveAsync(me, """{"pageSize":50,"notify":{"todo":false}}""");

        await Svc(db).SaveMergeAsync(me, """{"rowMode":"expanded"}""");

        using var doc = JsonDocument.Parse(await Svc(db).GetAsync(me));
        Assert.Equal(50, doc.RootElement.GetProperty("pageSize").GetInt32());              // 他键保留
        Assert.False(doc.RootElement.GetProperty("notify").GetProperty("todo").GetBoolean());
        Assert.Equal("expanded", doc.RootElement.GetProperty("rowMode").GetString());       // 新键并入
    }

    [Fact]
    public async Task SaveMerge_ReplacesKeyWholesale_And_NullDeletesKey()
    {
        using var db = NewDb();
        var me = Guid.NewGuid();
        await Svc(db).SaveAsync(me, """{"pageSize":50,"notify":{"todo":false,"email":false}}""");

        await Svc(db).SaveMergeAsync(me, """{"notify":{"todoCreated":{"inApp":false}}}""");  // 顶层键整体替换
        using (var doc = JsonDocument.Parse(await Svc(db).GetAsync(me)))
        {
            var notify = doc.RootElement.GetProperty("notify");
            Assert.False(notify.TryGetProperty("todo", out _));                              // 旧扁平键被替换掉
            Assert.False(notify.GetProperty("todoCreated").GetProperty("inApp").GetBoolean());
            Assert.Equal(50, doc.RootElement.GetProperty("pageSize").GetInt32());
        }

        await Svc(db).SaveMergeAsync(me, """{"notify":null}""");                             // 恢复默认 = 删键
        using (var doc = JsonDocument.Parse(await Svc(db).GetAsync(me)))
        {
            Assert.False(doc.RootElement.TryGetProperty("notify", out _));
            Assert.Equal(50, doc.RootElement.GetProperty("pageSize").GetInt32());
        }
    }

    [Fact]
    public async Task SaveMerge_NoRow_CreatesRow()
    {
        using var db = NewDb();
        var me = Guid.NewGuid();
        await Svc(db).SaveMergeAsync(me, """{"rowMode":"expanded"}""");
        Assert.Equal(1, await db.Wf_InboxPrefs.CountAsync(p => p.UserId == me));
    }

    [Fact]
    public async Task SaveMerge_BadPatchJson_Throws_i18nKey()
    {
        using var db = NewDb();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Svc(db).SaveMergeAsync(Guid.NewGuid(), "NOT_JSON{{{"));
        Assert.Equal("oa.pref.errBadJson", ex.Message);
    }

    // ── IsEnabledAsync：查库 + per-request 缓存（缓存不跨请求，spec §7）──
    [Fact]
    public async Task IsEnabledAsync_ReadsMatrix_FromDb()
    {
        using var db = NewDb();
        var me = Guid.NewGuid();
        await Svc(db).SaveAsync(me, """{"notify":{"flowRejected":{"email":false}}}""");
        var svc = Svc(db);
        Assert.True (await svc.IsEnabledAsync(me, "flowRejected", "inApp"));
        Assert.False(await svc.IsEnabledAsync(me, "flowRejected", "email"));
        Assert.True (await svc.IsEnabledAsync(Guid.NewGuid(), "flowRejected", "email"));   // 无行 → true
    }

    [Fact]
    public async Task IsEnabledAsync_CachesWithinInstance_NotAcrossInstances()
    {
        using var db = NewDb();
        var me = Guid.NewGuid();
        db.Wf_InboxPrefs.Add(new Wf_InboxPref { Id = Guid.NewGuid(), UserId = me, PrefsJson = "{}" });
        await db.SaveChangesAsync();

        var svc1 = Svc(db);                                             // 模拟请求 1（Scoped 实例）
        Assert.True(await svc1.IsEnabledAsync(me, "todoCreated", "inApp"));   // 首查 → 缓存 "{}"

        var row = await db.Wf_InboxPrefs.SingleAsync(p => p.UserId == me);
        row.PrefsJson = """{"notify":{"todoCreated":{"inApp":false}}}""";
        await db.SaveChangesAsync();

        Assert.True(await svc1.IsEnabledAsync(me, "todoCreated", "inApp"));   // 同实例（同请求）：命中缓存，仍 true
        Assert.False(await Svc(db).IsEnabledAsync(me, "todoCreated", "inApp")); // 新实例（新请求）：读到新值
    }

    [Fact]
    public async Task SaveMerge_InvalidatesOwnCache()
    {
        using var db = NewDb();
        var me = Guid.NewGuid();
        var svc = Svc(db);
        Assert.True(await svc.IsEnabledAsync(me, "todoCreated", "inApp"));                 // 缓存默认 "{}"
        await svc.SaveMergeAsync(me, """{"notify":{"todoCreated":{"inApp":false}}}""");
        Assert.False(await svc.IsEnabledAsync(me, "todoCreated", "inApp"));                // 同实例保存后读到新值
    }

    // ── GetRowModeAsync（D-T1 消费）──
    [Theory]
    [InlineData(null, "merged")]                                  // 无行 → 默认
    [InlineData("{}", "merged")]                                  // 无键 → 默认
    [InlineData("""{"rowMode":"expanded"}""", "expanded")]
    [InlineData("""{"rowMode":"merged"}""", "merged")]
    [InlineData("""{"rowMode":"garbage"}""", "merged")]           // 非法值 → 默认
    [InlineData("NOT_JSON{{{", "merged")]                         // 畸形 → 默认
    public async Task GetRowMode_ParsesTopLevelKey_DefaultMerged(string? prefsJson, string expected)
    {
        using var db = NewDb();
        var me = Guid.NewGuid();
        if (prefsJson is not null)
        {
            db.Wf_InboxPrefs.Add(new Wf_InboxPref { Id = Guid.NewGuid(), UserId = me, PrefsJson = prefsJson });
            await db.SaveChangesAsync();
        }
        Assert.Equal(expected, await Svc(db).GetRowModeAsync(me));
    }
}
