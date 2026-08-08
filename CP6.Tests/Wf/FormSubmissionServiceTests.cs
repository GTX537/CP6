using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Wf;

public class FormSubmissionServiceTests
{
    private const string FormSchema =
        """{"fields":[{"name":"qty","type":"number","required":true},{"name":"price","type":"number","required":true},{"name":"total","type":"number"}],"rules":[{"when":"true","then":[{"action":"compute","target":"total","expr":"qty * price"}]}]}""";
    private const string FlowSchema =
        """{"start":"start","nodes":[{"id":"start","type":"start"},{"id":"end","type":"end"}],"edges":[{"from":"start","to":"end"}]}""";
    private const string TableSchema =
        """
        {"fields":[{"name":"items","label":"采购明细","type":"table","required":true,"minRows":1,"maxRows":10,
        "columns":[{"name":"material","label":"物料","type":"input","required":true},
        {"name":"qty","label":"数量","type":"number","required":true}]}]}
        """;

    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    private static async Task<(FormSubmissionService Service, Wf_FormDef Form)> SetupAsync(
        CP6Context db, bool bind, string schema = FormSchema)
    {
        var forms = new FormService(db);
        var formDraft = await forms.SaveDraftAsync("order", "Order", schema, null);
        await forms.PublishAsync("order", formDraft.RowVersion, Guid.NewGuid());
        var form = await db.Wf_FormDefs.SingleAsync();
        if (bind)
        {
            var flows = new FlowDefService(db);
            var flowDraft = await flows.SaveDraftAsync("order-flow", "Order flow", null, FlowSchema, null);
            await flows.PublishAsync("order-flow", flowDraft.RowVersion, Guid.NewGuid());
            var flow = await db.Wf_FlowDefs.SingleAsync();
            db.Wf_FormFlowBindings.Add(new Wf_FormFlowBinding
            {
                Id = Guid.NewGuid(), FormDefId = form.Id, FlowDefId = flow.Id, Enable = true
            });
            await db.SaveChangesAsync();
        }
        var engine = new FlowEngine(db, new ApproverResolver(db));
        return (new FormSubmissionService(db, new DefinitionVersionResolver(db), forms, engine), form);
    }

    [Fact]
    public async Task Standalone_submit_pins_form_and_uses_server_compute()
    {
        await using var db = NewDb();
        var (service, _) = await SetupAsync(db, bind: false);
        using var document = JsonDocument.Parse("""{"qty":2,"price":3,"total":999}""");

        var result = await service.SubmitAsync(
            new SubmitFormCommand("order", Guid.NewGuid(), Guid.NewGuid().ToString(), document.RootElement.Clone(), null));

        Assert.Null(result.FlowInstanceId);
        var row = await db.Wf_FormDatas.SingleAsync();
        Assert.Equal(result.FormDefVersionId, row.FormDefVersionId);
        Assert.Contains(@"""total"":6", row.DataJson);
    }

    [Fact]
    public async Task Bound_submit_creates_one_pinned_instance_and_is_idempotent()
    {
        await using var db = NewDb();
        var (service, _) = await SetupAsync(db, bind: true);
        using var document = JsonDocument.Parse("""{"price":3,"qty":2}""");
        var key = Guid.NewGuid().ToString();
        var command = new SubmitFormCommand("order", Guid.NewGuid(), key, document.RootElement.Clone(), null);

        var first = await service.SubmitAsync(command);
        var second = await service.SubmitAsync(command);

        Assert.Equal(first, second);
        Assert.Single(await db.Wf_FormDatas.ToListAsync());
        var instance = Assert.Single(await db.Wf_FlowInstances.ToListAsync());
        Assert.Equal(first.FormDataId, instance.FormDataId);
        Assert.Equal(first.FormDefVersionId, instance.FormDefVersionId);
        Assert.NotNull(instance.FlowDefVersionId);
    }

    [Fact]
    public async Task Same_key_with_different_payload_is_rejected()
    {
        await using var db = NewDb();
        var (service, _) = await SetupAsync(db, bind: false);
        var key = Guid.NewGuid().ToString();
        using var one = JsonDocument.Parse("""{"qty":2,"price":3}""");
        using var two = JsonDocument.Parse("""{"qty":4,"price":3}""");
        await service.SubmitAsync(new("order", Guid.NewGuid(), key, one.RootElement.Clone(), null));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SubmitAsync(new("order", Guid.NewGuid(), key, two.RootElement.Clone(), null)));
        Assert.Equal("E-WF-044", ex.Message);
        Assert.Single(await db.Wf_FormDatas.ToListAsync());
    }

    [Theory]
    [InlineData("""{"qty":2,"price":3,"unknown":1}""", "E-WF-039")]
    [InlineData("""{"price":3}""", "E-WF-047")]
    [InlineData("""{"qty":"two","price":3}""", "E-WF-047")]
    public async Task Invalid_payload_is_rejected_without_data(string json, string code)
    {
        await using var db = NewDb();
        var (service, _) = await SetupAsync(db, bind: false);
        using var document = JsonDocument.Parse(json);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SubmitAsync(new("order", Guid.NewGuid(), Guid.NewGuid().ToString(),
                document.RootElement.Clone(), null)));

        Assert.StartsWith(code, ex.Message);
        Assert.Empty(await db.Wf_FormDatas.ToListAsync());
    }

    [Fact]
    public async Task TableSubmit_UsesSameServerValidationAndPersistsFlatRows()
    {
        await using var db = NewDb();
        var (service, _) = await SetupAsync(db, bind: false, TableSchema);
        using var valid = JsonDocument.Parse("""{"items":[{"material":"A-01","qty":2}]}""");

        await service.SubmitAsync(new(
            "order", Guid.NewGuid(), Guid.NewGuid().ToString(), valid.RootElement.Clone(), null));

        var row = await db.Wf_FormDatas.SingleAsync();
        Assert.Contains(@"""material"":""A-01""", row.DataJson);

        using var invalid = JsonDocument.Parse("""{"items":[{"material":"A-01","qty":"two"}]}""");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SubmitAsync(new(
                "order", Guid.NewGuid(), Guid.NewGuid().ToString(), invalid.RootElement.Clone(), null)));
        Assert.StartsWith("E-WF-047", ex.Message);
        Assert.Single(await db.Wf_FormDatas.ToListAsync());
    }
}
