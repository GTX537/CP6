using System.Net;
using System.Text.Json;
using CP6.Entity.DomainModels;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData("missing")][InlineData("other-tenant")]
    public async Task SceneDelta_save_requires_current_tenant_floor(string source)
    {
        await using var h=await SceneDeltaSeed();await using var other=await SceneDeltaSeed();await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var actor=await Login(host,h);var floor=source=="missing"?Guid.NewGuid():await SceneDeltaFloor(other);
        var before=await SceneDeltaRows(h);var otherBefore=await SceneDeltaRows(other);var stock=await PalletStockRows(h);
        using var result=await host.Send(OperationsRequest(actor,HttpMethod.Post,SceneDeltaPath(floor),SceneDeltaNewZone(floor,"DENIED")));var after=await SceneDeltaRows(h);var otherAfter=await SceneDeltaRows(other);var stockAfter=await PalletStockRows(h);
        await DeviceEvidence("scene-delta-floor-"+source+".json",new{Status=(int)result.StatusCode,Before=before,After=after,OtherBefore=otherBefore,OtherAfter=otherAfter,StockBefore=stock,StockAfter=stockAfter});
        Assert.Equal(HttpStatusCode.NotFound,result.StatusCode);Assert.Equal(before,after);Assert.Equal(otherBefore,otherAfter);Assert.Equal(stock,stockAfter);
    }

    [CurrentPermissionPostgresFact]
    public async Task SceneDelta_actual_rehome_commits_geometry_projection_number_and_event()
    {
        await using var h=await SceneDeltaSeed();await using var other=await SceneDeltaSeed();await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var actor=await Login(host,h);var floor=await SceneDeltaFloor(h);var published=await SceneIngressLocation(h,"FROZEN");var input=await SceneDeltaRehome(h,floor);
        var oldVersion=await h.Db.Space_Locations.Where(x=>x.Id==published).Select(x=>x.Version).SingleAsync();var seq=await SceneDeltaSequence(h);var otherBefore=await SceneDeltaRows(other);var stock=await PalletStockRows(h);
        using var saved=await host.Send(OperationsRequest(actor,HttpMethod.Post,SceneDeltaPath(floor),input));var map=(await SpaceCodeHttpData<JsonElement>(saved)).GetProperty("idMap");Assert.Equal(JsonValueKind.Object,map.ValueKind);Assert.Empty(map.EnumerateObject());
        var receipt=await VerifySceneDeltaRehome(h,floor,published,oldVersion,seq);var otherAfter=await SceneDeltaRows(other);var stockAfter=await PalletStockRows(h);
        Assert.Equal(otherBefore,otherAfter);Assert.Equal(stock,stockAfter);
        await DeviceEvidence("scene-delta-normal.json",new{Status=(int)saved.StatusCode,Receipt=receipt,OtherBefore=otherBefore,OtherAfter=otherAfter,StockBefore=stock,StockAfter=stockAfter});
    }

    [CurrentPermissionPostgresFact]
    public async Task SceneDelta_deferred_commit_failure_is_explicit_and_real_effects_are_checked()
    {
        await using var h=await SceneDeltaSeed();var connection=h.Db.Database.GetConnectionString()!;await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);var floor=await SceneDeltaFloor(h);var published=await SceneIngressLocation(h,"FROZEN");var input=await SceneDeltaRehome(h,floor);
        var oldVersion=await h.Db.Space_Locations.Where(x=>x.Id==published).Select(x=>x.Version).SingleAsync();var seq=await SceneDeltaSequence(h);var before=await SceneDeltaRows(h);var stock=await PalletStockRows(h);
        await using var control=new NpgsqlConnection(connection);await control.OpenAsync();await using var install=new NpgsqlCommand($$"""
            CREATE FUNCTION public.cp6_test_scene_commit_failure() RETURNS trigger LANGUAGE plpgsql AS $fault$
            BEGIN RAISE EXCEPTION 'simulated late scene commit failure'; END $fault$;
            CREATE CONSTRAINT TRIGGER cp6_test_scene_commit_failure AFTER INSERT ON public."T_IntegrationEvent"
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW WHEN (NEW."TenantId"='{{h.Tenant}}' AND NEW."SourceModule"='SPACE')
            EXECUTE FUNCTION public.cp6_test_scene_commit_failure();
            """,control);await install.ExecuteNonQueryAsync();
        try
        {
            using var failed=await Send();var after=await SceneDeltaRows(h);var afterSeq=await SceneDeltaSequence(h);var stockAfter=await PalletStockRows(h);var unknown=(await failed.Content.ReadAsStringAsync()).Contains("SPACE_SCENE_OUTCOME_UNKNOWN",StringComparison.Ordinal);
            await DeviceEvidence("scene-delta-commit-fault.json",new{Status=(int)failed.StatusCode,Unknown=unknown,Before=before,After=after,SequenceBefore=seq,SequenceAfter=afterSeq,StockBefore=stock,StockAfter=stockAfter});
            Assert.Equal(before,after);Assert.Equal(seq,afterSeq);Assert.Equal(stock,stockAfter);Assert.Equal(HttpStatusCode.ServiceUnavailable,failed.StatusCode);Assert.True(unknown);
        }
        finally{await using var remove=new NpgsqlCommand("DROP TRIGGER cp6_test_scene_commit_failure ON public.\"T_IntegrationEvent\"; DROP FUNCTION public.cp6_test_scene_commit_failure()",control);await remove.ExecuteNonQueryAsync();}
        // The fixture explicitly observed every affected durable group before this new attempt.
        using var recovered=await Send();Assert.Equal(HttpStatusCode.OK,recovered.StatusCode);await VerifySceneDeltaRehome(h,floor,published,oldVersion,seq);
        Task<HttpResponseMessage> Send()=>host.Send(OperationsRequest(actor,HttpMethod.Post,SceneDeltaPath(floor),input));
    }

    private static async Task<object> VerifySceneDeltaRehome(Harness h,Guid floor,Guid published,long oldVersion,int seq)
    {
        var rack=await h.Db.Space_Racks.AsNoTracking().SingleAsync(x=>x.RackCode=="R01");var row=await h.Db.Space_Locations.AsNoTracking().SingleAsync(x=>x.Id==published);var bin=await h.Db.WmsBins.AsNoTracking().SingleAsync(x=>x.Id==published);
        var evt=await h.Db.IntegrationEvents.AsNoTracking().SingleAsync(x=>x.SourceModule=="SPACE"&&x.TargetModule=="WMS");var afterSeq=await SceneDeltaSequence(h);
        Assert.Equal(110,rack.X);Assert.Null(rack.AisleId);Assert.Equal(rack.Id,row.RackId);Assert.Equal(2610,row.AbsX);Assert.Equal("FROZEN",row.LocationCode);Assert.Equal(1,row.Status);Assert.Equal(oldVersion+1,row.Version);
        Assert.True(bin.IsActive);Assert.Equal("FROZEN",bin.LocationCode);Assert.Equal("SIM",bin.WarehouseCd);Assert.Equal(row.Version,bin.Version);
        Assert.Equal(IntegrationEventStatus.Success,evt.Status);Assert.Equal(h.Tenant,evt.TenantId);Assert.StartsWith("LPUB-",evt.SourceNo);Assert.Equal(seq+1,afterSeq);
        Assert.Single(await h.Db.Space_Zones.Where(x=>x.FloorId==floor&&x.ZoneCode=="REHOME").ToArrayAsync());
        return new{RackX=rack.X,AisleCleared=rack.AisleId==null,row.AbsX,row.LocationCode,row.Status,LocationVersion=row.Version,BinVersion=bin.Version,BinActive=bin.IsActive,BinWarehouse=bin.WarehouseCd,EventStatus=evt.Status,EventSource=evt.SourceNo,SequenceBefore=seq,SequenceAfter=afterSeq};
    }
}
