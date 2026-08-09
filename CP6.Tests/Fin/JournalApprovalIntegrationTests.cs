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
/// OA 章05 §7 ★ MVP 兑现（A-3）：财务手工凭证经 OA 审批落地的端到端集成。
/// 真 FlowEngine + ApprovalDispatcher + JournalApprovalCallback + JournalEntryService 共享一个 DbContext，
/// 验证：通过→Posted（复核人=审批人）/ 驳回→Rejected / 自审被 maker-checker 拒且整体原子回滚。
/// </summary>
public class JournalApprovalIntegrationTests
{
    private const string BizType = "FinJournalPost";
    private const string FlowKey = "fin-journal-post";

    private static DbContextOptions<CP6Context> Opts(string dbName) =>
        new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    /// <summary>装配共享同一 DbContext 的全套服务（模拟一次请求 scope）。</summary>
    private static (IJournalEntryService journal, IApprovalService approval, IFlowEngine engine) Build(CP6Context db)
    {
        var journal = new JournalEntryService(db, new FiscalPeriodService(db, 1), new FinSequenceService(db));
        var dispatcher = new ApprovalDispatcher(new IApprovalCallback[] { new JournalApprovalCallback(journal) });
        var engine = new FlowEngine(db, new ApproverResolver(db), notifier: null, dispatcher: dispatcher);
        var approval = new ApprovalService(db, engine);
        return (journal, approval, engine);
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
            Id = Guid.NewGuid(), FlowKey = FlowKey, FlowName = "凭证过账审批", FormKey = BizType,
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

    /// <summary>建一张余额相等的待复核凭证（银行 1002 借 / 主营收入 4001 贷），返回 entryId。</summary>
    private static async Task<Guid> PendingReviewVoucher(CP6Context db, string maker, decimal amount = 100m)
    {
        var gl = new GlAccountService(db);
        if (!await db.GlAccounts.AnyAsync()) await gl.ImportTemplateAsync(FinCoaTemplate.CnGaap, "seed");
        var bank = (await gl.GetByCodeAsync("1002"))!.Id;
        var rev = (await gl.GetByCodeAsync("4001"))!.Id;
        var (journal, _, _) = Build(db);
        var entryId = await journal.CreateDraftAsync(new JournalEntry
        {
            VoucherDate = new DateTime(2026, 6, 15),
            Source = VoucherSource.Manual,
            Description = "OA 审批集成测试",
            Lines =
            {
                new JournalLine { AccountId = bank, Debit = amount },
                new JournalLine { AccountId = rev, Credit = amount },
            },
        }, maker);
        var sub = await journal.SubmitForReviewAsync(entryId);
        Assert.True(sub.Ok);
        return entryId;
    }

    [Fact]
    public async Task Approve_Flow_PostsVoucher_WithApproverAsChecker()
    {
        var dbName = Guid.NewGuid().ToString();
        var approver = Guid.NewGuid();
        var maker = "u-maker";

        using (var db = new CP6Context(Opts(dbName)))
        {
            await SeedFlowAndBindingAsync(db, approver);
            var entryId = await PendingReviewVoucher(db, maker);

            var (_, approval, engine) = Build(db);
            await approval.SubmitAsync(BizType, entryId.ToString(), starterId: Guid.NewGuid(),
                formSnapshot: new { amount = 100 });

            var task = await db.Wf_FlowTasks.SingleAsync(t => t.Status == FlowTaskStatus.Pending);
            await engine.ActAsync(task.Id, approver, approve: true, "同意过账");
        }

        // 用全新 context 读已落库结果（验证真的持久化）
        using (var db = new CP6Context(Opts(dbName)))
        {
            var e = await db.JournalEntries.SingleAsync();
            Assert.Equal(JournalStatus.Posted, e.Status);
            Assert.Equal(approver.ToString(), e.CheckerId);   // 复核人 = OA 审批人
            var inst = await db.Wf_FlowInstances.SingleAsync();
            Assert.Equal(FlowInstanceStatus.Approved, inst.Status);
        }
    }

    [Fact]
    public async Task Reject_Flow_RejectsVoucher()
    {
        var dbName = Guid.NewGuid().ToString();
        var approver = Guid.NewGuid();

        using (var db = new CP6Context(Opts(dbName)))
        {
            await SeedFlowAndBindingAsync(db, approver);
            var entryId = await PendingReviewVoucher(db, "u-maker");

            var (_, approval, engine) = Build(db);
            await approval.SubmitAsync(BizType, entryId.ToString(), Guid.NewGuid());

            var task = await db.Wf_FlowTasks.SingleAsync(t => t.Status == FlowTaskStatus.Pending);
            await engine.ActAsync(task.Id, approver, approve: false, "预算不足");
        }

        using (var db = new CP6Context(Opts(dbName)))
        {
            var e = await db.JournalEntries.SingleAsync();
            Assert.Equal(JournalStatus.Rejected, e.Status);
            Assert.Equal("预算不足", e.RejectReason);
            var inst = await db.Wf_FlowInstances.SingleAsync();
            Assert.Equal(FlowInstanceStatus.Rejected, inst.Status);
        }
    }

    [Fact]
    public async Task SelfApproval_MakerEqualsChecker_RollsBack_NothingPersisted()
    {
        var dbName = Guid.NewGuid().ToString();
        var approver = Guid.NewGuid();
        var maker = approver.ToString();   // 制单人 == 审批人 → maker-checker 必拒

        using (var db = new CP6Context(Opts(dbName)))
        {
            await SeedFlowAndBindingAsync(db, approver);
            var entryId = await PendingReviewVoucher(db, maker);

            var (_, approval, engine) = Build(db);
            await approval.SubmitAsync(BizType, entryId.ToString(), Guid.NewGuid());

            var task = await db.Wf_FlowTasks.SingleAsync(t => t.Status == FlowTaskStatus.Pending);
            // 回调内 PostAsync 返 E-FIN-111 → 回调抛 → ActAsync 终态 SaveChanges 不执行
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                engine.ActAsync(task.Id, approver, approve: true));
        }

        // 全新 context：凭证仍待复核、实例仍进行中、任务仍待办 —— 绝不"OA 通过、业务没落地"
        using (var db = new CP6Context(Opts(dbName)))
        {
            var e = await db.JournalEntries.SingleAsync();
            Assert.Equal(JournalStatus.PendingReview, e.Status);
            var inst = await db.Wf_FlowInstances.SingleAsync();
            Assert.Equal(FlowInstanceStatus.Running, inst.Status);
            Assert.Equal(1, await db.Wf_FlowTasks.CountAsync(t => t.Status == FlowTaskStatus.Pending));
        }
    }
}
