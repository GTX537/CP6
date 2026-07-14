using CP6.Entity.DomainModels.Sys;
using CP6.WebApi.Seed;

namespace CP6.Tests;

/// <summary>
/// WFS 波⑤ F-T1：I18nOaEngineInfraScreenSeed 五语键面双向对账 + insert-only 去重守卫。
///
/// ★ ExpectedKeys 为独立硬编码 oracle（源自前端 t() 全量 grep：WorkCalendar.vue / WfConnector*.vue /
///   FlowAdmin.vue tab / NodePropertyPanel.vue / designerModel.ts + 后端错误码），非引用 seed 内部——防自证假绿。
///   键面与实际 t() 消费零缺零孤儿；nav.743 为侧栏菜单导航键（MenuTreeItem.vue: te('nav.'+id)）。
/// </summary>
public class I18nOaEngineInfraScreenSeedTests
{
    // 前端/后端实际消费的键（草稿键名以代码 t() 为准）。
    private static readonly string[] ExpectedKeys =
    {
        // 年历（WorkCalendar.vue，含动态 kind.{makeup|closed|weekend|normal} 全域 + {n} 插值）
        "oa.workcal.title", "oa.workcal.empty", "oa.workcal.importJp", "oa.workcal.imported",
        "oa.workcal.legend.makeup", "oa.workcal.legend.closed", "oa.workcal.legend.weekend",
        "oa.workcal.kind.makeup", "oa.workcal.kind.closed", "oa.workcal.kind.weekend", "oa.workcal.kind.normal",
        "oa.workcal.dialog.title", "oa.workcal.dialog.note",
        "nav.743",
        // 连接器（WfConnectorPanel/Dialog.vue + FlowAdmin.vue tab）
        "oa.connector.tab", "oa.connector.new", "oa.connector.empty", "oa.connector.authYes", "oa.connector.authNo",
        "oa.connector.col.name", "oa.connector.col.displayName", "oa.connector.col.baseUrl", "oa.connector.col.timeout",
        "oa.connector.col.auth", "oa.connector.col.enabled", "oa.connector.col.actions",
        "oa.connector.form.name", "oa.connector.form.nameHint", "oa.connector.form.displayName", "oa.connector.form.baseUrl",
        "oa.connector.form.auth", "oa.connector.form.authHint", "oa.connector.form.authConfigured",
        "oa.connector.form.authPlaceholder", "oa.connector.form.timeout", "oa.connector.form.required",
        // 设计器新键（E-T1 / A-T3 / B-T2）
        "oa.designer.svc.httpMethod", "oa.designer.svc.httpMethodHint", "oa.designer.svc.timeoutSec",
        "oa.designer.svc.delayMode.workdays", "oa.designer.timeout.errorEdge",
        "oa.designer.errHttpOverride", "oa.designer.errErrorEdgeSource", "oa.designer.errTimeoutErrorEdge",
        // 后端错误码
        "E-WF-027", "E-WF-028",
    };

    [Fact]
    public void Items_KeySet_MatchesConsumedKeys_Exactly_NoMissingNoOrphan()
    {
        var seeded = I18nOaEngineInfraScreenSeed.Items.Select(x => x.LangKey).ToHashSet();
        var expected = ExpectedKeys.ToHashSet();

        var missing = expected.Except(seeded).ToList();   // 消费了但未种（裸 key）
        var orphan = seeded.Except(expected).ToList();     // 种了但无人消费
        Assert.True(missing.Count == 0, "漏种键：" + string.Join(", ", missing));
        Assert.True(orphan.Count == 0, "孤儿键：" + string.Join(", ", orphan));
        Assert.Equal(expected, seeded);
    }

