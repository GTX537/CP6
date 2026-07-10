using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Wms;
using Xunit;

namespace CP6.Tests.Wms;

/// <summary>
/// WMS 关键实体字段级审计接线冒烟（M-WMS 横切 T6）。
/// 照 <see cref="CP6.Tests.Space.SpaceAuditTests"/> 范式：InMemory + 假当前用户。
/// 验证贴 IAuditable 的 WMS 实体在 create/update 随业务行同周期写入 Sys_FieldAuditLogs；
/// 并回归确认追加型台账 StockTransaction（未贴）不产生审计行。
/// </summary>
public class WmsAuditTests
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

    [Fact]
    public void Create_StockTake_writes_op1_audit_row()
    {
        using var db = Ctx();
        var take = new StockTake { StockTakeNo = "ST20260710-001", TargetWarehouseCd = "W01" };
        db.Set<StockTake>().Add(take);
        db.SaveChanges();

        var rows = db.Sys_FieldAuditLogs.Where(x => x.Operation == 1).ToList();
        Assert.Single(rows);
        Assert.Equal(nameof(StockTake), rows[0].EntityName);
        Assert.Equal(take.Id.ToString(), rows[0].EntityKey);
    }

    [Fact]
    public void Update_StockTakeDetail_countedQty_writes_op2_diff()
    {
        using var db = Ctx();
        var detail = new StockTakeDetail
        {
            StockTakeNo = "ST20260710-002",
            LineNo = 1,
            WarehouseCd = "W01",
            LocationCd = "L01",
            ProductCd = "P01",
            LotNo = "LOT01",
            BookQty = 100m,
        };
        db.Set<StockTakeDetail>().Add(detail);
        db.SaveChanges();

        detail.CountedQty = 97m;
        detail.DiffQty = -3m;
        db.SaveChanges();

        var rows = db.Sys_FieldAuditLogs.Where(x => x.Operation == 2).ToList();
        Assert.Single(rows);
        Assert.Equal(nameof(StockTakeDetail), rows[0].EntityName);

        var diffs = ParseChanges(rows[0].Changes);
        Assert.Contains(diffs, d => d.Field == "CountedQty" && d.New == "97");
    }

    [Fact]
    public void Append_StockTransaction_writes_no_audit_row()
    {
        // 追加型不可变台账：豁免 IAuditable，插入不应产生任何字段审计行。
        using var db = Ctx();
        var txn = new StockTransaction
        {
            TxnNo = "TX20260710-001",
            WarehouseCd = "W01",
            ProductCd = "P01",
        };
        db.Set<StockTransaction>().Add(txn);
        db.SaveChanges();

        Assert.Empty(db.Sys_FieldAuditLogs.ToList());
    }
}
