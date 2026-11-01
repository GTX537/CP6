using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Wms.Replenish;
using CP6.Core.Services.Wms.StockKernel;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using K=CP6.Core.Services.Wms.StockKernel.StockDirectMoveValuesV1;
namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static StockRefV1 StockRef(RepRefV1 r)=>new(r.Owner,r.Id,r.Version,r.DigestDomain,r.CodecVersion,r.Digest);
    private static StockRefV1 StockRef(string owner,string id)=>StockRef(RepReference(owner,id));
    private static StockOpeningV1 StockOpening(RepObservationV1 o,string limit="100",bool claim=true)
    {
        var metric=StockRef(o.Targets[0].CapacityDomains[0].MetricRef);var unit=StockRef(o.Rules[0].BaseUomRef);
        var domains=o.Locations.Select((l,i)=>i==2?StockRef(o.Targets[0].CapacityDomains[0].DomainRef):StockRef("MASTER","capacity-"+i)).ToArray();
        var locations=o.Locations.Select((l,i)=>new StockLocationV1(StockRef(l.Ref),l.NativeLocationId,"W1",i==0?"SRC-A":i==1?"SRC-B":"PIK-BLOCK-CHECK",["SIM-P"],[domains[i]])).ToArray();
        var roots=o.Roots.Select(r=>new StockOpeningRootV1(StockRef(r.Span.RootRef),StockRef("SIMULATED-SOURCE","opening-"+r.Span.RootRef.Id),
            new(StockRef(r.ProductRef),StockRef(r.TechnicalRef),StockRef(r.OwnerRef),StockRef(r.BaseUomRef),StockRef("MASTER","sale-use"),StockRef(o.Scope.SiteRef)),
            r.Span.To,false,[new("0",r.Span.To,StockRef(r.LocationRef))],[new(metric,unit,"1","1")],StockRef(r.MasterConditionRef),true)).ToArray();
        return new(StockRef("STOCK","opening-"+Guid.NewGuid()),"SIMULATED",locations,roots,
            claim?[new(StockRef("STOCK","sale-claim"),StockRef("STOCK","binding"),new(roots[0].Ref,"0","2"),"ALLOCATED",StockRef("RSV","move-use"),true)]:[],
            domains.Select((d,i)=>new StockCapacityV1(d,metric,unit,i==2?"LIMITED":"UNLIMITED",i==2?limit:null,"0")).ToArray(),
            [new(StockRef(o.Rules[0].ProductRef),"SIM-P")]);
    }
    private static StockMoveInputV1 StockMove(StockOpeningV1 o,string from="0",string to="2")
        =>new(Guid.NewGuid().ToString("D"),Guid.NewGuid(),StockRef("WMS_TRANSFER","demand"),StockRef("WMS_TRANSFER","stage"),o.Locations[0].Ref,o.Locations[2].Ref,[new(o.Roots[0].Ref,from,to)]);
    private static Task<T> StockTransaction<T>(RepFixture f,Harness h,Func<StockDirectMoveV1,CancellationToken,Task<T>> body)
        =>f.Authority.ExecuteAsync(OperationsOwnerV1.Replenish,"prepare",true,(a,t)=>body(new(h.Db,ScopeFence(h).EnvironmentId),t),default);
    private static async Task OpenStock(RepFixture f,Harness h,StockOpeningV1 opening)
        =>await StockTransaction(f,h,async(k,t)=>{await k.OpenSimulation(opening,t);return true;});

    [CurrentPermissionPostgresFact]
    public async Task StockMove_actual_root_partition_two_legs_claim_preservation_capacity_and_original_replay()
    {
        await using var h=await Seed();var f=await RepSetup(h);await DraftActions(h);var opening=StockOpening(f.Observation);await OpenStock(f,h,opening);var input=StockMove(opening);
        var receipt=await StockTransaction(f,h,(k,t)=>k.Move(input,t));Assert.Equal("POSTED",receipt.State);Assert.Equal(2,receipt.Movements.Length);
        Assert.Equal(new[]{"OUT","IN"},receipt.Movements.Select(x=>x.Direction));Assert.All(receipt.Movements,x=>Assert.Equal("2.00000000",x.Quantity));
        var current=await StockTransaction(f,h,(k,t)=>k.Root(opening.Roots[0].Ref,t));Assert.Equal(2,current.Revision);
        Assert.Equal(new[]{"2.00000000","1.00000000"},current.Slices.Select(x=>K.Text(K.Number(x.To)-K.Number(x.From))));
        Assert.Equal(opening.Locations[2].Ref,current.Slices[0].LocationRef);Assert.Equal(opening.Locations[0].Ref,current.Slices[1].LocationRef);
        Assert.Equal(opening.Claims[0],Assert.Single(receipt.Claims));Assert.Equal(opening.Claims[0],Assert.Single(await StockTransaction(f,h,(k,t)=>k.Claims(input.Spans[0],t))));
        Assert.Equal(K.Hash(receipt),K.Hash(await StockTransaction(f,h,(k,t)=>k.Move(input,t))));
        Assert.Equal("2.00000000",await h.Db.Database.SqlQuery<string>($"SELECT occupied::text AS \"Value\" FROM cp6_stock_move_v1.capacities WHERE tenant_id={h.Tenant} AND id={K.Key(opening.Capacities[2].DomainRef)}").SingleAsync());
        Assert.Equal("STOCK_SOURCE_POSITION_CHANGED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>StockTransaction(f,h,(k,t)=>k.Move(input with{CanonicalKey=Guid.NewGuid().ToString(),OperationId=Guid.NewGuid()},t)))).Message);
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_stock_move_v1.decisions WHERE tenant_id={h.Tenant}").SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task StockMove_source_target_write_failure_rolls_back_root_capacity_receipt_and_parent()
    {
        var hook=new StartWriteHook();await using var h=await Seed(hook);var f=await RepSetup(h);await DraftActions(h);var opening=StockOpening(f.Observation);await OpenStock(f,h,opening);var input=StockMove(opening);var hit=false;
        hook.Before=c=>{if(c.CommandText.Contains("INSERT INTO cp6_stock_move_v1.decisions",StringComparison.Ordinal)){hit=true;throw new InvalidOperationException("simulated-final-receipt-write-fault");}return Task.CompletedTask;};
        await Assert.ThrowsAsync<InvalidOperationException>(()=>StockTransaction(f,h,(k,t)=>k.Move(input,t)));hook.Before=null;Assert.True(hit);
        var root=await StockTransaction(f,h,(k,t)=>k.Root(opening.Roots[0].Ref,t));Assert.Equal(1,root.Revision);Assert.Equal(opening.Locations[0].Ref,Assert.Single(root.Slices).LocationRef);
        Assert.Null(await StockTransaction(f,h,(k,t)=>k.Original(input,t)));Assert.Equal("POSTED",(await StockTransaction(f,h,(k,t)=>k.Move(input,t))).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task StockMove_stop_wins_or_returns_posted_and_never_releases_claim()
    {
        await using var h=await Seed();var f=await RepSetup(h);await DraftActions(h);var opening=StockOpening(f.Observation);await OpenStock(f,h,opening);var input=StockMove(opening);
        var sealedResult=await StockTransaction(f,h,(k,t)=>k.Seal(input,t));Assert.Equal("SEALED_NO_EFFECT",sealedResult.State);Assert.Empty(sealedResult.Movements);
        Assert.Equal(K.Hash(sealedResult),K.Hash(await StockTransaction(f,h,(k,t)=>k.Move(input,t))));
        Assert.Equal(opening.Claims[0],Assert.Single(await StockTransaction(f,h,(k,t)=>k.Claims(input.Spans[0],t))));
        var second=StockMove(opening);var posted=await StockTransaction(f,h,(k,t)=>k.Move(second,t));Assert.Equal(K.Hash(posted),K.Hash(await StockTransaction(f,h,(k,t)=>k.Seal(second,t))));
    }
    [CurrentPermissionPostgresFact]
    public async Task StockMove_current_capacity_claim_and_two_end_location_conditions_fail_before_effect()
    {
        await using var h=await Seed();var f=await RepSetup(h);await DraftActions(h);var opening=StockOpening(f.Observation,"1");await OpenStock(f,h,opening);var input=StockMove(opening);
        Assert.Equal("STOCK_CAPACITY_INSUFFICIENT",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>StockTransaction(f,h,(k,t)=>k.Move(input,t)))).Message);
        var blocked=opening.Claims[0] with{MoveAllowed=false};await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_stock_move_v1.claim_bindings SET body={K.Bytes(blocked)} WHERE tenant_id={h.Tenant}");
        Assert.Equal("STOCK_CLAIM_USE_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>StockTransaction(f,h,(k,t)=>k.Move(input with{Spans=[input.Spans[0] with{To="1"}]},t)))).Message);
        await h.Db.Locations.Where(x=>x.Id==opening.Locations[2].NativeLocationId).ExecuteUpdateAsync(x=>x.SetProperty(l=>l.IsBlocked,true));
        Assert.Equal("STOCK_LOCATION_USE_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>StockTransaction(f,h,(k,t)=>k.Move(input,t)))).Message);
        Assert.Null(await StockTransaction(f,h,(k,t)=>k.Original(input,t)));
    }
    [CurrentPermissionPostgresFact]
    public async Task StockMove_legacy_raw_writer_is_fenced_and_tenant_environment_do_not_alias()
    {
        await using var h=await Seed();var f=await RepSetup(h);await DraftActions(h);var opening=StockOpening(f.Observation);await OpenStock(f,h,opening);
        var failure=await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public."T_Stock"("Id","TenantId","WarehouseCd","LocationCd","ProductCd","LotNo","PhysicalQty","AllocatedQty","AvailableQty","OwnerType","QcStatus","IsDeleted")
            VALUES({Guid.NewGuid()},{h.Tenant},'W1','SRC-A','SIM-P','',1,0,1,'SELF','PASSED',false)
            """));Assert.Contains("STOCK_ROOT_WRITER_REQUIRED",failure.Message);
        await using var other=await Seed();var of=await RepSetup(other);await DraftActions(other);
        Assert.Equal("STOCK_ROOT_UNAVAILABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>StockTransaction(of,other,(k,t)=>k.Root(opening.Roots[0].Ref,t)))).Message);
        await using var transaction=await h.Db.Database.BeginTransactionAsync();
        Assert.Equal("STOCK_ROOT_UNAVAILABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>new StockDirectMoveV1(h.Db,Guid.NewGuid()).Root(opening.Roots[0].Ref,default))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task StockMove_maximum_root_coordinates_preserve_one_eighth_decimal_unit()
    {
        await using var h=await Seed();var f=await RepSetup(h);await DraftActions(h);var opening=StockOpening(f.Observation,claim:false);
        var root=opening.Roots[0] with{Quantity="9999999999999.99999999",Slices=[new("0","9999999999999.99999999",opening.Locations[0].Ref)]};
        opening=opening with{Roots=[root,opening.Roots[1]]};await OpenStock(f,h,opening);
        var input=StockMove(opening,"9999999999999.99999998","9999999999999.99999999");var receipt=await StockTransaction(f,h,(k,t)=>k.Move(input,t));
        Assert.All(receipt.Movements,x=>Assert.Equal("0.00000001",x.Quantity));
        var current=await StockTransaction(f,h,(k,t)=>k.Root(root.Ref,t));Assert.Equal(root.Quantity,K.Text(current.Slices.Aggregate(System.Numerics.BigInteger.Zero,(n,s)=>n+K.Number(s.To)-K.Number(s.From))));
    }
    [CurrentPermissionPostgresFact]
    public async Task StockMove_concurrent_stop_and_execution_share_one_permanent_decision()
    {
        await using var h=await Seed();var f=await RepSetup(h);await DraftActions(h);var opening=StockOpening(f.Observation);await OpenStock(f,h,opening);var input=StockMove(opening);
        await using var peerDb=server.Context(new CP6.Core.Services.Common.TenantContext{CurrentTenantId=h.Tenant});var peer=h with{Db=peerDb};var pf=f with{Authority=RepAuthority(peer)};
        var results=await Task.WhenAll(StockTransaction(f,h,(k,t)=>k.Move(input,t)),StockTransaction(pf,peer,(k,t)=>k.Seal(input,t)));
        Assert.Equal(K.Hash(results[0]),K.Hash(results[1]));
        var root=await StockTransaction(f,h,(k,t)=>k.Root(opening.Roots[0].Ref,t));Assert.Equal(results[0].State=="POSTED"?2:1,root.Revision);
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_stock_move_v1.decisions WHERE tenant_id={h.Tenant}").SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task StockMove_different_roots_compete_for_same_final_capacity_domain()
    {
        await using var h=await Seed();var f=await RepSetup(h);await DraftActions(h);var opening=StockOpening(f.Observation,"2");await OpenStock(f,h,opening);var first=StockMove(opening);
        var second=StockMove(opening) with{SourceLocationRef=opening.Locations[1].Ref,Spans=[new(opening.Roots[1].Ref,"0","2")]};
        await using var peerDb=server.Context(new CP6.Core.Services.Common.TenantContext{CurrentTenantId=h.Tenant});var peer=h with{Db=peerDb};var pf=f with{Authority=RepAuthority(peer)};
        async Task<bool> Move(RepFixture fixture,Harness actor,StockMoveInputV1 command)
        {try{await StockTransaction(fixture,actor,(k,t)=>k.Move(command,t));return true;}catch(OrderPriceCorrectionException e)when(e.Message=="STOCK_CAPACITY_INSUFFICIENT"){return false;}}
        Assert.Single(await Task.WhenAll(Move(f,h,first),Move(pf,peer,second)),x=>x);
        Assert.Equal("2.00000000",await h.Db.Database.SqlQuery<string>($"SELECT occupied::text AS \"Value\" FROM cp6_stock_move_v1.capacities WHERE tenant_id={h.Tenant} AND id={K.Key(opening.Capacities[2].DomainRef)}").SingleAsync());
    }
}