    [Fact]
    public void Items_NoDuplicateLangKeys()
    {
        var keys = I18nOaEngineInfraScreenSeed.Items.Select(x => x.LangKey).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void Items_AllFiveLanguagesNonEmpty()
    {
        foreach (var it in I18nOaEngineInfraScreenSeed.Items)
        {
            Assert.False(string.IsNullOrWhiteSpace(it.ZhCN), $"{it.LangKey} ZhCN 空");
            Assert.False(string.IsNullOrWhiteSpace(it.ZhTW), $"{it.LangKey} ZhTW 空");
            Assert.False(string.IsNullOrWhiteSpace(it.En), $"{it.LangKey} En 空");
            Assert.False(string.IsNullOrWhiteSpace(it.Ja), $"{it.LangKey} Ja 空");
            Assert.False(string.IsNullOrWhiteSpace(it.Ko), $"{it.LangKey} Ko 空");
        }
    }

    // 中日共用汉字词（合法真译，非偷懒复制）——允许 Ja==ZhCN 的白名单。
    private static readonly HashSet<string> SharedCjkTerms = new() { "操作" };

    [Fact]
    public void Items_JaAndKo_AreRealTranslations_NotChineseCopy()
    {
        // 术语系与既有 I18nOa* 一致：Ko 为谚文必异于 ZhCN；Ja 除中日共用汉字词外亦须异于 ZhCN。
        foreach (var it in I18nOaEngineInfraScreenSeed.Items)
        {
            if (it.LangKey.StartsWith("E-WF-")) continue;
            Assert.NotEqual(it.ZhCN, it.Ko);   // 谚文强信号：真译
            if (!SharedCjkTerms.Contains(it.ZhCN!))
                Assert.NotEqual(it.ZhCN, it.Ja);
        }
    }

    [Fact]
    public void Items_OnlyImportedKey_UsesBraceInterpolation()
    {
        // vue-i18n flatJson 把 {x} 解析为具名插值；除 imported({n}) 外其余值不得含裸 {，否则渲染破形。
        foreach (var it in I18nOaEngineInfraScreenSeed.Items)
        {
            var expectBrace = it.LangKey == "oa.workcal.imported";
            foreach (var v in new[] { it.ZhCN, it.ZhTW, it.En, it.Ja, it.Ko })
            {
                var hasBrace = v!.Contains('{');
                Assert.True(hasBrace == expectBrace, $"{it.LangKey} 花括号占位异常：{v}");
            }
        }
        var imported = I18nOaEngineInfraScreenSeed.Items.Single(x => x.LangKey == "oa.workcal.imported");
        Assert.Contains("{n}", imported.ZhCN);
        Assert.Contains("{n}", imported.En);
    }

    [Fact]
    public void Items_NoCollisionWithSiblingSeeds_InsertOnlySafe()
    {
        // SeedLangs 为 insert-only（波①T10 教训）——本波键须全新。与最易撞的同域 seed 交叉核对零重复。
        var mine = I18nOaEngineInfraScreenSeed.Items.Select(x => x.LangKey).ToHashSet();
        var siblings = new[]
        {
            I18nOaServiceTaskScreenSeed.Items,      // oa.designer.svc.* 同域
            I18nOaKernelHardeningScreenSeed.Items,
            I18nOaFlowTriggerScreenSeed.Items,
            I18nOaInboxUxScreenSeed.Items,
            I18nOaInboxScreenSeed.Items,            // nav.733/734 所在
            I18nOaDesignerScreenSeed.Items,         // nav.738 所在
            I18nTenantComplianceSeed.Items,         // platform.tenant.* 所在
        }.SelectMany(a => a).Select(x => x.LangKey).ToHashSet();

        var collisions = mine.Intersect(siblings).ToList();
        Assert.True(collisions.Count == 0, "与既有 seed 撞键：" + string.Join(", ", collisions));
    }

    [Fact]
    public void TenantComplianceSeed_CarriesTimeZoneKeys()
    {
        // E-T2 的 platform.tenant.timeZone* 按平台域惯例落 I18nTenantComplianceSeed。
        var keys = I18nTenantComplianceSeed.Items.Select(x => x.LangKey).ToHashSet();
        Assert.Contains("platform.tenant.timeZone", keys);
        Assert.Contains("platform.tenant.timeZonePlaceholder", keys);
        Assert.Contains("platform.tenant.timeZoneHint", keys);
    }
}
