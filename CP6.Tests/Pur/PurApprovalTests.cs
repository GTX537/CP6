using CP6.Core.EFDbContext;
using CP6.Core.Services.Pub;
using CP6.Core.Services.Pur;
using CP6.Core.Services.Pur.Contracts;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Pub;
using CP6.Entity.DomainModels.Pur;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CP6.Tests.Pur;

/// <summary>
/// 采购 OA 审批接真实 — 激活方法单测（P-D1 接桩）。
/// 回调要调的最底层：PO/PR 终态状态推进（PendingApproval→Confirmed/Approved，驳回→Draft；幂等；不 SaveChanges）。
/// </summary>
public class PurApprovalTests
{
    private const string Sup = "SUP";

    private static async Task SeedAsync(CP6Context db)
    {
        db.BusinessPartners.Add(new BusinessPartner { BpCd = Sup, BpName = "供应商", SupplierFlg = true });
        db.Pub_DocSequences.Add(new Pub_DocSequence { BizKey = "PO", Prefix = "PO", DateFormat = "yyyyMMdd", SeqLength = 4, ResetCycle = 0 });
        db.Pub_DocSequences.Add(new Pub_DocSequence { BizKey = "PR", Prefix = "PR", DateFormat = "yyyyMMdd", SeqLength = 4, ResetCycle = 0 });
        await db.SaveChangesAsync();
    }

    private static PurchaseOrderService NewPoSvc(CP6Context db, IApprovalService? approval = null)
        => new(db, new SupplierPriceService(db), new FxRateService(db), new SeqService(db), approval ?? new StubApprovalService());

    private static async Task<string> CreatePendingPoAsync(CP6Context db)
    {
        var po = await NewPoSvc(db).CreateAsync(new PoCreateDto
        {
            SupplierId = Sup, OrderDate = new DateTime(2026, 6, 1),
            Lines = { new PoLineCreateDto { ItemId = "ITEM", Qty = 10m, UnitPrice = 5m } },
        }, "u1");
        var row = await db.PurchaseOrders.FirstAsync(p => p.PoNo == po.PoNo);
        row.Status = PoStatus.PendingApproval;    // 模拟已送审进流程
        await db.SaveChangesAsync();
        return po.PoNo;
    }

    [Fact]
    public async Task ConfirmFromApproval_PendingApproval_ToConfirmed()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var poNo = await CreatePendingPoAsync(db);

        await NewPoSvc(db).ConfirmFromApprovalAsync(poNo, "approver");
        await db.SaveChangesAsync();   // 激活方法不 SaveChanges，由引擎/调用方统一提交

