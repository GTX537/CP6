using System.Globalization;
using System.Text;
using System.Text.Json;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Sys;
using CP6.Platform.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Platform;

public sealed partial class IamUserAssignmentsPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Integer_role_domain_creates_assigns_and_exports_original_identity_without_remapping()
    {
        await using var h=await Seed();var roles=RoleSession(h);var policy=PolicySession(h);var domain=await PolicyDomain(h);
        var created=new List<object>();var ids=new[]{int.MinValue,-1,0,1,int.MaxValue};
        foreach(var id in ids)
        {
            var result=await roles.Service.ChangeAsync("role-create",await RoleCommand(h,id),h.Actor,default);
            Assert.Equal(id,result.RoleId);
            var command=(await PolicyCommand(h,policy.Service,"actions",id)) with
            {MenuIds=[domain.Menu],Actions=[new(){MenuId=domain.Menu,ActionCode="read"}]};
            await policy.Service.ChangeAsync("actions",id,command,h.Actor,default);
            var user=await h.Service.ChangeAsync("user-create",(await NewCommand(h)) with
            {UserName="full-int-"+created.Count,RoleId=id,RoleIds=[id]},h.Actor,default);
            var row=await h.Db.Sys_Users.SingleAsync(x=>x.Id==user.EntityId);
            row.MustChangePassword=false;await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
            var subject="role:"+id.ToString(CultureInfo.InvariantCulture);
            var permission=await h.Db.CrmIdentitySnapshots.AsNoTracking().SingleAsync(x=>x.TenantId==h.Tenant && x.AggregateId=="permission:"+subject);
            var member=await h.Db.CrmIdentitySnapshots.AsNoTracking().SingleAsync(x=>x.TenantId==h.Tenant && x.AggregateId=="user:"+user.EntityId!.Value.ToString("D"));
            Assert.Equal(subject,JsonDocument.Parse(permission.PayloadJson).RootElement.GetProperty("subjectId").GetString());
            Assert.Equal(subject,JsonDocument.Parse(member.PayloadJson).RootElement.GetProperty("roles")[0].GetString());
            Assert.Equal("com.gtx537.platform.permission.changed.v"+(id<=0?"2":"1"),permission.EventType);
            Assert.Equal("com.gtx537.platform.user.changed.v"+(id<=0?"2":"1"),member.EventType);
            Assert.False(row.IsPlatformAdmin);
            var effective=await new PermissionAggregator(h.Db).BuildAsync(row.Id);
            Assert.Contains(domain.Resource+":read",effective.ActionKeys);
            created.Add(new{roleId=id,userId=user.EntityId,subject});
        }
        await Export("baseline");
        await roles.Service.ChangeAsync("role-update",(await RoleCommand(h,0)) with{Enable=false},h.Actor,default);
        await Export("disabled-role");

        async Task Export(string stage)
        {
            var snapshots=await h.Db.CrmIdentitySnapshots.AsNoTracking().Where(x=>x.TenantId==h.Tenant).OrderBy(x=>x.AggregateId).ToArrayAsync();
            await using var priority=server.PriorityContext();
            var messages=(await h.Db.Set<Cp6OutboxMessage>().Where(x=>x.TenantId==h.Tenant).ToArrayAsync())
                .Concat(await priority.Set<Cp6OutboxMessage>().Where(x=>x.TenantId==h.Tenant).ToArrayAsync()).ToArray();
            foreach(var snapshot in snapshots)
            {
                Assert.Equal(IdentityEventContracts.Hash(Encoding.UTF8.GetBytes(snapshot.PayloadJson)),snapshot.PayloadSha256);
                var original=messages.Single(x=>x.AggregateId==snapshot.AggregateId && x.AggregateVersion==snapshot.Version);
                Assert.Equal(snapshot.PayloadJson,JsonDocument.Parse(original.Payload).RootElement.GetProperty("data").GetRawText());
            }
            if(Environment.GetEnvironmentVariable("CP6_IDENTITY_TRANSFER_DIRECTORY") is {Length:>0} directory)
            {
                Directory.CreateDirectory(directory);
                await File.WriteAllTextAsync(Path.Combine(directory,stage+".json"),JsonSerializer.Serialize(new
                {
                    tenantId=h.Tenant,issuer="https://iam-writer.example.test",resource=domain.Resource,created,
                    snapshots=snapshots.Select(x=>new{x.TenantId,x.AggregateId,x.EventType,x.Version,x.PayloadSha256,x.IsDeleted,x.PayloadJson}),
                    originalEnvelopes=messages.Select(x=>Convert.ToBase64String(x.Payload))
                },IdentityEventContracts.Json));
            }
        }
    }
}
