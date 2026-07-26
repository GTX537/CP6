using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Pub;
using CP6.Core.Services.Pur;
using CP6.Core.Services.Pur.Contracts;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Pub;
using CP6.Entity.DomainModels.Pur;
using CP6.Entity.DomainModels.Wf;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace CP6.Tests.Pur;

/// <summary>
/// 采购 OA 审批集成测试：PR/PO 经 OA 全链路（送审→真 FlowEngine→终态回调）端到端验证。
/// 真 FlowEngine + ApprovalDispatcher + Pur 回调 + ApprovalServiceAdapter + Pur 服务共享一个 DbContext。
/// 验证：通过→Confirmed/Approved、驳回→Draft 均原子落库。
/// </summary>
public class PurApprovalIntegrationTests
{
    private const string Sup = "SUP";

    private static DbContextOptions<CP6Context> Opts(string dbName) =>
        new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    /// <summary>延迟解析 PO/PR 服务，打破回调构造期循环（与生产 DI 经 IServiceProvider 同构）。</summary>
    private sealed class SpHolder : IServiceProvider
    {
        public IPurchaseOrderService? Po;
        public IPurchaseRequestService? Pr;
        public object? GetService(Type t)
        {
            if (t == typeof(IPurchaseOrderService)) return Po;
            if (t == typeof(IPurchaseRequestService)) return Pr;
            return null;
        }
    }

    /// <summary>装配共享同一 DbContext 的全套服务（模拟一次请求 scope）。</summary>
    private static (IPurchaseOrderService po, IPurchaseRequestService pr, IFlowEngine engine) Build(CP6Context db)
    {
        var holder = new SpHolder();
        var dispatcher = new ApprovalDispatcher(new IApprovalCallback[] { new PoApprovalCallback(holder), new PrApprovalCallback(holder) });
        var engine = new FlowEngine(db, new ApproverResolver(db), notifier: null, dispatcher: dispatcher);
        var adapter = new ApprovalServiceAdapter(new ApprovalService(db, engine), db);
        var poSvc = new PurchaseOrderService(db, new SupplierPriceService(db), new FxRateService(db), new SeqService(db), adapter);
        var prSvc = new PurchaseRequestService(db, new SeqService(db), adapter, poSvc);
        holder.Po = poSvc; holder.Pr = prSvc;
        return (poSvc, prSvc, engine);
    }

