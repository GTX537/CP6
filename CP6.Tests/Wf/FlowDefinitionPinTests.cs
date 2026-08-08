using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Wf;

public class FlowDefinitionPinTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    private static string Schema(Guid approver) =>
        $$"""{"start":"start","nodes":[{"id":"start","type":"start"},{"id":"approve","type":"approval","approverStrategy":"Specified","approverUserId":"{{approver}}"},{"id":"end","type":"end"}],"edges":[{"from":"start","to":"approve"},{"from":"approve","to":"end"}]}""";

    [Fact]
    public async Task V1_in_flight_keeps_v1_while_new_instance_pins_v2()
    {
        await using var db = NewDb();
        var a1 = Guid.NewGuid();
        var a2 = Guid.NewGuid();
        var definitions = new FlowDefService(db);
        var v1Draft = await definitions.SaveDraftAsync("leave", "Leave", null, Schema(a1), null);
        var v1 = await definitions.PublishAsync("leave", v1Draft.RowVersion, Guid.NewGuid());
        var engine = new FlowEngine(db, new ApproverResolver(db));
        var oldId = await engine.SubmitAsync("leave", Guid.NewGuid(), "{}");

        var v2Draft = await definitions.SaveDraftAsync("leave", "Leave v2", null, Schema(a2), null);
        var v2 = await definitions.PublishAsync("leave", v2Draft.RowVersion, Guid.NewGuid());
        var newId = await engine.SubmitAsync("leave", Guid.NewGuid(), "{}");

        var oldInstance = await db.Wf_FlowInstances.SingleAsync(x => x.Id == oldId);
        var newInstance = await db.Wf_FlowInstances.SingleAsync(x => x.Id == newId);
        Assert.Equal(v1.VersionId, oldInstance.FlowDefVersionId);
        Assert.Equal(v2.VersionId, newInstance.FlowDefVersionId);
        Assert.Equal(a1, (await db.Wf_FlowTasks.SingleAsync(x => x.InstanceId == oldId)).AssigneeId);
        Assert.Equal(a2, (await db.Wf_FlowTasks.SingleAsync(x => x.InstanceId == newId)).AssigneeId);
    }

    [Fact]
    public async Task Disable_blocks_new_start_but_pinned_instance_can_finish()
    {
        await using var db = NewDb();
        var approver = Guid.NewGuid();
        var definitions = new FlowDefService(db);
        var draft = await definitions.SaveDraftAsync("leave", "Leave", null, Schema(approver), null);
        await definitions.PublishAsync("leave", draft.RowVersion, Guid.NewGuid());
        var engine = new FlowEngine(db, new ApproverResolver(db));
        var instanceId = await engine.SubmitAsync("leave", Guid.NewGuid(), "{}");

        var head = await db.Wf_FlowDefs.SingleAsync();
        head.Enable = false;
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => engine.SubmitAsync("leave", Guid.NewGuid(), "{}"));
        Assert.Equal("E-WF-029", ex.Message);
        var task = await db.Wf_FlowTasks.SingleAsync(x => x.InstanceId == instanceId);
        await engine.ActAsync(task.Id, approver, true);
        Assert.Equal(FlowInstanceStatus.Approved,
            (await db.Wf_FlowInstances.SingleAsync(x => x.Id == instanceId)).Status);
    }

    [Fact]
    public async Task Business_start_has_flow_pin_and_no_form_pins()
    {
        await using var db = NewDb();
        var approver = Guid.NewGuid();
        var definitions = new FlowDefService(db);
        var draft = await definitions.SaveDraftAsync("business", "Business", null,
            Schema(approver), null);
        await definitions.PublishAsync("business", draft.RowVersion, Guid.NewGuid());

        var id = await new FlowEngine(db, new ApproverResolver(db))
            .SubmitAsync("business", Guid.NewGuid(), "{}", "PUR_PR", "PR1");
        var instance = await db.Wf_FlowInstances.SingleAsync(x => x.Id == id);

        Assert.NotNull(instance.FlowDefVersionId);
        Assert.Null(instance.FormDefVersionId);
        Assert.Null(instance.FormDataId);
    }
}
