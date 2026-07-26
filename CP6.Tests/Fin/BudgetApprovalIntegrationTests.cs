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

    /// <summary>极简 IServiceProvider：仅为 BudgetApprovalCallback 延迟解析 IBudgetService（同一 db 实例）。</summary>
    private sealed class BudgetSpHolder : IServiceProvider
    {
        public IBudgetService? Budget;
        public object? GetService(Type t) => t == typeof(IBudgetService) ? Budget : null;
    }

    /// <summary>装配共享同一 DbContext 的全套服务（模拟一次请求 scope）。
    /// 回调经 holder 延迟解析 budget，打破构造期循环（与生产 DI 经 IServiceProvider 同构）。</summary>
    private static (IBudgetService budget, IApprovalService approval, IFlowEngine engine) Build(CP6Context db)
    {
        var seq = new FinSequenceService(db);
        var holder = new BudgetSpHolder();
        var dispatcher = new ApprovalDispatcher(new IApprovalCallback[] { new BudgetApprovalCallback(holder) });
        var engine = new FlowEngine(db, new ApproverResolver(db), notifier: null, dispatcher: dispatcher);
        var approval = new ApprovalService(db, engine);
        var budget = new BudgetService(db, seq, approval);
        holder.Budget = budget;   // 回调触发时经 holder 取到真实 budget（共享 db）
        return (budget, approval, engine);
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
        var head = new Wf_FlowDef
        {
            Id = Guid.NewGuid(), FlowKey = FlowKey, FlowName = "预算审批", FormKey = "BudgetApproval",
            SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = true,
        };
        db.Wf_FlowDefs.Add(head);
        db.Wf_FlowDefVersions.Add(new Wf_FlowDefVersion
        {
            Id = Guid.NewGuid(), FlowDefId = head.Id, Version = 1,
            Status = WfDefinitionVersionStatus.Published,
            FlowNameSnapshot = head.FlowName, SchemaJson = head.SchemaJson,
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
