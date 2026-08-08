using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

/// <summary>OA 章02 表单引擎（B-1）。FormDef/FormData JSON 列 + 服务端 schema 复核 + 改版留痕。</summary>
public class FormServiceTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options);

    private const string LeaveSchema =
        """{"fields":[{"name":"reason","label":"事由","type":"input","required":true,"maxLength":50},{"name":"days","label":"天数","type":"number","required":true}]}""";
    private const string PurchaseSchema =
        """
        {"fields":[{"name":"items","label":"采购明细","type":"table","required":true,"minRows":1,"maxRows":2,
        "columns":[{"name":"material","label":"物料","type":"input","required":true,"maxLength":5},
        {"name":"qty","label":"数量","type":"number","required":true},
        {"name":"unit","label":"单位","type":"select"}]}]}
        """;

    [Fact]
    public async Task SaveDef_Then_GetDef_RoundTrip()
    {
        using var db = NewDb();
        var svc = new FormService(db);
        await svc.SaveDefAsync("leave", "请假单", LeaveSchema, "tester");

        var def = await svc.GetDefAsync("leave");
        Assert.NotNull(def);
        Assert.Equal("请假单", def!.FormName);
        Assert.Equal(1, def.Version);
        Assert.Contains("reason", def.SchemaJson);
    }

    [Fact]
    public async Task SubmitData_Valid_Stored()
    {
        using var db = NewDb();
        var svc = new FormService(db);
        await svc.SaveDefAsync("leave", "请假单", LeaveSchema);

        var id = await svc.SubmitDataAsync("leave", bizId: null, """{"reason":"年假","days":3}""");

        var data = await db.Wf_FormDatas.SingleAsync();
        Assert.Equal(id, data.Id);
        Assert.Equal(1, data.FormVersion);
        Assert.Contains("年假", data.DataJson);
    }

    [Fact]
    public async Task SubmitData_MissingRequired_Throws_AndNotStored()
    {
        using var db = NewDb();
        var svc = new FormService(db);
        await svc.SaveDefAsync("leave", "请假单", LeaveSchema);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.SubmitDataAsync("leave", null, """{"reason":"年假"}"""));   // 缺 days

        Assert.Contains("必填", ex.Message);
        Assert.Equal(0, await db.Wf_FormDatas.CountAsync());   // 校验失败不落库
    }

    [Fact]
    public async Task SubmitData_WrongType_Throws()
    {
        using var db = NewDb();
        var svc = new FormService(db);
        await svc.SaveDefAsync("leave", "请假单", LeaveSchema);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.SubmitDataAsync("leave", null, """{"reason":"年假","days":"three"}"""));

        Assert.Contains("数字", ex.Message);
    }

    [Fact]
    public async Task SaveDef_SchemaChange_BumpsVersion_OldDataUntouched()
    {
        using var db = NewDb();
        var svc = new FormService(db);
        await svc.SaveDefAsync("leave", "请假单", LeaveSchema);
        await svc.SubmitDataAsync("leave", null, """{"reason":"年假","days":3}""");   // v1 数据

        const string v2 = """{"fields":[{"name":"reason","label":"事由","type":"input","required":true}]}""";
        await svc.SaveDefAsync("leave", "请假单v2", v2);   // 改 schema → 升版

        var def = await svc.GetDefAsync("leave");
        Assert.Equal(2, def!.Version);

        var data = await db.Wf_FormDatas.SingleAsync();
        Assert.Equal(1, data.FormVersion);              // 旧数据仍记 v1
        Assert.Contains("年假", data.DataJson);
    }

    [Fact]
    public async Task SubmitData_ValidTable_StoredAsVersionedJson()
    {
        using var db = NewDb();
        var svc = new FormService(db);
        await svc.SaveDefAsync("purchase", "采购单", PurchaseSchema);

        await svc.SubmitDataAsync("purchase", null,
            """{"items":[{"material":"A-01","qty":2,"unit":"pc"}]}""");

        var data = await db.Wf_FormDatas.SingleAsync();
        Assert.Contains(@"""items"":[{", data.DataJson);
        Assert.Contains(@"""qty"":2", data.DataJson);
    }

    [Theory]
    [InlineData("""{"items":[]}""", "必填")]
    [InlineData("""{"items":[{"material":"A-01"}]}""", "数量 必填")]
    [InlineData("""{"items":[{"material":"A-01","qty":"two"}]}""", "必须是数字")]
    [InlineData("""{"items":[{"material":"A-01","qty":1,"unexpected":true}]}""", "未知列")]
    [InlineData("""{"items":[{"material":"TOO-LONG","qty":1}]}""", "最大长度")]
    [InlineData("""{"items":[{"material":"A","qty":1},{"material":"B","qty":2},{"material":"C","qty":3}]}""", "最多允许 2 行")]
    public async Task SubmitData_InvalidTable_ThrowsAndDoesNotStore(string json, string message)
    {
        using var db = NewDb();
        var svc = new FormService(db);
        await svc.SaveDefAsync("purchase", "采购单", PurchaseSchema);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.SubmitDataAsync("purchase", null, json));

        Assert.Contains(message, ex.Message);
        Assert.Empty(await db.Wf_FormDatas.ToListAsync());
    }

    [Fact]
    public async Task Publish_InvalidTableSchema_FailsClosed()
    {
        using var db = NewDb();
        var svc = new FormService(db);
        const string invalid =
            """{"fields":[{"name":"items","type":"table","minRows":2,"maxRows":1,"columns":[]}]}""";
        var draft = await svc.SaveDraftAsync("purchase", "采购单", invalid, null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.PublishAsync("purchase", draft.RowVersion, Guid.NewGuid()));

        Assert.Equal("E-WF-036", ex.Message);
        Assert.Empty(await db.Wf_FormDefVersions
            .Where(x => x.Status == WfDefinitionVersionStatus.Published).ToListAsync());
    }
}
