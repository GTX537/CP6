using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Pub;
using CP6.Core.Services.Pur;
using CP6.Core.Services.Pur.Contracts;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Pub;
using CP6.Entity.DomainModels.Pur;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using PurApproval = CP6.Core.Services.Pur.Contracts.IApprovalService;

namespace CP6.Tests.Pur;

public sealed class PurchaseRequestApprovalP0Tests
{
    private static readonly Guid Actor = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid Approver = Guid.Parse("20000000-0000-0000-0000-000000000002");

    private sealed class CapturingApproval : PurApproval
    {
        public ApprovalSubmitRequest? Request { get; private set; }
        public Task<ApprovalSubmitResult> SubmitAsync(ApprovalSubmitRequest request)
        {
            Request = request;
            return Task.FromResult(new ApprovalSubmitResult
            {
                ApprovalRef = request.InstanceId!.Value.ToString(),
                AutoApproved = false,
            });
        }
    }

    private sealed class ServiceProviderHolder : IServiceProvider
    {
        public IPurchaseRequestService? PurchaseRequests { get; set; }
        public object? GetService(Type serviceType) =>
            serviceType == typeof(IPurchaseRequestService) ? PurchaseRequests : null;
    }

    private sealed class ThrowingPrCallback : IApprovalCallback
    {
        public string BizType => "PUR_PR";
        public Task OnApprovedAsync(ApprovalCallbackContext ctx) =>
            throw new InvalidOperationException("injected callback failure");
        public Task OnRejectedAsync(ApprovalCallbackContext ctx) =>
            throw new InvalidOperationException("injected callback failure");
    }

