using System.Text.Json;
using CP6.Core.Services.CrmIdentity;
using CP6.Entity.DomainModels.Sys;
using CP6.Platform.EntityFramework;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests;

public sealed partial class CrmIdentityWriterPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task User_major_version_stays_upgraded_after_removing_zero_role_and_deleting_user()
    {
        var tenant=Guid.NewGuid();var runtime=Runtime(tenant);
        await using var db=Context(tenant,runtime);
        var user=new Sys_User{UserName="integer-sticky",Password="fixture",RoleId=0};
        db.Sys_Roles.AddRange(new Sys_Role{RoleId=0,RoleName="zero"},new Sys_Role{RoleId=71,RoleName="positive"});
        db.Sys_Users.Add(user);await db.SaveChangesAsync();db.ChangeTracker.Clear();
        var aggregate="user:"+user.Id.ToString("D");
        var first=await db.CrmIdentitySnapshots.AsNoTracking().SingleAsync(x=>x.AggregateId==aggregate);
        Assert.Equal(IdentityEventContracts.UserChangedV2,first.EventType);
        (await db.Sys_Users.SingleAsync(x=>x.Id==user.Id)).RoleId=71;
        await db.SaveChangesAsync();db.ChangeTracker.Clear();
        var second=await db.CrmIdentitySnapshots.AsNoTracking().SingleAsync(x=>x.AggregateId==aggregate);
        Assert.Equal(IdentityEventContracts.UserChangedV2,second.EventType);Assert.Equal(first.Version+1,second.Version);
        Assert.Equal("role:71",JsonDocument.Parse(second.PayloadJson).RootElement.GetProperty("roles")[0].GetString());
        db.Sys_Users.Remove(await db.Sys_Users.SingleAsync(x=>x.Id==user.Id));
        await db.SaveChangesAsync();db.ChangeTracker.Clear();
        var deleted=await db.CrmIdentitySnapshots.AsNoTracking().SingleAsync(x=>x.AggregateId==aggregate);
        Assert.True(deleted.IsDeleted);Assert.Equal(IdentityEventContracts.UserChangedV2,deleted.EventType);
        Assert.Equal(second.Version+1,deleted.Version);
        await using var priority=server.PriorityContext();
        Assert.True(await priority.Set<Cp6OutboxMessage>().AnyAsync(x=>x.AggregateId==aggregate && x.AggregateVersion==deleted.Version));
    }

    [CurrentPermissionPostgresFact]
    public async Task Full_integer_revocation_enqueues_only_priority_with_original_identity_and_version()
    {
        var tenant=Guid.NewGuid();var runtime=Runtime(tenant);await using var db=Context(tenant,runtime);
        var jti=Guid.NewGuid().ToString("D");var aggregate=IdentityEventContracts.TokenAggregate(runtime.Options.Issuer,jti);
        await using(var tx=await db.Database.BeginTransactionAsync())
        {
            await new IdentitySnapshotWriter(db,runtime).RevokeTokenAsync(tenant,jti,"role:0",DateTimeOffset.UtcNow.AddMinutes(5));
            await db.SaveChangesAsync();await tx.CommitAsync();
        }
        var snapshot=await db.CrmIdentitySnapshots.AsNoTracking().SingleAsync(x=>x.AggregateId==aggregate);
        Assert.Equal(IdentityEventContracts.TokenRevokedV2,snapshot.EventType);
        Assert.Equal("role:0",JsonDocument.Parse(snapshot.PayloadJson).RootElement.GetProperty("subjectId").GetString());
        Assert.False(await db.Set<Cp6OutboxMessage>().AnyAsync(x=>x.AggregateId==aggregate));
        await using var priority=server.PriorityContext();
        var message=await priority.Set<Cp6OutboxMessage>().SingleAsync(x=>x.AggregateId==aggregate);
        Assert.Equal(snapshot.Version,message.AggregateVersion);Assert.True(runtime.Validator.IsValid(message.Payload));
    }
}
