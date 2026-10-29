using System.Net;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static LocationControlOwnerV1 Controls(Harness h,CP6Context? db=null)=>new(db??h.Db,
        new(db??h.Db,new OperationsHttp{HttpContext=h.Http},_=>Task.FromResult(h.User),MasterCatalog().Capture));
    private static async Task<Guid> ControlSetup(Harness h)
    {await GrantMaster(h);await EngineeringAction(h,"wms-location","block");await EngineeringAction(h,"wms-location","unblock");return (await SeedMaster(h)).location;}
    private static LocationBlockCommandV1 Block(string reason="现场通道维修")=>new(Guid.NewGuid(),reason,"LOCAL_OPERATIONAL");
    private static async Task<LocationControlResultV1> Unblock(LocationControlOwnerV1 o,Guid location,Guid block)
    {
        var v=await o.ReadAsync(location);var d=await o.DecideReleaseAsync(location,v.Etag,new(Guid.NewGuid(),block,"现场通道已恢复"));
        return await o.ReleaseAsync(location,d.Etag,new(Guid.NewGuid(),block,"解除本项限制",d.Reference));
    }
    [CurrentPermissionPostgresFact]
    public async Task LocationControl_actual_native_multi_block_release_and_immutable_replay_are_atomic()
    {
        await using var h=await Seed();var id=await ControlSetup(h);var o=Controls(h);var before=await o.ReadAsync(id);var c=Block();
        var one=await o.BlockAsync(id,before.Etag,c);Assert.True(one.IsBlocked);Assert.Equal("2",one.ControlRevision);
        var two=await o.BlockAsync(id,one.Etag,Block("设备检修"));Assert.NotEqual(one.Etag,two.Etag);
        var first=await Unblock(o,id,one.BlockId);Assert.True(first.IsBlocked);Assert.Equal(1,(await o.ReadAsync(id)).OpenBlocks);
        var last=await Unblock(o,id,two.BlockId);Assert.False(last.IsBlocked);Assert.Equal("5",last.ControlRevision);
        var after=await o.ReadAsync(id);Assert.False(after.AdmissionGrant);Assert.Equal(0,after.OpenBlocks);Assert.Equal("NATIVE_LOCAL_CONTROL_ONLY",after.Qualification);
        Assert.Equal(2,after.Entries.Count(e=>e.State=="RELEASED"));
        await RevokeTransferAction(h,"wms-location","block");Assert.Equal(one,await o.BlockAsync(id,before.Etag,c));
        Assert.Equal(one,(await o.OperationAsync(id,c.OperationId)).Result);
        Assert.Equal(409,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.BlockAsync(id,before.Etag,c with{Reason="changed"}))).Status);
        Assert.Equal(9999999999999.99999999m,(await h.Db.Locations.SingleAsync(x=>x.Id==id)).ReservedCapacityQty);h.Db.ChangeTracker.Clear();
        await RevokeTransferAction(h,"wms-location","query");Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.OperationAsync(id,c.OperationId))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task LocationControl_keeps_prior_foreign_same_value_and_initialization_restrictions()
    {
        await using var h=await Seed();var id=await ControlSetup(h);var o=Controls(h);var v=await o.ReadAsync(id);
        var b=await o.BlockAsync(id,v.Etag,Block());
        await h.Db.Locations.Where(x=>x.Id==id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.IsBlocked,true));
        var r=await Unblock(o,id,b.BlockId);Assert.True(r.IsBlocked);Assert.True((await o.ReadAsync(id)).OtherRestrictionRetained);
        await using var prior=await Seed();var p=await ControlSetup(prior);await prior.Db.Locations.Where(x=>x.Id==p).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.IsBlocked,true));
        var po=Controls(prior);var pb=await po.BlockAsync(p,(await po.ReadAsync(p)).Etag,Block());Assert.True((await Unblock(po,p,pb.BlockId)).IsBlocked);
        await using var q=await Seed();var f=await LocationDraftSetup(q);await EngineeringAction(q,"wms-location","block");await EngineeringAction(q,"wms-location","unblock");
        var draft=await LocationDrafts(q).CreateAsync(f.Warehouse,f.Command with{AuthorityVersion=(await LocationDrafts(q).ContextAsync(f.Warehouse)).Creation.AuthorityVersion});var qo=Controls(q);var qb=await qo.BlockAsync(draft.LocationId,(await qo.ReadAsync(draft.LocationId)).Etag,Block());
        Assert.True((await Unblock(qo,draft.LocationId,qb.BlockId)).IsBlocked);
        var current=await LocationDrafts(q).ReadAsync(f.Warehouse,draft.LocationId);Assert.Equal("3",current.ControlRevision);Assert.Equal("QUARANTINED",current.InitializationState);
        Assert.False(await q.Db.Locations.AnyAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task LocationControl_release_requires_exact_current_decision_block_and_independent_permission()
    {
        await using var h=await Seed();var id=await ControlSetup(h);var o=Controls(h);var v=await o.ReadAsync(id);var b=await o.BlockAsync(id,v.Etag,Block());
        var d=await o.DecideReleaseAsync(id,b.Etag,new(Guid.NewGuid(),b.BlockId,"复核通过"));
        Assert.Equal(412,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.ReleaseAsync(id,d.Etag,new(Guid.NewGuid(),b.BlockId,"解除",d.Reference with{Digest=new string('0',64)})))).Status);
        var b2=await o.BlockAsync(id,d.Etag,Block("新增限制"));
        Assert.Equal(412,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.ReleaseAsync(id,b2.Etag,new(Guid.NewGuid(),b.BlockId,"解除",d.Reference)))).Status);
        Assert.Equal(412,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.BlockAsync(id,v.Etag,Block()))).Status);
        await RevokeTransferAction(h,"wms-location","unblock");
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.DecideReleaseAsync(id,b2.Etag,new(Guid.NewGuid(),b.BlockId,"解除")))).Status);
        await h.Db.WmsRoleScopes.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.AreaCd,"B"));Assert.Equal(404,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.ReadAsync(id))).Status);
        await using var other=await Seed();await ControlSetup(other);Assert.Equal(404,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>Controls(other).ReadAsync(id))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task LocationControl_database_protects_originals_mirror_and_unknown_operation_fences()
    {
        await using var h=await Seed();var id=await ControlSetup(h);var o=Controls(h);var v=await o.ReadAsync(id);var c=Block();
        Assert.Equal("UNKNOWN",(await o.OperationAsync(id,c.OperationId)).State);Assert.Equal("CLOSED",(await o.OperationAsync(id,c.OperationId,true)).State);
        Assert.Equal(409,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.BlockAsync(id,v.Etag,c))).Status);
        var b=await o.BlockAsync(id,v.Etag,Block());
        await Assert.ThrowsAsync<PostgresException>(()=>h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"T_Location\" SET \"IsBlocked\"=false WHERE \"Id\"={id}"));
        await Assert.ThrowsAsync<PostgresException>(()=>h.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cp6_location_control_v1.events WHERE id={b.Reference.Id}"));
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_location_control_v1.events DISABLE TRIGGER control_event_apply");
        try{Assert.Equal("LOCATION_CONTROL_STORE_UNAVAILABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.BlockAsync(id,b.Etag,Block()))).Message);}
        finally{await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_location_control_v1.events ENABLE TRIGGER control_event_apply");}
        Assert.Equal(1,(await o.ReadAsync(id)).OpenBlocks);
    }
    [CurrentPermissionPostgresFact]
    public async Task LocationControl_final_revoke_rolls_back_event_mirror_control_and_original()
    {
        var hook=new StartWriteHook();await using var h=await Seed(hook);var id=await ControlSetup(h);var o=Controls(h);var v=await o.ReadAsync(id);var c=Block();var hit=false;
        hook.Before=async command=>
        {
            if(!command.CommandText.Contains("INSERT INTO cp6_main_owner_v1.operations"))return;hook.Before=null;hit=true;
            await using var sql=new NpgsqlCommand("DELETE FROM public.\"Sys_RoleAction\" WHERE \"TenantId\"=@tenant AND \"RoleId\"=72 AND \"MenuId\"=402 AND \"ActionCode\"='block'",(NpgsqlConnection)command.Connection!,(NpgsqlTransaction)command.Transaction!);
            sql.Parameters.AddWithValue("tenant",h.Tenant);await sql.ExecuteNonQueryAsync();
        };
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.BlockAsync(id,v.Etag,c))).Status);Assert.True(hit);
        var after=await o.ReadAsync(id);Assert.Equal(v.Etag,after.Etag);Assert.False(after.IsBlocked);Assert.Empty(after.Entries);Assert.Equal("UNKNOWN",(await o.OperationAsync(id,c.OperationId)).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task LocationControl_competing_current_versions_cannot_both_commit()
    {
        await using var h=await Seed();var id=await ControlSetup(h);var etag=(await Controls(h).ReadAsync(id)).Etag;
        async Task<int> Write()
        {await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant});try{await Controls(h,db).BlockAsync(id,etag,Block());return 200;}catch(OrderPriceCorrectionException e){return e.Status;}}
        var results=await Task.WhenAll(Write(),Write());Assert.Equal(new[]{200,412},results.Order().ToArray());Assert.Equal(1,(await Controls(h).ReadAsync(id)).OpenBlocks);
    }
    [CurrentPermissionPostgresFact]
    public async Task LocationControl_database_requires_original_for_control_head_and_event()
    {
        await using var h=await Seed();var id=await ControlSetup(h);
        await Assert.ThrowsAsync<PostgresException>(()=>h.Db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_location_control_v1.controls(tenant_id,location_id,revision,other_blocked) VALUES({h.Tenant},{id},1,false)"));
        await using var tx=await h.Db.Database.BeginTransactionAsync();
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_location_control_v1.controls(tenant_id,location_id,revision,other_blocked) VALUES({h.Tenant},{id},1,false)");
        var key=Guid.NewGuid();var o=new LocationControlOriginalV1(h.Tenant,id,key,key,"BLOCK",Guid.NewGuid(),h.User,DateTimeOffset.UtcNow,"missing original","1","ignored",null);
        var bytes=CP6.Core.Services.Plm.EngineeringProductStoreV1.Encode(o);var digest=CP6.Core.Services.Plm.EngineeringProductStoreV1.Digest(bytes);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_location_control_v1.events(tenant_id,location_id,id,block_id,kind,revision_before,operation_id,actor_id,body,digest) VALUES({h.Tenant},{id},{key},{key},'BLOCK',1,{o.OperationId},{h.User},{bytes},{digest})");
        await Assert.ThrowsAsync<PostgresException>(()=>tx.CommitAsync());await tx.DisposeAsync();
        Assert.False(await h.Db.Locations.AsNoTracking().Where(x=>x.Id==id).Select(x=>x.IsBlocked).SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task LocationControl_real_program_HTTP_closed_DTO_etag_and_independent_actions()
    {
        await using var h=await Seed();var id=await ControlSetup(h);await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var cookies=await Login(host,h);var root=$"api/wms/locations/{id}/local-controls-v1";
        using var read=await host.Send(OperationsRequest(cookies,HttpMethod.Get,root));Assert.Equal(HttpStatusCode.OK,read.StatusCode);Assert.True(read.Headers.CacheControl?.NoStore);
        var c=Block();var body=JsonSerializer.SerializeToNode(c,new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();body["isBlocked"]=false;
        using var invalid=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+"/blocks",body));Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);
        using var noEtag=await host.Send(OperationsRequest(cookies,HttpMethod.Post,root+"/blocks",c));Assert.Equal((HttpStatusCode)428,noEtag.StatusCode);
        var request=OperationsRequest(cookies,HttpMethod.Post,root+"/blocks",c);request.Headers.TryAddWithoutValidation("If-Match",read.Headers.ETag!.Tag);
        using var created=await host.Send(request);Assert.Equal(HttpStatusCode.OK,created.StatusCode);Assert.True(created.Headers.CacheControl?.NoStore);
        await RevokeTransferAction(h,"wms-location","unblock");var b=(await Controls(h).ReadAsync(id)).Entries.Single();
        var release=OperationsRequest(cookies,HttpMethod.Post,root+"/release-decisions",new LocationReleaseDecisionCommandV1(Guid.NewGuid(),b.Original.BlockId,"解除"));release.Headers.TryAddWithoutValidation("If-Match",(await Controls(h).ReadAsync(id)).Etag);
        using var forbidden=await host.Send(release);Assert.Equal(HttpStatusCode.Forbidden,forbidden.StatusCode);
    }
}
