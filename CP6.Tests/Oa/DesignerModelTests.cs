using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

public class DesignerModelTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    [Fact]
    public async Task FlowDef_HasFunctionIdAndFlowCode()
    {
        using var db = NewDb();
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "leave", FlowName = "请假", FormKey = "leave",
            FunctionId = "MSBBPA010", FlowCode = "2887" });
        await db.SaveChangesAsync();
        var got = await db.Wf_FlowDefs.SingleAsync();
        Assert.Equal("MSBBPA010", got.FunctionId);
        Assert.Equal("2887", got.FlowCode);
    }

    [Fact]
    public void FlowNode_HasXYAndCode()
    {
        var n = new FlowNode { Id = "n1", X = 120, Y = 80, Code = "10" };
        Assert.Equal(120, n.X);
        Assert.Equal(80, n.Y);
        Assert.Equal("10", n.Code);   // 状态编号(Delta StateCode)
    }
}
