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
}