    private static CP6Context Db(string? name = null) => new(
        new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(name ?? Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static UserPermissionContext Permission(
        Guid? userId = null, string userName = "alice", int scope = 5,
        bool query = true, bool submit = true)
    {
        var result = new UserPermissionContext
        {
            UserId = userId ?? Actor,
            UserName = userName,
            DataScopes = { ["pur-pr"] = scope },
        };
        if (query) result.ActionKeys.Add("pur-pr:query");
        if (submit) result.ActionKeys.Add("pur-pr:submit");
        return result;
    }

    private static PurchaseRequestService Service(
        CP6Context db, PurApproval approval, IDataScopeFilter? scope = null) =>
        new(db, new SeqService(db), approval, Mock.Of<IPurchaseOrderService>(),
            scope ?? new DataScopeFilter(db));

    private static async Task SeedSequenceAsync(CP6Context db)
    {
        db.Pub_DocSequences.Add(new Pub_DocSequence
        {
            BizKey = "PR", Prefix = "PR", DateFormat = "yyyyMMdd", SeqLength = 4,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<PurchaseRequest> CreateAsync(
        PurchaseRequestService service, decimal? price = 100m, decimal qty = 2m,
        string creator = "alice") =>
        await service.CreateAsync(new PrCreateDto
        {
            RequesterId = creator,
            DeptId = Guid.Parse("30000000-0000-0000-0000-000000000001"),
            RequestDate = new DateTime(2026, 7, 23),
            Lines =
            {
                new PrLineCreateDto
                    { ItemId = "ITEM-1", Qty = qty, EstPrice = price, SuggestSupplierId = "SUP-1" },
                new PrLineCreateDto
                    { ItemId = "ITEM-2", Qty = 1, EstPrice = null, SuggestSupplierId = "SUP-1" },
            },
        }, creator);

    private static async Task<Wf_FlowDefVersion> SeedFlowAsync(
        CP6Context db, string flowKey, Guid approver, bool immediate = false)
    {
        var schema = immediate
            ? new FlowSchema { Nodes = { new FlowNode { Id = "end", Type = "end" } } }
            : new FlowSchema
            {
                Nodes =
                {
                    new FlowNode
                        { Id = "approve", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = approver },
                    new FlowNode { Id = "end", Type = "end" },
                },
                Edges = { new FlowEdge { From = "approve", To = "end" } },
            };
        var head = new Wf_FlowDef
        {
            Id = Guid.NewGuid(), FlowKey = flowKey, FlowName = flowKey,
            FormKey = "", SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = true,
        };
        var version = new Wf_FlowDefVersion
        {
            Id = Guid.NewGuid(), FlowDefId = head.Id, Version = 1,
            Status = WfDefinitionVersionStatus.Published,
            FlowNameSnapshot = flowKey, SchemaJson = head.SchemaJson,
        };
        db.Wf_FlowDefs.Add(head);
        db.Wf_FlowDefVersions.Add(version);
        await db.SaveChangesAsync();
        return version;
    }

    private static (PurchaseRequestService service, FlowEngine engine) RealService(
        CP6Context db, IEnumerable<IApprovalCallback>? callbacks = null)
    {
        var engine = new FlowEngine(db, new ApproverResolver(db), dispatcher:
            new ApprovalDispatcher(callbacks ?? Array.Empty<IApprovalCallback>()));
        var adapter = new ApprovalServiceAdapter(new ApprovalService(db, engine), db);
        return (Service(db, adapter), engine);
    }

    [Fact]
    public async Task P0_AC_P02_ServerBuildsSnapshotFromPersistedHeaderAndLines()
    {
        using var db = Db();
        await SeedSequenceAsync(db);
        var approval = new CapturingApproval();
        var service = Service(db, approval);
        var pr = await CreateAsync(service, 125m, 3m);

        await service.SubmitForApprovalAsync(pr.PrNo, Actor, "alice", Permission());

        var snapshot = Assert.IsType<PurchaseRequestApprovalSnapshot>(approval.Request!.Snapshot);
        Assert.Equal(2, snapshot.LineCount);
        Assert.Equal(375m, snapshot.TotalEstimatedAmount);
        Assert.True(snapshot.HasUnpricedLines);
        Assert.Equal(1, snapshot.SuggestedSupplierCount);
        Assert.Equal(Actor, approval.Request.ActorId);
    }

    [Fact]
    public async Task P0_AC_P03_MissingOrDisabledBindingFailsClosedAndLeavesDraft()
    {
        var name = Guid.NewGuid().ToString();
        using (var db = Db(name))
        {
            await SeedSequenceAsync(db);
            var (service, _) = RealService(db);
            var pr = await CreateAsync(service);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.SubmitForApprovalAsync(pr.PrNo, Actor, "alice", Permission()));
            Assert.Equal("E-WF-031", ex.Message);
        }
        using var verify = Db(name);
        Assert.Equal(PrStatus.Draft, (await verify.PurchaseRequests.SingleAsync()).Status);
        Assert.Empty(verify.Wf_FlowInstances);
    }

    [Fact]
    public async Task P0_AC_P04_TotalAmountSelectsFirstMatchingPublishedFlowVersion()
    {
        using var db = Db();
        await SeedSequenceAsync(db);
        await SeedFlowAsync(db, "fallback", Approver);
        var mid = await SeedFlowAsync(db, "mid", Approver);
        await SeedFlowAsync(db, "high", Approver);
        db.Wf_ApprovalBindings.Add(new Wf_ApprovalBinding
        {
            BizType = "PUR_PR", FlowKey = "fallback", Enable = true,
            ConditionJson = """
                [{"when":"totalEstimatedAmount > 100000","flowKey":"high"},
                 {"when":"totalEstimatedAmount > 10000","flowKey":"mid"}]
                """,
        });
        await db.SaveChangesAsync();
        var (service, _) = RealService(db);
        var pr = await CreateAsync(service, 6000m, 2m);

        await service.SubmitForApprovalAsync(pr.PrNo, Actor, "alice", Permission());

        var instance = await db.Wf_FlowInstances.SingleAsync();
        Assert.Equal("mid", instance.FlowKey);
        Assert.Equal(mid.Id, instance.FlowDefVersionId);
    }

    [Fact]
    public async Task P0_AC_P05_SubmitPersistsSubmittedAndCorrelatedUniqueInstance()
    {
        using var db = Db();
        await SeedSequenceAsync(db);
        var capturing = new CapturingApproval();
        var service = Service(db, capturing);
        var pr = await CreateAsync(service);

        var submitted = await service.SubmitForApprovalAsync(pr.PrNo, Actor, "alice", Permission());

        Assert.Equal(PrStatus.Submitted, submitted.Status);
        Assert.Equal(capturing.Request!.InstanceId!.Value.ToString(), submitted.ApprovalRef);
        Assert.Equal("PUR_PR", capturing.Request.BizType);
        Assert.Equal(pr.PrNo, capturing.Request.BizKey);
    }

    [Fact]
    public async Task P0_AC_P09_ApproveCallbackRequiresCorrelationAndMovesToApproved()
    {
        using var db = Db();
        await SeedSequenceAsync(db);
        var service = Service(db, new CapturingApproval());
        var pr = await CreateAsync(service);
        var submitted = await service.SubmitForApprovalAsync(pr.PrNo, Actor, "alice", Permission());
        var instanceId = Guid.Parse(submitted.ApprovalRef!);

        await service.ApproveFromApprovalAsync(pr.PrNo, instanceId, Approver.ToString());
        await db.SaveChangesAsync();

        Assert.Equal(PrStatus.Approved, (await db.PurchaseRequests.SingleAsync()).Status);
    }

    [Fact]
    public async Task P0_AC_P10_RejectCallbackReturnsToDraftAndKeepsApprovalRef()
    {
        using var db = Db();
        await SeedSequenceAsync(db);
        var service = Service(db, new CapturingApproval());
        var pr = await CreateAsync(service);
        var submitted = await service.SubmitForApprovalAsync(pr.PrNo, Actor, "alice", Permission());
        var instanceId = Guid.Parse(submitted.ApprovalRef!);

        await service.RejectFromApprovalAsync(pr.PrNo, instanceId, "budget");
        await db.SaveChangesAsync();

        var rejected = await db.PurchaseRequests.SingleAsync();
        Assert.Equal(PrStatus.Draft, rejected.Status);
        Assert.Equal(instanceId.ToString(), rejected.ApprovalRef);
    }

    [Fact]
    public async Task P0_AC_P11_CallbackFailureLeavesWorkflowAndBusinessStateUncommitted()
    {
        var name = Guid.NewGuid().ToString();
        Guid taskId;
        string prNo;
        using (var db = Db(name))
        {
            await SeedSequenceAsync(db);
            await SeedFlowAsync(db, "pr-flow", Approver);
            db.Wf_ApprovalBindings.Add(new Wf_ApprovalBinding
                { BizType = "PUR_PR", FlowKey = "pr-flow", Enable = true });
            await db.SaveChangesAsync();
            var (service, engine) = RealService(db, new[] { new ThrowingPrCallback() });
            var pr = await CreateAsync(service);
            prNo = pr.PrNo;
            await service.SubmitForApprovalAsync(prNo, Actor, "alice", Permission());
            taskId = (await db.Wf_FlowTasks.SingleAsync()).Id;

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                engine.ActAsync(taskId, Approver, true, "ok"));
        }
        using var verify = Db(name);
        Assert.Equal(PrStatus.Submitted,
            (await verify.PurchaseRequests.SingleAsync(x => x.PrNo == prNo)).Status);
        Assert.Equal(FlowInstanceStatus.Running, (await verify.Wf_FlowInstances.SingleAsync()).Status);
        Assert.Equal(FlowTaskStatus.Pending, (await verify.Wf_FlowTasks.SingleAsync()).Status);
    }

    [Fact]
    public async Task P0_AC_P12_PrControllerReadEndpointsRequireQueryPermission()
    {
        var service = new Mock<IPurchaseRequestService>();
        var current = new Mock<ICurrentPermissionContext>();
        current.Setup(x => x.GetAsync()).ReturnsAsync(Permission(query: false));
        var controller = new CP6.WebApi.Controllers.Pur.PurchaseRequestController(
            service.Object, current.Object);

        var list = Assert.IsType<Microsoft.AspNetCore.Mvc.ObjectResult>(
            await controller.List(null, null));
        var detail = Assert.IsType<Microsoft.AspNetCore.Mvc.ObjectResult>(
            await controller.Get("PR-1"));

        Assert.Equal(403, list.StatusCode);
        Assert.Equal(403, detail.StatusCode);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task P0_AC_P14_MenuPermissionWithoutBusinessScopeCannotSubmit()
    {
        using var db = Db();
        await SeedSequenceAsync(db);
        var service = Service(db, new CapturingApproval());
        var pr = await CreateAsync(service, creator: "owner");
        var other = Permission(userName: "other", scope: 1);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.SubmitForApprovalAsync(pr.PrNo, Actor, "other", other));
        Assert.Equal(PrStatus.Draft, (await db.PurchaseRequests.SingleAsync()).Status);
    }

    [Fact]
    public async Task P0_AC_P15_StaleCallbackCannotOverwriteResubmittedApprovalRef()
    {
        using var db = Db();
        await SeedSequenceAsync(db);
        var service = Service(db, new CapturingApproval());
        var pr = await CreateAsync(service);
        var first = await service.SubmitForApprovalAsync(pr.PrNo, Actor, "alice", Permission());
        var oldId = Guid.Parse(first.ApprovalRef!);
        await service.RejectFromApprovalAsync(pr.PrNo, oldId, "retry");
        await db.SaveChangesAsync();
        var second = await service.SubmitForApprovalAsync(pr.PrNo, Actor, "alice", Permission());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApproveFromApprovalAsync(pr.PrNo, oldId, Approver.ToString()));

        Assert.Equal("E-PUR-061", ex.Message);
        Assert.Equal(PrStatus.Submitted, second.Status);
        Assert.NotEqual(oldId.ToString(), second.ApprovalRef);
    }
}
