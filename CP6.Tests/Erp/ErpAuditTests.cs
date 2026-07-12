using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Erp;
using Xunit;

namespace CP6.Tests.Erp;

/// <summary>
/// ERP 钱与主数据关键实体字段级审计接线冒烟（M-ERP 横切 T5）。
/// 照 <see cref="CP6.Tests.Wms.WmsAuditTests"/> / <see cref="CP6.Tests.Space.SpaceAuditTests"/> 范式：
/// InMemory + 假当前用户。验证贴 IAuditable 的 ERP 实体（受注头/明细·製品主档·价表定价主数据）
/// 在 create/update 随业务行同周期写入 Sys_FieldAuditLogs（真实断言实体名/字段/新旧值）；
/// 并回归确认追加型履历 FscChecklist（未贴）不产生审计行（负测试）。
/// </summary>
public class ErpAuditTests
{
    private sealed class FakeUser : ICurrentUserAccessor
    {
        public FakeUser(Guid? id, string? name) { UserId = id; UserName = name; }
        public Guid? UserId { get; }
        public string? UserName { get; }
    }

    private sealed record DiffRow(string Field, string? Old, string? New);

    private static List<DiffRow> ParseChanges(string json)
        => JsonSerializer.Deserialize<List<DiffRow>>(json) ?? new();

    private static readonly Guid _userId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static CP6Context Ctx()
        => TestHelper.CreateInMemoryContext(new FakeUser(_userId, "alice"));

    // ── 受注头（Order）──────────────────────────────────────────────
    [Fact]
    public void Create_Order_writes_op1_audit_row()
    {
        using var db = Ctx();
        var order = new Order { WebOrderNo = "WO20260712-001", CustomerCd = "C001", OrderType = "10" };
        db.Set<Order>().Add(order);
        db.SaveChanges();

        var rows = db.Sys_FieldAuditLogs.Where(x => x.Operation == 1).ToList();
        Assert.Single(rows);
        Assert.Equal(nameof(Order), rows[0].EntityName);
        Assert.Equal(order.Id.ToString(), rows[0].EntityKey);
    }

    [Fact]
    public void Update_Order_fxRate_writes_op2_diff()
    {
        using var db = Ctx();
        var order = new Order { WebOrderNo = "WO20260712-002", CustomerCd = "C001", OrderType = "10", FxRate = 1m };
        db.Set<Order>().Add(order);
        db.SaveChanges();

        order.FxRate = 150m;
        db.SaveChanges();

        var rows = db.Sys_FieldAuditLogs.Where(x => x.Operation == 2).ToList();
        Assert.Single(rows);
        Assert.Equal(nameof(Order), rows[0].EntityName);

        var diffs = ParseChanges(rows[0].Changes);
        Assert.Contains(diffs, d => d.Field == "FxRate" && d.Old == "1" && d.New == "150");
    }

    // ── 受注明细（OrderDetail）— 价格快照·钱 ──────────────────────────
    [Fact]
    public void Update_OrderDetail_amount_writes_op2_diff()
    {
        using var db = Ctx();
        var detail = new OrderDetail
        {
            WebOrderNo = "WO20260712-003",
            WebOrderDetailNo = 1,
            ProductCd = "P001",
            Amount = 1000m,
        };
        db.Set<OrderDetail>().Add(detail);
        db.SaveChanges();

        detail.Amount = 1200m;
        db.SaveChanges();

        var rows = db.Sys_FieldAuditLogs.Where(x => x.Operation == 2).ToList();
        Assert.Single(rows);
        Assert.Equal(nameof(OrderDetail), rows[0].EntityName);

        var diffs = ParseChanges(rows[0].Changes);
        Assert.Contains(diffs, d => d.Field == "Amount" && d.New == "1200");
    }

    // ── 製品主档（ProductMaster）─────────────────────────────────────
    [Fact]
    public void Create_ProductMaster_writes_op1_audit_row()
    {
        using var db = Ctx();
        var product = new ProductMaster { ProductCd = "PRD-000001" };
        db.Set<ProductMaster>().Add(product);
        db.SaveChanges();

        var rows = db.Sys_FieldAuditLogs.Where(x => x.Operation == 1).ToList();
        Assert.Single(rows);
        Assert.Equal(nameof(ProductMaster), rows[0].EntityName);
        Assert.Equal(product.Id.ToString(), rows[0].EntityKey);
    }

    // ── 价表/定价主数据（FxRate 為替）— 改它影响算价 ────────────────────
    [Fact]
    public void Update_FxRate_rate_writes_op2_diff()
    {
        using var db = Ctx();
        var fx = new FxRate { CurrencyCd = "USD", RateDate = new DateTime(2026, 7, 12), Rate = 150m };
        db.Set<FxRate>().Add(fx);
        db.SaveChanges();

        fx.Rate = 155m;
        db.SaveChanges();

        var rows = db.Sys_FieldAuditLogs.Where(x => x.Operation == 2).ToList();
        Assert.Single(rows);
        Assert.Equal(nameof(FxRate), rows[0].EntityName);

        var diffs = ParseChanges(rows[0].Changes);
        Assert.Contains(diffs, d => d.Field == "Rate" && d.Old == "150" && d.New == "155");
    }

    // ── 价表 master（SheetUnitPrice シート単価）──────────────────────
    [Fact]
    public void Create_SheetUnitPrice_writes_op1_audit_row()
    {
        using var db = Ctx();
        var price = new SheetUnitPrice { BaseCd = "B01", CustomerCd = "C001", SheetFlute = "A" };
        db.Set<SheetUnitPrice>().Add(price);
        db.SaveChanges();

        var rows = db.Sys_FieldAuditLogs.Where(x => x.Operation == 1).ToList();
        Assert.Single(rows);
        Assert.Equal(nameof(SheetUnitPrice), rows[0].EntityName);
        Assert.Equal(price.Id.ToString(), rows[0].EntityKey);
    }

    // ── 负测试：追加型履历（FscChecklist）未贴 IAuditable → 零审计行 ──────
    [Fact]
    public void Append_FscChecklist_writes_no_audit_row()
    {
        // 追加型 FSC 発行履歴：豁免 IAuditable，插入不应产生任何字段审计行。
        using var db = Ctx();
        var fsc = new FscChecklist
        {
            FscManagementNo = "FSC20260712-001",
            QtnNo = "QTN001",
            QtnCalcNo = "QC001",
        };
        db.Set<FscChecklist>().Add(fsc);
        db.SaveChanges();

        Assert.Empty(db.Sys_FieldAuditLogs.ToList());
    }
}
