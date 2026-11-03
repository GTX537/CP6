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
        Assert.Equal(CP6.Core.Services.Wf.LegacyDefinitionWrites.Code, ex.Message);
    }

    [Fact]
    public async Task Retired_activation_cannot_disable_or_enable_legacy_head()
    {
        using var db = NewDb();
        db.Wf_FlowDefs.AddRange(Def("a", "leave", true), Def("b", "leave", false));
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Admin(db).SetEnabledAsync("a", false));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Admin(db).SetEnabledAsync("b", true));
        Assert.True((await db.Wf_FlowDefs.SingleAsync(d => d.FlowKey == "a")).Enable);
        Assert.False((await db.Wf_FlowDefs.SingleAsync(d => d.FlowKey == "b")).Enable);
    }

    [Fact]
    public async Task Enable_NotExist_ThrowsE006()
    {
        using var db = NewDb();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Admin(db).SetEnabledAsync("zzz", true));
        Assert.Equal(CP6.Core.Services.Wf.LegacyDefinitionWrites.Code, ex.Message);
    }
}
