using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

public class ApproverResolverAdvancedTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options);

    [Fact]
    public void RichRule_ConstructsWithOptionalMembers()
    {
        var leaf = new ApproverRule(ApproverStrategy.Starter, null, null, null) { When = "amount > 10" };
        var grp = new ApproverRule(ApproverStrategy.Group, null, null, null) { Members = new[] { leaf } };
        Assert.Equal("amount > 10", grp.Members!.Single().When);
        Assert.Equal(ApproverStrategy.Group, grp.Strategy);
    }

    [Fact]
    public async Task ExistingStrategies_StillWork_WithVarsJsonNull()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid();
        var res = await new ApproverResolver(db).ResolveAsync(
            new ApproverRule(ApproverStrategy.Starter, null, null, null),
            new ApproverResolveContext { StarterUserId = starter, VarsJson = null });
        Assert.Equal(starter, res.ApproverIds.Single());
    }
}