    private static async Task SeedBaseAsync(CP6Context db)
    {
        db.BusinessPartners.Add(new BusinessPartner { BpCd = Sup, BpName = "供应商", SupplierFlg = true });
        db.Pub_DocSequences.Add(new Pub_DocSequence { BizKey = "PO", Prefix = "PO", DateFormat = "yyyyMMdd", SeqLength = 4, ResetCycle = 0 });
        db.Pub_DocSequences.Add(new Pub_DocSequence { BizKey = "PR", Prefix = "PR", DateFormat = "yyyyMMdd", SeqLength = 4, ResetCycle = 0 });
        db.Sys_Users.Add(new Sys_User
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000001"),
            UserName = "admin",
            Password = "x",
            Enable = true,
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedFlowAsync(CP6Context db, string bizType, string flowKey, Guid approver)
    {
        var schema = new FlowSchema
        {
            Nodes =
            {
                new FlowNode { Id = "n1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = approver },
                new FlowNode { Id = "end", Type = "end" },
            },
            Edges = { new FlowEdge { From = "n1", To = "end" } },
        };
        var head = new Wf_FlowDef
        {
            Id = Guid.NewGuid(), FlowKey = flowKey, FlowName = bizType, FormKey = bizType,
            SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = true,
        };
        db.Wf_FlowDefs.Add(head);
        db.Wf_FlowDefVersions.Add(new Wf_FlowDefVersion
        {
            Id = Guid.NewGuid(),
            FlowDefId = head.Id,
            Version = 1,
            Status = WfDefinitionVersionStatus.Published,
            FlowNameSnapshot = bizType,
            SchemaJson = head.SchemaJson,
        });
        db.Wf_ApprovalBindings.Add(new Wf_ApprovalBinding { Id = Guid.NewGuid(), BizType = bizType, FlowKey = flowKey, Enable = true });
        await db.SaveChangesAsync();
    }

    private static async Task<string> CreateDraftPoAsync(IPurchaseOrderService poSvc)
        => (await poSvc.CreateAsync(new PoCreateDto
        {
            SupplierId = Sup, OrderDate = new DateTime(2026, 6, 1),
            Lines = { new PoLineCreateDto { ItemId = "ITEM", Qty = 10m, UnitPrice = 5m } },
        }, "admin")).PoNo;

    [Fact]
    public async Task Po_Submit_Approve_Confirms()
    {
        var dbName = Guid.NewGuid().ToString();
        var approver = Guid.NewGuid();
        string poNo;
        using (var db = new CP6Context(Opts(dbName)))
        {
            await SeedBaseAsync(db);
            await SeedFlowAsync(db, "PUR_PO", "po-approve", approver);
            var (poSvc, _, engine) = Build(db);

            poNo = await CreateDraftPoAsync(poSvc);
            await poSvc.SubmitForApprovalAsync(poNo, "admin");
            Assert.Equal(PoStatus.PendingApproval, (await db.PurchaseOrders.FirstAsync(p => p.PoNo == poNo)).Status);

            var task = await db.Wf_FlowTasks.SingleAsync(t => t.Status == FlowTaskStatus.Pending);
            await engine.ActAsync(task.Id, approver, approve: true, "同意");
        }
        using (var db = new CP6Context(Opts(dbName)))
        {
            Assert.Equal(PoStatus.Confirmed, (await db.PurchaseOrders.FirstAsync(p => p.PoNo == poNo)).Status);
            Assert.Equal(FlowInstanceStatus.Approved, (await db.Wf_FlowInstances.SingleAsync()).Status);
        }
    }

    [Fact]
    public async Task Po_Submit_Reject_BacksToDraft()
    {
        var dbName = Guid.NewGuid().ToString();
        var approver = Guid.NewGuid();
        string poNo;
        using (var db = new CP6Context(Opts(dbName)))
        {
            await SeedBaseAsync(db);
            await SeedFlowAsync(db, "PUR_PO", "po-approve", approver);
            var (poSvc, _, engine) = Build(db);

            poNo = await CreateDraftPoAsync(poSvc);
            await poSvc.SubmitForApprovalAsync(poNo, "admin");
            var task = await db.Wf_FlowTasks.SingleAsync(t => t.Status == FlowTaskStatus.Pending);
            await engine.ActAsync(task.Id, approver, approve: false, "金额过高");
        }
        using (var db = new CP6Context(Opts(dbName)))
            Assert.Equal(PoStatus.Draft, (await db.PurchaseOrders.FirstAsync(p => p.PoNo == poNo)).Status);
    }

    [Fact]
    public async Task Pr_Submit_Approve_Approves()
    {
        var dbName = Guid.NewGuid().ToString();
        var approver = Guid.NewGuid();
        string prNo;
        using (var db = new CP6Context(Opts(dbName)))
        {
            await SeedBaseAsync(db);
            await SeedFlowAsync(db, "PUR_PR", "pr-approve", approver);
            var (_, prSvc, engine) = Build(db);

            prNo = (await prSvc.CreateAsync(new PrCreateDto
            {
                Lines = { new PrLineCreateDto { ItemId = "ITEM", Qty = 10m, EstPrice = 5m, SuggestSupplierId = Sup } },
            }, "admin")).PrNo;
            await prSvc.SubmitForApprovalAsync(prNo, Guid.Parse("10000000-0000-0000-0000-000000000001"),
                "admin", new UserPermissionContext
                {
                    UserId = Guid.Parse("10000000-0000-0000-0000-000000000001"),
                    UserName = "admin",
                    DataScopes = { ["pur-pr"] = 5 },
                });
            Assert.Equal(PrStatus.Submitted, (await db.PurchaseRequests.FirstAsync(p => p.PrNo == prNo)).Status);

            var task = await db.Wf_FlowTasks.SingleAsync(t => t.Status == FlowTaskStatus.Pending);
            await engine.ActAsync(task.Id, approver, approve: true, "同意");
        }
        using (var db = new CP6Context(Opts(dbName)))
            Assert.Equal(PrStatus.Approved, (await db.PurchaseRequests.FirstAsync(p => p.PrNo == prNo)).Status);
    }
}
