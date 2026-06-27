using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

public class FlowAdminServiceTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static IFlowAdminService Admin(CP6Context db) => new FlowAdminService(db);

    private static Wf_FlowDef Def(string flowKey, string formKey, bool enable) => new()
    { Id = Guid.NewGuid(), FlowKey = flowKey, FlowName = flowKey, FormKey = formKey, Version = 1, Enable = enable };

    [Fact]
    public async Task List_ReturnsAllDefs()
    {
        using var db = NewDb();
        db.Wf_FlowDefs.AddRange(Def("a", "fa", true), Def("b", "fb", false));
        await db.SaveChangesAsync();
        Assert.Equal(2, (await Admin(db).ListFlowsAsync()).Count);
    }

    [Fact]
    public async Task Enable_SecondFlowSameForm_ThrowsE008()
    {
        using var db = NewDb();
        db.Wf_FlowDefs.AddRange(Def("a", "leave", true), Def("b", "leave", false));
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Admin(db).SetEnabledAsync("b", true));
        Assert.Equal("E-WF-008", ex.Message);   // leave 已有启用流程 a
    }

    [Fact]
    public async Task Disable_ThenEnableOther_Ok()
    {
        using var db = NewDb();
        db.Wf_FlowDefs.AddRange(Def("a", "leave", true), Def("b", "leave", false));
        await db.SaveChangesAsync();

        await Admin(db).SetEnabledAsync("a", false);   // 先停 a
        await Admin(db).SetEnabledAsync("b", true);    // 再启 b → 不冲突
        Assert.True((await db.Wf_FlowDefs.SingleAsync(d => d.FlowKey == "b")).Enable);
    }

    [Fact]
    public async Task Enable_NotExist_ThrowsE006()
    {
        using var db = NewDb();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Admin(db).SetEnabledAsync("zzz", true));
        Assert.Equal("E-WF-006", ex.Message);
    }
}
