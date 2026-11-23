using System.Net;
using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using CP6.Tests.Platform;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private ConnectorVersionControlService ConnOwner(Harness h) => new(h.Db,new HttpContextAccessor{HttpContext=h.Http},
        ScopeFence(h),Authoring(h),_=>Task.FromResult(h.User),Options.Create(new OaConnectorControlOptionsV1{AllowSimulatedParticipants=true}),[],[]);

    [CurrentPermissionPostgresFact]
    public async Task OaConn_lost_commit_recovers_once_with_current_read_after_write_revoke_and_hides_other_scope()
    {
        var fault=new CommitFault();
        await using var h=await Seed(fault);
        await RegisterScope(h); await ConnPeople(h);
        var profile=await ConnRegistration(h,"CONNECTOR",ConnFixture());
        var selected=new ConnSelectionV1("original-connector","TENANT",profile);
        var owner=ConnOwner(h);
        var expected=(await owner.CreationAsync("CONNECTOR",selected,default)).GetProperty("expectedVersion").GetString()!;
        var command=ConnCmd(expected,new ConnDraftV1(selected,"原命令"),"lost-connector");
        var bytes=JsonSerializer.SerializeToUtf8Bytes(command,ConnectorVersionControlService.Json);
        fault.Armed=true;
        var lost=await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.WriteAsync("CONNECTOR",null,"CREATE",bytes,default));
        Assert.Equal("COMMAND_OUTCOME_UNKNOWN",lost.Message);
        await h.Db.Sys_RoleActions.Where(a=>a.RoleId==72 && a.ActionCode=="conn.connector.write").ExecuteDeleteAsync();
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,ConnSettings());
        var maker=new ConnApi(host,await Login(host,h));
        var otherUser=new ConnApi(host,await Login(host,"conn-reviewer","Reviewer1!Test",h.Tenant.ToString("N")));
        var exact=await maker.Data("commands/lost-connector");
        var repeats=await Task.WhenAll(maker.Data("connectors",command),maker.Data("connectors",command));
        Assert.All(repeats,r=>Assert.Equal(exact.GetRawText(),r.GetRawText()));
        Assert.Equal("NOT_OBSERVED",(await otherUser.Data("commands/lost-connector")).GetProperty("outcome").GetString());
        await maker.Reject("connectors",ConnCmd(expected,new ConnDraftV1(selected,"另一意图"),"lost-connector"),
            HttpStatusCode.Conflict,"COMMAND_PAYLOAD_CONFLICT");
        await using var foreign=await Seed();
        await EngineeringAction(foreign,"oa-flow-admin","conn.read");
        var menu=await foreign.Db.Sys_Menus.SingleAsync(m=>m.MenuKey=="oa-flow-admin");
        if(!await foreign.Db.Sys_RoleMenus.AnyAsync(r=>r.RoleId==72 && r.MenuId==menu.MenuId))
        {
            foreign.Db.Sys_RoleMenus.Add(new(){RoleId=72,MenuId=menu.MenuId});
            await foreign.Db.SaveChangesAsync();
        }
        var foreignUser=new ConnApi(host,await Login(host,foreign));
        Assert.Equal("NOT_OBSERVED",(await foreignUser.Data("commands/lost-connector")).GetProperty("outcome").GetString());
        await foreignUser.Reject("connectors/"+exact.GetProperty("rootId").GetGuid()+"/context",null,HttpStatusCode.NotFound,"RESOURCE_NOT_FOUND");
        await h.Db.Sys_RoleActions.Where(a=>a.RoleId==72 && a.ActionCode=="conn.read").ExecuteDeleteAsync();
        await maker.Reject("commands/lost-connector",null,HttpStatusCode.Forbidden,"ACTION_FORBIDDEN");
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM cp6_oa_conn_v1.audit WHERE tenant_id={h.Tenant} AND command_id='lost-connector'
            """).SingleAsync());
    }
}