        Assert.Equal(PoStatus.Confirmed, (await db.PurchaseOrders.FirstAsync(p => p.PoNo == poNo)).Status);
    }

    [Fact]
    public async Task ConfirmFromApproval_NotPending_NoOp_Idempotent()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var poNo = await CreatePendingPoAsync(db);
        var svc = NewPoSvc(db);
        await svc.ConfirmFromApprovalAsync(poNo, "approver");   // 第一次 → Confirmed
        await db.SaveChangesAsync();

        await svc.ConfirmFromApprovalAsync(poNo, "approver");   // 重放：已 Confirmed 非 Pending → 跳过
        await db.SaveChangesAsync();

        Assert.Equal(PoStatus.Confirmed, (await db.PurchaseOrders.FirstAsync(p => p.PoNo == poNo)).Status);
    }

    [Fact]
    public async Task RejectFromApproval_PendingApproval_ToDraft()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var poNo = await CreatePendingPoAsync(db);

        await NewPoSvc(db).RejectFromApprovalAsync(poNo, "金额过高");
        await db.SaveChangesAsync();

        Assert.Equal(PoStatus.Draft, (await db.PurchaseOrders.FirstAsync(p => p.PoNo == poNo)).Status);   // 回退草稿可重编
    }

    // ───── PR 激活方法 ─────

    private static PurchaseRequestService NewPrSvc(CP6Context db, IApprovalService? approval = null)
        => new(db, new SeqService(db), approval ?? new StubApprovalService(), NewPoSvc(db, approval));

    private static async Task<string> CreateSubmittedPrAsync(CP6Context db)
    {
        var pr = await NewPrSvc(db).CreateAsync(new PrCreateDto
        {
            Lines = { new PrLineCreateDto { ItemId = "ITEM", Qty = 10m, EstPrice = 5m, SuggestSupplierId = Sup } },
        }, "u1");
        var row = await db.PurchaseRequests.FirstAsync(p => p.PrNo == pr.PrNo);
        row.Status = PrStatus.Submitted;   // 模拟已送审进流程
        await db.SaveChangesAsync();
        return pr.PrNo;
    }

    [Fact]
    public async Task ApproveFromApproval_Submitted_ToApproved()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var prNo = await CreateSubmittedPrAsync(db);

        await NewPrSvc(db).ApproveFromApprovalAsync(prNo, "approver");
        await db.SaveChangesAsync();

        Assert.Equal(PrStatus.Approved, (await db.PurchaseRequests.FirstAsync(p => p.PrNo == prNo)).Status);
    }

    [Fact]
    public async Task RejectFromApproval_Pr_Submitted_ToDraft()
    {
        using var db = TestHelper.CreateInMemoryContext();
        await SeedAsync(db);
        var prNo = await CreateSubmittedPrAsync(db);

        await NewPrSvc(db).RejectFromApprovalAsync(prNo, "不批");
        await db.SaveChangesAsync();

        Assert.Equal(PrStatus.Draft, (await db.PurchaseRequests.FirstAsync(p => p.PrNo == prNo)).Status);   // 回退草稿可重编重送
    }

    // ───── ApprovalServiceAdapter：有绑定→起 OA 流程 / 无绑定→自动放行 ─────

    /// <summary>OA 审批引擎假实现：记录起流程入参，返回固定实例 Id。</summary>
    private sealed class FakeWfApproval : CP6.Core.Services.Wf.IApprovalService
    {
        public Guid ReturnId = Guid.NewGuid();
        public string? LastBizType, LastBizId;
        public Task<Guid> SubmitAsync(string bizType, string bizId, Guid starterId, object? formSnapshot = null)
        {
            LastBizType = bizType; LastBizId = bizId;
            return Task.FromResult(ReturnId);
        }
        public Task<CP6.Core.Services.Wf.ApprovalStatus> GetStatusAsync(string bizType, string bizId)
            => Task.FromResult(CP6.Core.Services.Wf.ApprovalStatus.None);
    }

    [Fact]
    public async Task Adapter_NoBinding_AutoApproves()
    {
        using var db = TestHelper.CreateInMemoryContext();
        var adapter = new ApprovalServiceAdapter(new FakeWfApproval(), db);

        var r = await adapter.SubmitAsync(new ApprovalSubmitRequest { BizType = "PUR_PO", BizKey = "PO1", Amount = 100m, Submitter = "admin" });

        Assert.True(r.AutoApproved);                 // 未配绑定 → 向后兼容直通
        Assert.Equal("AUTO-PO1", r.ApprovalRef);
    }

    [Fact]
    public async Task Adapter_WithBinding_DelegatesToOaFlow()
    {
        using var db = TestHelper.CreateInMemoryContext();
        db.Wf_ApprovalBindings.Add(new Wf_ApprovalBinding { Id = Guid.NewGuid(), BizType = "PUR_PO", FlowKey = "po-approve", Enable = true });
        await db.SaveChangesAsync();
        var fake = new FakeWfApproval();
        var adapter = new ApprovalServiceAdapter(fake, db);

        var r = await adapter.SubmitAsync(new ApprovalSubmitRequest { BizType = "PUR_PO", BizKey = "PO1", Amount = 100m, Submitter = "admin" });

        Assert.False(r.AutoApproved);                // 有绑定 → 进 OA 流程
        Assert.Equal(fake.ReturnId.ToString(), r.ApprovalRef);
        Assert.Equal("PUR_PO", fake.LastBizType);    // 委托入参正确
        Assert.Equal("PO1", fake.LastBizId);
    }
}
