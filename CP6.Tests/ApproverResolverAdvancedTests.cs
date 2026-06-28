using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wf;
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

    [Fact]
    public async Task FormField_SingleGuid_ResolvesEnabledUser()
    {
        using var db = NewDb();
        var u = Guid.NewGuid();
        db.Sys_Users.Add(new Sys_User { Id = u, UserName = "u", Password = "x", Enable = true });
        await db.SaveChangesAsync();

        var res = await new ApproverResolver(db).ResolveAsync(
            new ApproverRule(ApproverStrategy.FormField, null, null, null) { FieldName = "approver" },
            new ApproverResolveContext { StarterUserId = Guid.NewGuid(), VarsJson = $"{{\"approver\":\"{u}\"}}" });
        Assert.Equal(u, res.ApproverIds.Single());
    }

    [Fact]
    public async Task FormField_ArrayOfGuids_ResolvesGroup_ExcludesDisabled()
    {
        using var db = NewDb();
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        db.Sys_Users.AddRange(
            new Sys_User { Id = a, UserName = "a", Password = "x", Enable = true },
            new Sys_User { Id = b, UserName = "b", Password = "x", Enable = false });
        await db.SaveChangesAsync();

        var res = await new ApproverResolver(db).ResolveAsync(
            new ApproverRule(ApproverStrategy.FormField, null, null, null) { FieldName = "approvers" },
            new ApproverResolveContext { VarsJson = $"{{\"approvers\":[\"{a}\",\"{b}\"]}}" });
        Assert.Equal(a, res.ApproverIds.Single());   // b 停用排除
    }

    [Fact]
    public async Task FormField_MissingOrInvalid_Unresolved()
    {
        using var db = NewDb();
        var res1 = await new ApproverResolver(db).ResolveAsync(
            new ApproverRule(ApproverStrategy.FormField, null, null, null) { FieldName = "approver" },
            new ApproverResolveContext { VarsJson = "{}" });
        Assert.False(res1.Resolved);

        var res2 = await new ApproverResolver(db).ResolveAsync(
            new ApproverRule(ApproverStrategy.FormField, null, null, null) { FieldName = "approver" },
            new ApproverResolveContext { VarsJson = "{\"approver\":\"not-a-guid\"}" });
        Assert.False(res2.Resolved);
    }

    [Fact]
    public async Task DataMap_MatchValue_ResolvesUserAndExpandsRole()
    {
        using var db = NewDb();
        var user = Guid.NewGuid(); var roleUser = Guid.NewGuid();
        db.Sys_Users.AddRange(
            new Sys_User { Id = user, UserName = "u", Password = "x", Enable = true },
            new Sys_User { Id = roleUser, UserName = "r", Password = "x", RoleId = 9, Enable = true });
        db.Wf_ApproverMaps.AddRange(
            new Wf_ApproverMap { Id = Guid.NewGuid(), MapKey = "cc", MatchValue = "A100", ApproverUserId = user, Enable = true },
            new Wf_ApproverMap { Id = Guid.NewGuid(), MapKey = "cc", MatchValue = "A100", ApproverRoleId = 9, Enable = true });
        await db.SaveChangesAsync();

        var res = await new ApproverResolver(db).ResolveAsync(
            new ApproverRule(ApproverStrategy.DataMap, null, null, null) { MapKey = "cc", FieldName = "costCenter" },
            new ApproverResolveContext { VarsJson = "{\"costCenter\":\"A100\"}" });
        Assert.Contains(user, res.ApproverIds);
        Assert.Contains(roleUser, res.ApproverIds);
    }

    [Fact]
    public async Task DataMap_NoMatch_Unresolved()
    {
        using var db = NewDb();
        var res = await new ApproverResolver(db).ResolveAsync(
            new ApproverRule(ApproverStrategy.DataMap, null, null, null) { MapKey = "cc", FieldName = "costCenter" },
            new ApproverResolveContext { VarsJson = "{\"costCenter\":\"ZZZ\"}" });
        Assert.False(res.Resolved);
    }
}
