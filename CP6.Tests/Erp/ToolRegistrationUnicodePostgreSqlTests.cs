using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData("evidence")][InlineData("sizeNote")][InlineData("remarks")]
    public async Task Tool_registration_unicode_original_codepoints_survive_new_registration_and_replay(string field)
    {
        await using var h=await Seed();await GrantRegistration(h);var svc=Registrations(h);
        const string raw="  e\u0301 \U0001F6E0  ";
        var command=(await NewRegistered(h)) with{Evidence=raw,Profile=new("PLATE",SizeNote:raw,Remarks:raw)};
        var saved=await svc.CommandAsync(command,null);var registration=(await svc.ReadAsync(command.ObjectId))!;
        Assert.Equal("tool.registration.v2",saved.InputContract);Assert.Equal(saved.InputContract,registration.InputContract);
        using var original=JsonDocument.Parse(registration.OriginalJson);var native=await ToolInstances(h).ReadAsync(command.ObjectId);
        Assert.Equal(raw,field=="evidence"?original.RootElement.GetProperty("Evidence").GetString():native.Source.GetProperty(field).GetString());
        Assert.Equal(saved,await svc.CommandAsync(command,null));
        var equivalent=raw.Normalize();
        var changed=field=="evidence"?command with{Evidence=equivalent}:command with{Profile=field=="sizeNote"?command.Profile! with{SizeNote=equivalent}:command.Profile! with{Remarks=equivalent}};
        var failure=await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.CommandAsync(changed,null));Assert.Equal(409,failure.Status);
        Assert.Equal(1,await h.Db.PlateMoldStocks.CountAsync());Assert.Equal(saved,(await svc.OperationAsync(command.OperationId,false)).Result);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_registration_unicode_same_key_different_codepoints_is_a_conflict()
    {
        await using var h=await Seed();await GrantRegistration(h);var svc=Registrations(h);
        var command=(await NewRegistered(h)) with{Evidence="\u00e9"};var saved=await svc.CommandAsync(command,null);
        Assert.Equal(409,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.CommandAsync(command with{Evidence="e\u0301"},null))).Status);
        Assert.Equal(saved,(await svc.OperationAsync(command.OperationId,false)).Result);Assert.Equal(1,await h.Db.PlateMoldStocks.CountAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_registration_unicode_legacy_initialization_preserves_raw_evidence_and_native_history()
    {
        await using var h=await Seed();await GrantRegistration(h);var svc=Registrations(h);var(view,initial)=await LegacyRegistration(h);
        var command=initial with{Evidence="e\u0301"};var saved=await svc.CommandAsync(command,view.Etag);var registration=(await svc.ReadAsync(command.ObjectId))!;
        using var original=JsonDocument.Parse(registration.OriginalJson);Assert.Equal(command.Evidence,original.RootElement.GetProperty("Evidence").GetString());
        Assert.Equal("500",registration.CurrentCycleShots);Assert.Null(registration.LifetimeShots);Assert.Equal(view.Reference,(await ToolInstances(h).ReadAsync(command.ObjectId)).Reference);
        Assert.Equal(saved,await svc.CommandAsync(command,view.Etag));
        Assert.Equal(409,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.CommandAsync(command with{Evidence="\u00e9"},view.Etag))).Status);
    }
    [TenantManagementPostgresTheory]
    [InlineData(true)][InlineData(false)]
    public async Task Tool_registration_unicode_historical_v1_replay_and_close_preserve_original_bytes_and_codec(bool create)
    {
        await using var h=await Seed();await GrantRegistration(h);var svc=Registrations(h);
        ToolRegistrationCommandV1 command;ToolInstanceViewV1 native;string? etag=null;
        if(create)
        {
            command=(await NewRegistered(h)) with{Evidence="\u00e9",Profile=new("PLATE",SizeNote:"\u00e9",Remarks:"\u00e9",MadeCost:"1.20")};
            h.Db.PlateMoldStocks.Add(new PlateMoldStock{Id=command.ObjectId,PlateNo="PLT2-COMPAT",PlateType="PLATE",UsedShots=0,MadeCost=1.20m,SizeNote="\u00e9",Remarks="\u00e9",Creator="price-owner",CreateDate=new(2026,1,1)});
            await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();native=await ToolInstances(h).ReadAsync(command.ObjectId);
        }
        else { var legacy=await LegacyRegistration(h);native=legacy.View;etag=native.Etag;command=legacy.Command with{Evidence="\u00e9"}; }
        // Historical fixtures use the exact pre-fix v1 JSON shape, with no newly added contract fields.
        var value=new{TenantId=h.Tenant,ObjectId=command.ObjectId,ActorId=h.User,Origin=create?"NEW_REGISTERED":"LEGACY_OBSERVED",CycleAnchorId=Guid.NewGuid(),
            CycleNo=create?"1":null,InitialCycleShots=create?"0":"500",LifetimeKnowledge=create?"KNOWN_FROM_REGISTRATION":"UNKNOWN",LifetimeShots=create?"0":null,
            NativeAtRegistration=native.Reference,Site=command.Site,Evidence=command.Evidence};
        var originalJson=JsonSerializer.Serialize(value);var digest=MainOwnerOperationJournalV1.Digest(System.Text.Encoding.UTF8.GetBytes(originalJson));
        var result=new{command.OperationId,Kind=create?"TOOL_CREATE":"TOOL_INITIALIZE",command.ObjectId,Version="1",Digest=digest};
        var bytes=MainOwnerOperationJournalV1.Encode(new{contract="tool.registration.v1",etag,command});var closed=Guid.NewGuid();var revision=long.Parse(native.Reference.Version);
        await RegistrationAuthority(h).ExecuteAsync("wms-plate-mold","query",true,async(a,t)=>
        {
            await h.Db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_tool_instance_v1.registrations(tenant_id,object_id,native_revision,original_json,original_digest) VALUES({h.Tenant},{command.ObjectId},{revision},{originalJson},{digest})",t);
            var journal=new MainOwnerOperationJournalV1(h.Db);
            await journal.Store(ToolRegistrationOwnerV1.RegistrationOwner,"tool.registration.v1",command.OperationId,a.ActorId,bytes,result,[command.ObjectId],new{fixture="historical-v1"},t);
            await journal.Store(ToolRegistrationOwnerV1.RegistrationOwner,"tool.registration.v1",closed,a.ActorId,null,new ToolRegistrationOperationV1(closed,"CLOSED"),[],new{fixture="historical-v1-closed"},t);return true;
        });
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==443&&(x.ActionCode=="add"||x.ActionCode=="initialize")).ExecuteDeleteAsync();
        var replay=command with{Evidence="e\u0301",Profile=command.Profile is {} p?p with{SizeNote="e\u0301",Remarks="e\u0301",MadeCost="1.2"}:null};
        var saved=await svc.CommandAsync(replay,etag);Assert.Equal("tool.registration.v1",saved.InputContract);Assert.Equal(result.Digest,saved.Digest);
        Assert.Equal(saved,(await svc.OperationAsync(command.OperationId,false)).Result);
        var read=(await svc.ReadAsync(command.ObjectId))!;Assert.Equal("tool.registration.v1",read.InputContract);Assert.Equal(originalJson,read.OriginalJson);
        Assert.Equal(409,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.CommandAsync(command with{Evidence="different"},etag))).Status);
        Assert.Equal("CLOSED",(await svc.OperationAsync(closed,true)).State);
        Assert.Equal(409,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.CommandAsync(command with{OperationId=closed},etag))).Status);
        var stored=await RegistrationAuthority(h).ExecuteAsync("wms-plate-mold","query",false,(a,t)=>new MainOwnerOperationJournalV1(h.Db).Read(ToolRegistrationOwnerV1.RegistrationOwner,command.OperationId,t));
        Assert.Equal(bytes,stored!.Input);Assert.Equal(MainOwnerOperationJournalV1.Encode(result),stored.Result);Assert.Equal("tool.registration.v1",stored.Contract);
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==443&&x.ActionCode=="query").ExecuteDeleteAsync();
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.OperationAsync(command.OperationId,false))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_registration_unicode_rejects_malformed_sequences_before_json_freeze_without_side_effects()
    {
        await using var h=await Seed();await GrantRegistration(h);var svc=Registrations(h);var command=await NewRegistered(h);
        foreach(var bad in new[]{command with{Evidence="\ud800"},command with{Profile=command.Profile! with{Remarks="\udc00"}},command with{Profile=command.Profile! with{SizeNote="\ud800"}}})
            Assert.Equal(400,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>svc.CommandAsync(bad,null))).Status);
        Assert.Empty(await h.Db.PlateMoldStocks.ToArrayAsync());Assert.Empty(await h.Db.WmsSequences.ToArrayAsync());Assert.Equal("UNKNOWN",(await svc.OperationAsync(command.OperationId,false)).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_registration_unicode_HTTP_preserves_raw_text_and_conflicts_without_duplicate_effects()
    {
        await using var h=await Seed();await GrantRegistration(h);var command=(await NewRegistered(h)) with{Evidence="e\u0301",Profile=new("PLATE",SizeNote:"e\u0301",Remarks:"e\u0301")};
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var cookies=await Login(host,h);
        using var context=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"/api/wms/specials/v1/tools/creation-context"));
        using var contextJson=JsonDocument.Parse(await context.Content.ReadAsStringAsync());command=command with{CreationAuthorityVersion=contextJson.RootElement.GetProperty("data").GetProperty("authorityVersion").GetString()};
        async Task<HttpResponseMessage> Post(ToolRegistrationCommandV1 body){var request=OperationsRequest(cookies,HttpMethod.Post,"/api/wms/specials/v1/tools");request.Content=JsonContent.Create(body);return await host.Send(request);}
        using var created=await Post(command);Assert.Equal(HttpStatusCode.OK,created.StatusCode);
        using var result=JsonDocument.Parse(await created.Content.ReadAsStringAsync());Assert.Equal("tool.registration.v2",result.RootElement.GetProperty("data").GetProperty("inputContract").GetString());
        using var replay=await Post(command);Assert.Equal(HttpStatusCode.OK,replay.StatusCode);
        using var conflict=await Post(command with{Evidence="\u00e9"});Assert.Equal(HttpStatusCode.Conflict,conflict.StatusCode);
        using var response=await host.Send(OperationsRequest(cookies,HttpMethod.Get,$"/api/wms/specials/v1/tools/{command.ObjectId}/registration"));Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        using var read=JsonDocument.Parse(await response.Content.ReadAsStringAsync());var data=read.RootElement.GetProperty("data");Assert.Equal("tool.registration.v2",data.GetProperty("inputContract").GetString());
        using var original=JsonDocument.Parse(data.GetProperty("originalJson").GetString()!);Assert.Equal(command.Evidence,original.RootElement.GetProperty("Evidence").GetString());
        var native=await ToolInstances(h).ReadAsync(command.ObjectId);Assert.Equal(command.Profile.SizeNote,native.Source.GetProperty("sizeNote").GetString());Assert.Equal(command.Profile.Remarks,native.Source.GetProperty("remarks").GetString());
        Assert.Equal(1,await h.Db.PlateMoldStocks.CountAsync());Assert.Equal(1,await h.Db.WmsSequences.Select(x=>x.NextNo).SingleAsync());
    }
}
