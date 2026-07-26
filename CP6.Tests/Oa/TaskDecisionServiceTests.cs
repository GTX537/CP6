using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Oa;

public sealed class TaskDecisionServiceTests
{
    private const string FormSchema =
        """{"fields":[{"name":"requester","type":"input","required":true},{"name":"secret","type":"input"},{"name":"amount","type":"number","required":true}]}""";
    private static readonly Guid Approver = Guid.NewGuid();
    private static string FlowSchema =>
        """{"start":"start","nodes":[{"id":"start","type":"start"},{"id":"approve","type":"approval","approverStrategy":"Specified","approverUserId":"__APPROVER__","fieldPerms":{"requester":"readonly","secret":"hidden","amount":"edit"}},{"id":"end","type":"end"}],"edges":[{"from":"start","to":"approve"},{"from":"approve","to":"end"}]}"""
            .Replace("__APPROVER__", Approver.ToString());

    private static CP6Context NewDb(params IInterceptor[] interceptors) => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .AddInterceptors(interceptors)
        .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    private static async Task<(TaskDecisionService Service, Guid TaskId, Wf_FormData Data)> SetupAsync(CP6Context db)
    {
        var forms = new FormService(db);
        var formDraft = await forms.SaveDraftAsync("expense", "Expense", FormSchema, null);
        await forms.PublishAsync("expense", formDraft.RowVersion, Guid.NewGuid());
        var flows = new FlowDefService(db);
        var flowDraft = await flows.SaveDraftAsync("expense-flow", "Expense flow", null, FlowSchema, null);
        await flows.PublishAsync("expense-flow", flowDraft.RowVersion, Guid.NewGuid());
        var form = await db.Wf_FormDefs.SingleAsync();
        var flow = await db.Wf_FlowDefs.SingleAsync();
        db.Wf_FormFlowBindings.Add(new Wf_FormFlowBinding
        {
            Id = Guid.NewGuid(), FormDefId = form.Id, FlowDefId = flow.Id, Enable = true
        });
        await db.SaveChangesAsync();

        var engine = new FlowEngine(db, new ApproverResolver(db));
        var submissions = new FormSubmissionService(db, new DefinitionVersionResolver(db), forms, engine);
        using var input = JsonDocument.Parse("""{"requester":"Alice","secret":"classified","amount":10}""");
        await submissions.SubmitAsync(new("expense", Guid.NewGuid(), Guid.NewGuid().ToString(),
            input.RootElement.Clone(), null));
        var task = await db.Wf_FlowTasks.SingleAsync(x => x.Status == FlowTaskStatus.Pending);
        var data = await db.Wf_FormDatas.SingleAsync();
        var delegates = new DelegateService(db);
        var access = new OaInstanceAccessService(db, delegates);
        var projection = new FormFieldProjectionService(db);
        return (new TaskDecisionService(db, access, projection, forms, engine), task.Id, data);
    }

