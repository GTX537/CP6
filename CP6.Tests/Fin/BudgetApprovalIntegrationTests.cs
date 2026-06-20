using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Fin;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Fin;

/// <summary>
/// A5 §8 OA 审批集成测试：预算版本经 OA 全链路（提交→真实 FlowEngine→终态回调）的端到端验证。
/// 真 FlowEngine + ApprovalDispatcher + BudgetApprovalCallback + BudgetService 共享一个 DbContext。
/// 验证：通过→Status=Approved+IsActive=true / 驳回→Status=Rejected+RejectReason 均原子落库。
/// </summary>
public class BudgetApprovalIntegrationTests
{
    private const string BizType = "A5_Budget";
    private const string FlowKey = "budget-approve";

    private static DbContextOptions<CP6Context> Opts(string dbName) =>
        new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    /// <summary>装配共享同一 DbContext 的全套服务（模拟一次请求 scope）。</summary>
    private static (IBudgetService budget, IApprovalService approval, IFlowEngine engine) Build(CP6Context db)
    {
        var seq = new FinSequenceService(db);
        var dispatcher = new ApprovalDispatcher(new IApprovalCallback[]
        {
            new BudgetApprovalCallback(null!)   // 先占位；下面用 budget 真实实例
        });
        // 重建 dispatcher 带真实 budget 服务（引用传递，需真实 budget 实例）
        var engine = new FlowEngine(db, new ApproverResolver(db), notifier: null, dispatcher: null!);
        // 用真实 ApprovalService（委托 engine）
        var approval = new ApprovalService(db, engine);
        var budget = new BudgetService(db, seq, approval);

        // 重建 dispatcher 和 engine，绑定真实 budget
        var realDispatcher = new ApprovalDispatcher(new IApprovalCallback[]
        {
            new BudgetApprovalCallback(budget)
        });
        var realEngine = new FlowEngine(db, new ApproverResolver(db), notifier: null, dispatcher: realDispatcher);
        var realApproval = new ApprovalService(db, realEngine);
        var realBudget = new BudgetService(db, seq, realApproval);

        return (realBudget, realApproval, realEngine);
    }

    private static async Task SeedFlowAndBindingAsync(CP6Context db, Guid approver)
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
        db.Wf_FlowDefs.Add(new Wf_FlowDef
        {
            Id = Guid.NewGuid(), FlowKey = FlowKey, FlowName = "预算审批", FormKey = "BudgetApproval",
            SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = true,
        });
        db.Wf_ApprovalBindings.Add(new Wf_ApprovalBinding
        {
            Id = Guid.NewGuid(), BizType = BizType, FlowKey = FlowKey, Enable = true,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>建一个带 1 行的草稿版本，返回 (budgetId, versionId)。</summary>
    private static async Task<(Guid budgetId, Guid versionId)> SeedBudgetVersionWithLine(CP6Context db)
    {
        // 确保有会计科目（取费用类叶科目作预算行账户）
        var gl = new GlAccountService(db);
        if (!await db.GlAccounts.AnyAsync()) await gl.ImportTemplateAsync(FinCoaTemplate.CnGaap, "seed");

        var (budget, _, _) = Build(db);

        var br = await budget.CreateBudgetAsync(new Budget { Name = "2027预算", FiscalYear = 2027 }, "admin");
        Assert.True(br.Ok);
        var budgetId = br.Data!.Id;

        var vr = await budget.CreateVersionAsync(budgetId, "V1", "admin");
        Assert.True(vr.Ok);
        var versionId = vr.Data!.Id;

        // 取一个叶子费用科目（销售费用 6601 系列 or 管理费用 6602 系列）
        var expAcct = await db.GlAccounts.FirstAsync(a => a.IsLeaf && a.Type == AccountType.Expense);

        var lineSvc = new BudgetLineService(db);
        var lr = await lineSvc.UpsertLineAsync(new BudgetLineDto
        {
            VersionId = versionId,
            AccountId = expAcct.Id,
            AnnualAmount = 1200m,
            SpreadMode = "even",
        });
        Assert.True(lr.Ok);

        return (budgetId, versionId);
    }

    [Fact]
    public async Task Approve_SetsApprovedAndAutoActivates()
    {
        var dbName = Guid.NewGuid().ToString();
        var approver = Guid.NewGuid();
        var starter = Guid.NewGuid();

        Guid versionId;
        using (var db = new CP6Context(Opts(dbName)))
        {
            await SeedFlowAndBindingAsync(db, approver);
            (_, versionId) = await SeedBudgetVersionWithLine(db);

            var (budget, _, engine) = Build(db);

            // 提交送审 → PendingApproval
            var sr = await budget.SubmitForApprovalAsync(versionId, starter, "admin");
            Assert.True(sr.Ok);

            // 验证状态变为 PendingApproval
            var v = await db.BudgetVersions.FindAsync(versionId);
            Assert.Equal(BudgetVersionStatus.PendingApproval, v!.Status);

            // 找到待办任务
            var task = await db.Wf_FlowTasks.SingleAsync(t => t.Status == FlowTaskStatus.Pending);

            // 审批人通过
            await engine.ActAsync(task.Id, approver, approve: true, "预算合理，同意");
        }

        // 用全新 context 验证已持久化
        using (var db = new CP6Context(Opts(dbName)))
        {
            var v = await db.BudgetVersions.FindAsync(versionId);
            Assert.Equal(BudgetVersionStatus.Approved, v!.Status);
            Assert.True(v.IsActive);

            var inst = await db.Wf_FlowInstances.SingleAsync();
            Assert.Equal(FlowInstanceStatus.Approved, inst.Status);
        }
    }

    [Fact]
    public async Task Reject_SetsRejectedWithReason()
    {
        var dbName = Guid.NewGuid().ToString();
        var approver = Guid.NewGuid();
        var starter = Guid.NewGuid();

        Guid versionId;
        using (var db = new CP6Context(Opts(dbName)))
        {
            await SeedFlowAndBindingAsync(db, approver);
            (_, versionId) = await SeedBudgetVersionWithLine(db);

            var (budget, _, engine) = Build(db);

            var sr = await budget.SubmitForApprovalAsync(versionId, starter, "admin");
            Assert.True(sr.Ok);

            var task = await db.Wf_FlowTasks.SingleAsync(t => t.Status == FlowTaskStatus.Pending);

            // 审批人驳回
            await engine.ActAsync(task.Id, approver, approve: false, "超支");
        }

        // 验证驳回状态
        using (var db = new CP6Context(Opts(dbName)))
        {
            var v = await db.BudgetVersions.FindAsync(versionId);
            Assert.Equal(BudgetVersionStatus.Rejected, v!.Status);
            Assert.Equal("超支", v.RejectReason);

            var inst = await db.Wf_FlowInstances.SingleAsync();
            Assert.Equal(FlowInstanceStatus.Rejected, inst.Status);
        }
    }
}
