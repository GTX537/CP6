namespace CP6.Tests;

/// <summary>
/// 低层码计算服务测试（MRP P1 · spec §5.3 step0 / §10）。
///
/// 低层码 = 物料在 BOM 中出现的"最深层级"——保证 MRP 逐层 net 时该料汇齐所有上层需求后才净算一次。
/// 成环 BOM 检出报错（E-PLAN-循环BOM）。
/// </summary>
public class LowLevelCodeServiceTests
{
    [Fact]
    public void Compute_SharedItem_TakesDeepestLevel()
    {
        var svc = new LowLevelCodeService();
        // 成品X→板→原纸（原纸在 X 下层级 2）；成品Y→原纸 直挂（层级 1）→ 低层码取最深 = 2
        var edges = new[]
        {
            new BomEdge("X", "板"),
            new BomEdge("板", "原纸"),
            new BomEdge("Y", "原纸"),
        };

        var codes = svc.Compute(edges);

        Assert.Equal(0, codes["X"]);
        Assert.Equal(0, codes["Y"]);
        Assert.Equal(1, codes["板"]);
        Assert.Equal(2, codes["原纸"]);
    }

    [Fact]
    public void Compute_DuplicateEdges_Deduped()
    {
        var svc = new LowLevelCodeService();
        // 同一父子关系经不同工程链重复出现 → 去重，不应判成环
        var edges = new[]
        {
            new BomEdge("X", "原纸"),
            new BomEdge("X", "原纸"),
        };

        var codes = svc.Compute(edges);

        Assert.Equal(0, codes["X"]);
        Assert.Equal(1, codes["原纸"]);
    }

    [Fact]
    public void Compute_Cycle_Throws()
    {
        var svc = new LowLevelCodeService();
        var edges = new[]
        {
            new BomEdge("A", "B"),
            new BomEdge("B", "A"),
        };

        var ex = Assert.Throws<InvalidOperationException>(() => svc.Compute(edges));
        Assert.Contains("E-PLAN", ex.Message);
    }

    [Fact]
    public void Compute_Empty_ReturnsEmpty()
    {
        var svc = new LowLevelCodeService();
        Assert.Empty(svc.Compute(Array.Empty<BomEdge>()));
    }
}