    private static JsonElement Patch(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    [Fact]
    public async Task Edit_patch_updates_authoritative_data_and_readonly_hidden_unknown_are_rejected()
    {
        await using var db = NewDb();
        var (service, taskId, data) = await SetupAsync(db);

        foreach (var forbidden in new[] { """{"requester":"Mallory"}""", """{"secret":"leak"}""", """{"unknown":1}""" })
        {
            var error = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                service.DecideAsync(new(taskId, Approver, Approver, "approve", null,
                    Patch(forbidden), data.RowVersion)));
            Assert.Equal("E-WF-042", error.Message);
            Assert.Contains(@"""requester"":""Alice""", (await db.Wf_FormDatas.SingleAsync()).DataJson);
            db.ChangeTracker.Clear();
        }

        var result = await service.DecideAsync(new(taskId, Approver, Approver, "approve", "ok",
            Patch("""{"amount":20}"""), data.RowVersion));
        Assert.Equal(FlowTaskStatus.Approved, result.TaskStatus);
        var updated = await db.Wf_FormDatas.SingleAsync();
        Assert.Contains(@"""amount"":20", updated.DataJson);
        Assert.Equal(updated.DataJson, (await db.Wf_FlowInstances.SingleAsync()).VarsJson);
        var resolver = new ApproverResolver(db);
        var detail = await new InboxService(db, new FlowEngine(db, resolver),
            new ForecastService(db, resolver, new ApprovalStagePlanner(resolver)))
            .DetailAsync(Approver, Approver, result.InstanceId);
        var serialized = JsonSerializer.Serialize(detail);
        Assert.DoesNotContain("classified", serialized);
        Assert.DoesNotContain("VarsJson", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.All(detail!.Snapshots, snapshot => Assert.DoesNotContain("secret", snapshot.DataJson));
    }

    [Fact]
    public async Task Stale_form_data_rowversion_returns_conflict_before_mutation()
    {
        await using var db = NewDb();
        var (service, taskId, data) = await SetupAsync(db);
        data.RowVersion = new byte[] { 1 };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DecideAsync(new(taskId, Approver, Approver, "approve", null,
                Patch("""{"amount":50}"""), new byte[] { 2 })));

        Assert.Equal("E-WF-049", error.Message);
        Assert.Equal(FlowTaskStatus.Pending, (await db.Wf_FlowTasks.SingleAsync()).Status);
        Assert.Contains(@"""amount"":10", (await db.Wf_FormDatas.SingleAsync()).DataJson);
    }

    [Fact]
    public async Task Concurrency_retry_repeats_projection_patch_compute_and_action()
    {
        var interceptor = new ThrowOnceConcurrencyInterceptor();
        await using var db = NewDb(interceptor);
        var (_, taskId, data) = await SetupAsync(db);
        var forms = new FormService(db);
        var engine = new FlowEngine(db, new ApproverResolver(db));
        var projection = new CountingProjection(new FormFieldProjectionService(db));
        var access = new OaInstanceAccessService(db, new DelegateService(db));
        var service = new TaskDecisionService(db, access, projection, forms, engine);
        interceptor.Armed = true;

        await service.DecideAsync(new(taskId, Approver, Approver, "approve", null,
            Patch("""{"amount":30}"""), data.RowVersion));

        Assert.True(interceptor.Thrown);
        Assert.Equal(2, projection.DecisionCalls);
        Assert.Contains(@"""amount"":30", (await db.Wf_FormDatas.SingleAsync()).DataJson);
        Assert.Equal(FlowTaskStatus.Approved, (await db.Wf_FlowTasks.SingleAsync()).Status);
    }

    [Fact]
    public async Task Batch_rejects_tasks_whose_node_has_edit_fields()
    {
        await using var db = NewDb();
        var (_, taskId, _) = await SetupAsync(db);
        var resolver = new ApproverResolver(db);
        var inbox = new InboxService(db, new FlowEngine(db, resolver),
            new ForecastService(db, resolver, new ApprovalStagePlanner(resolver)));

        var result = Assert.Single(await inbox.ActBatchAsAsync(
            Approver, null, new[] { taskId }, approve: true));

        Assert.False(result.Ok);
        Assert.Equal("E-WF-042", result.Error);
        Assert.Equal(FlowTaskStatus.Pending, (await db.Wf_FlowTasks.SingleAsync()).Status);
    }

    private sealed class CountingProjection(IFormFieldProjectionService inner) : IFormFieldProjectionService
    {
        public int DecisionCalls { get; private set; }
        public Task<ProjectedForm> ProjectAsync(Guid instanceId, Guid viewerId, string dataJson,
            CancellationToken ct = default) => inner.ProjectAsync(instanceId, viewerId, dataJson, ct);
        public Task<IReadOnlyDictionary<string, string>> DecisionMaskAsync(
            Guid instanceId, string nodeId, string dataJson, CancellationToken ct = default)
        {
            DecisionCalls++;
            return inner.DecisionMaskAsync(instanceId, nodeId, dataJson, ct);
        }
    }

    private sealed class ThrowOnceConcurrencyInterceptor : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public bool Thrown { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Armed && !Thrown)
            {
                Thrown = true;
                throw new DbUpdateConcurrencyException("simulated decision race");
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
