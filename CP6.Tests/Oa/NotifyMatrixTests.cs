// CP6.Tests/Oa/NotifyMatrixTests.cs
using CP6.Core.Services.Oa;
using Xunit;

namespace CP6.Tests.Oa;

public class NotifyMatrixTests
{
    // ── 三态坍缩：缺行/缺键/缺通道键 → true（spec §2.1）──
    [Theory]
    [InlineData("")]                                            // 空串（等价无行）
    [InlineData("{}")]                                          // 无 notify 键
    [InlineData("""{"notify":{}}""")]                           // notify 空对象（无类型键）
    [InlineData("""{"notify":{"todoCreated":{}}}""")]           // 类型对象存在但无通道键
    [InlineData("NOT_VALID_JSON{{{")]                           // 畸形 JSON 回落 true 不抛
    public void IsEnabled_ThreeStateCollapse_DefaultsTrue(string prefsJson)
    {
        Assert.True(NotifyMatrix.IsEnabled(prefsJson, "todoCreated", NotifyMatrix.ChannelInApp));
        Assert.True(NotifyMatrix.IsEnabled(prefsJson, "todoCreated", NotifyMatrix.ChannelEmail));
    }

    [Fact]
    public void IsEnabled_NewMatrixShape_PerTypePerChannel()
    {
        const string json = """{"notify":{"flowRejected":{"inApp":true,"email":false},"todoCreated":{"inApp":false}}}""";
        Assert.True (NotifyMatrix.IsEnabled(json, "flowRejected", "inApp"));
        Assert.False(NotifyMatrix.IsEnabled(json, "flowRejected", "email"));
        Assert.False(NotifyMatrix.IsEnabled(json, "todoCreated",  "inApp"));
        Assert.True (NotifyMatrix.IsEnabled(json, "todoCreated",  "email"));   // 缺通道键 → true
        Assert.True (NotifyMatrix.IsEnabled(json, "flowApproved", "inApp"));   // 缺类型键 → true
    }

    // ── 遗留扁平形态兼容（C2：既有 notify.{todo,...,email} 语义逐位等价）──
    [Fact]
    public void IsEnabled_LegacyFlat_EventOff_KillsBothChannels()
    {
        const string json = """{"notify":{"todo":false,"email":true}}""";
        Assert.False(NotifyMatrix.IsEnabled(json, "todoCreated", "inApp"));
        Assert.False(NotifyMatrix.IsEnabled(json, "todoCreated", "email"));   // 现状：事件关 → 整跳（含邮件）
        Assert.True (NotifyMatrix.IsEnabled(json, "flowApproved", "inApp")); // 其他事件不受影响
    }

    [Fact]
    public void IsEnabled_LegacyFlat_GlobalEmailOff_KillsOnlyEmail()
    {
        const string json = """{"notify":{"todo":true,"approved":true,"email":false}}""";
        Assert.True (NotifyMatrix.IsEnabled(json, "todoCreated",  "inApp"));
        Assert.False(NotifyMatrix.IsEnabled(json, "todoCreated",  "email"));
        Assert.False(NotifyMatrix.IsEnabled(json, "flowApproved", "email"));
        Assert.False(NotifyMatrix.IsEnabled(json, "flowRejected", "email"));  // 缺 rejected 键也吃全局 email
    }

    [Fact]
    public void IsEnabled_NewShapeWinsOverLegacy_WhenTypeObjectPresent()
    {
        // 同一 notify 里新旧混存：类型键为对象 → 走新形态，无视遗留 email 全局开关
        const string json = """{"notify":{"email":false,"todoCreated":{"inApp":true,"email":true}}}""";
        Assert.True(NotifyMatrix.IsEnabled(json, "todoCreated", "email"));
    }

    // ── 类型轴（反射枚举，数据驱动）+ 邮件动作核定（R1）──
    [Fact]
    public void Rows_ReflectsEnum_WithSupportFlags()
    {
        var rows = NotifyMatrix.Rows();
        Assert.Contains(rows, r => r is { TypeKey: "todoCreated",  TypeValue: 1, InAppSupported: true,  EmailSupported: true });
        Assert.Contains(rows, r => r is { TypeKey: "flowApproved", TypeValue: 2, InAppSupported: true,  EmailSupported: true });
        Assert.Contains(rows, r => r is { TypeKey: "flowRejected", TypeValue: 3, InAppSupported: true,  EmailSupported: true });
        Assert.Contains(rows, r => r is { TypeKey: "timeout",      TypeValue: 4, InAppSupported: false, EmailSupported: false }); // 无发送路径（R1）
        // BranchPruned 未合入时不出现；合入后（hardening spec §4.2）自动长出且双通道 true——不对存在性做负断言，保证两 spec 任意合并顺序都绿
        foreach (var r in rows.Where(r => r.TypeKey == "branchPruned"))
        {
            Assert.True(r.InAppSupported);
            Assert.True(r.EmailSupported);
        }
        // 控制器授权适配（w4-AT1-brief 预检漂移修正）：BranchPruned=5 已由内核 hardening 波合入，
        // PersistentWfNotifier.BranchPrunedAsync(:196-198) 与 FlowRejected 同样有邮件动作 → 双通道 true。
        // 此处补正断言：反射轴此刻必含 branchPruned 行（typeValue 5，双通道支持）。
        Assert.Contains(rows, r => r is { TypeKey: "branchPruned", TypeValue: 5, InAppSupported: true, EmailSupported: true });
    }
}
