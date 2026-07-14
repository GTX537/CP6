using CP6.Entity.DomainModels.Sys;
using CP6.WebApi.Seed;

namespace CP6.Tests;

/// <summary>
/// WFS 波⑥ F-T1：I18nOaSubFlowScreenSeed 五语键面双向对账 + insert-only 去重守卫。
///
/// ★ ExpectedKeys 为独立硬编码 oracle（源自波⑥前端 t() 全量 grep：SubFlowNode.vue / NodePropertyPanel.vue /
///   designerModel.ts / FormDetail.vue + 后端错误码），非引用 seed 内部——防自证假绿。
///   键面与实际 t() 消费零缺零孤儿。<c>oa.detail.subIndex</c> 刻意不在集内：FormDetail.vue 以
///   <c>#{{ s.subIndex }}</c> 直渲数据属性、非 t()，入种即孤儿。
/// </summary>
public class I18nOaSubFlowScreenSeedTests
{
    // 前端/后端实际消费的键（草稿键名以代码 t() 为准）。
    private static readonly string[] ExpectedKeys =
    {
        // 节点/面板（SubFlowNode.vue title + 动态 policy.{all|any} 全域；NodePropertyPanel.vue 其余）
        "oa.designer.subflow.title", "oa.designer.subflow.target", "oa.designer.subflow.targetHint",
        "oa.designer.subflow.varsIn", "oa.designer.subflow.varsOut", "oa.designer.subflow.varsHint",
        "oa.designer.subflow.multi", "oa.designer.subflow.collectionVar",
        "oa.designer.subflow.policy", "oa.designer.subflow.policy.all", "oa.designer.subflow.policy.any",
        "oa.designer.subflow.policyHint",
        // 前端校验（designerModel.ts validateClient）
        "oa.designer.errSubFlowConfig",
        // 收件箱父子互链（FormDetail.vue）
        "oa.detail.parentFlow", "oa.detail.subFlows",
        // 后端错误码
        "E-WF-025", "E-WF-026",
    };

    [Fact]
    public void Items_KeySet_MatchesConsumedKeys_Exactly_NoMissingNoOrphan()
    {
        var seeded = I18nOaSubFlowScreenSeed.Items.Select(x => x.LangKey).ToHashSet();
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
        var keys = I18nOaSubFlowScreenSeed.Items.Select(x => x.LangKey).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void Items_AllFiveLanguagesNonEmpty()
    {
        foreach (var it in I18nOaSubFlowScreenSeed.Items)
        {
            Assert.False(string.IsNullOrWhiteSpace(it.ZhCN), $"{it.LangKey} ZhCN 空");
            Assert.False(string.IsNullOrWhiteSpace(it.ZhTW), $"{it.LangKey} ZhTW 空");
            Assert.False(string.IsNullOrWhiteSpace(it.En), $"{it.LangKey} En 空");
            Assert.False(string.IsNullOrWhiteSpace(it.Ja), $"{it.LangKey} Ja 空");
            Assert.False(string.IsNullOrWhiteSpace(it.Ko), $"{it.LangKey} Ko 空");
        }
    }

    [Fact]
    public void Items_JaAndKo_AreRealTranslations_NotChineseCopy()
    {
        // 术语系与既有 I18nOa* 一致：Ko 为谚文必异于 ZhCN；Ja 亦须异于 ZhCN（本波无中日同形词）。
        foreach (var it in I18nOaSubFlowScreenSeed.Items)
        {
            if (it.LangKey.StartsWith("E-WF-")) continue;
            Assert.NotEqual(it.ZhCN, it.Ko);   // 谚文强信号：真译
            Assert.NotEqual(it.ZhCN, it.Ja);   // 假名/片假名强信号：真译
        }
    }

    [Fact]
    public void Items_NoBraceOrLinkedInterpolation_VueI18nSafe()
    {
        // vue-i18n message 编译对裸 { } @ | 敏感（编译失败→画面/标签空白，Program.cs 同步块只兜 I18nLabelSeed）。
        // 本波无具名插值键，故五语值一律禁这四类字符。
        var forbidden = new[] { '{', '}', '@', '|' };
        foreach (var it in I18nOaSubFlowScreenSeed.Items)
            foreach (var v in new[] { it.ZhCN, it.ZhTW, it.En, it.Ja, it.Ko })
                Assert.True(v!.IndexOfAny(forbidden) < 0, $"{it.LangKey} 含 vue-i18n 特殊字符：{v}");
    }

    [Fact]
    public void Items_NoCollisionWithSiblingSeeds_InsertOnlySafe()
    {
        // SeedLangs 为 insert-only（波①T10 教训）——本波键须全新。与最易撞的同域 seed 交叉核对零重复。
        var mine = I18nOaSubFlowScreenSeed.Items.Select(x => x.LangKey).ToHashSet();
        var siblings = new[]
        {
            I18nOaServiceTaskScreenSeed.Items,      // oa.designer.* 同域
            I18nOaKernelHardeningScreenSeed.Items,
            I18nOaFlowTriggerScreenSeed.Items,
            I18nOaEngineInfraScreenSeed.Items,
            I18nOaInboxUxScreenSeed.Items,
            I18nOaInboxScreenSeed.Items,            // oa.detail.* 基础 + E-WF-001~008 所在
            I18nOaSerialSignScreenSeed.Items,       // oa.detail.* + E-WF-011~013 所在
            I18nOaDesignerScreenSeed.Items,
            I18nOaApproverScreenSeed.Items,
        }.SelectMany(a => a).Select(x => x.LangKey).ToHashSet();

        var collisions = mine.Intersect(siblings).ToList();
        Assert.True(collisions.Count == 0, "与既有 seed 撞键：" + string.Join(", ", collisions));
    }
}
