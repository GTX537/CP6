using System.Globalization;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Plm;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
namespace CP6.Core.Services.Wms;

public sealed class LocationControlOwnerV1(CP6Context db,OperationsOwnerV1 owner)
{
    internal const string Owner="Main.Wms.LocationOperationalControl.v1",Contract="LOCATION-LOCAL-CONTROL/1";
    private OperationsOwnerV1 Authority=>owner.Owns(db)?owner:throw Error("AUTHORITY_FENCE_UNAVAILABLE",503);
    private static OrderPriceCorrectionException Error(string code,int status=422)=>new(code,status);
    private static string Rev(long n)=>n.ToString(CultureInfo.InvariantCulture);
    private static string Etag(Location n,long revision)=>"\"lc1."+Convert.ToBase64String(n.RowVersion)+"."+Rev(revision)+"\"";
    private sealed class Head {public long Revision{get;init;}public bool Other{get;init;}}
    private sealed class Row
    {
        public Guid Id{get;init;}public Guid Block{get;init;}public string Kind{get;init;}="";
        public byte[] Body{get;init;}=[];public string Digest{get;init;}="";public bool Closed{get;init;}
    }
    private async Task Fence(CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE cp6_location_control_v1.controls,cp6_location_control_v1.events IN SHARE ROW EXCLUSIVE MODE",ct);
        if(!await db.Database.SqlQueryRaw<bool>("SELECT cp6_location_control_v1.ready() AS \"Value\"").SingleAsync(ct))throw Error("LOCATION_CONTROL_STORE_UNAVAILABLE",503);
    }
    private Task<Head?> HeadFor(Guid id,CancellationToken ct)=>db.Database.SqlQuery<Head>($"""
        SELECT revision AS "Revision",other_blocked AS "Other" FROM cp6_location_control_v1.controls
        WHERE tenant_id={db.CurrentTenantId} AND location_id={id}
        """).SingleOrDefaultAsync(ct);
    private IQueryable<Row> Rows(Guid location)=>db.Database.SqlQuery<Row>($"""
        SELECT e.id AS "Id",e.block_id AS "Block",e.kind AS "Kind",e.body AS "Body",e.digest AS "Digest",
          EXISTS(SELECT FROM cp6_location_control_v1.events r WHERE r.tenant_id=e.tenant_id AND r.location_id=e.location_id
            AND r.block_id=e.block_id AND r.kind='RELEASE') AS "Closed"
        FROM cp6_location_control_v1.events e WHERE e.tenant_id={db.CurrentTenantId} AND e.location_id={location}
        """);
    private LocationControlEntryV1 Entry(Guid location,Row row)
    {
        var o=JsonSerializer.Deserialize<LocationControlOriginalV1>(row.Body,EngineeringProductStoreV1.Json);
        if(o==null||o.TenantId!=db.CurrentTenantId||o.LocationId!=location||o.Id!=row.Id||o.BlockId!=row.Block||o.Kind!=row.Kind
            ||EngineeringProductStoreV1.Digest(row.Body)!=row.Digest)throw Error("LOCATION_CONTROL_ORIGINAL_UNAVAILABLE",503);
        return new(new(Owner,row.Id,"1",row.Digest),o,row.Kind=="BLOCK"?(row.Closed?"RELEASED":"OPEN"):"RECORDED");
    }
    private async Task<Location> Target(OperationsAccessV1 a,Guid id,CancellationToken ct)
    {
        // Independent query authority is mandatory even for a currently authorized write.
        var q=await a.SourceAsync(OperationsOwnerV1.Location,ct);
        var row=(Location)await q.RequireMasterNameTarget("locations",id,ct);
        await a.RequireMasterNameTarget("locations",id,ct);
        a.AtCommit(async t=>{await q.RequireMasterNameTarget("locations",id,t);await a.RequireMasterNameTarget("locations",id,t);});
        return row;
    }
    public Task<LocationControlViewV1> ReadAsync(Guid location,int page=1,CancellationToken ct=default)
    {
        if(location==Guid.Empty||page<1||page>int.MaxValue/50)throw Error("LOCATION_CONTROL_QUERY_INVALID",400);
        return Authority.ExecuteAsync(OperationsOwnerV1.Location,"query",false,async(a,t)=>
        {
            var n=await Target(a,location,t);await Fence(t);var h=await HeadFor(location,t);
            var rows=await Rows(location).OrderBy(x=>x.Id).Skip((page-1)*50).Take(50).ToArrayAsync(t);
            var count=await Rows(location).CountAsync(x=>x.Kind=="BLOCK"&&!x.Closed,t);
            return new LocationControlViewV1(db.CurrentTenantId,a.ActorId,location,Etag(n,h?.Revision??1),Rev(h?.Revision??1),
                n.IsBlocked,n.IsInitializationQuarantined,h?.Other??n.IsBlocked,count,rows.Select(r=>Entry(location,r)).ToArray(),page);
        },ct);
    }
    public Task<LocationControlResultV1> BlockAsync(Guid location,string? etag,LocationBlockCommandV1 c,CancellationToken ct=default)
    {
        if(c==null||c.BlockScope!="LOCAL_OPERATIONAL")throw Error("LOCATION_BLOCK_SCOPE_INVALID",400);
        return Command(location,etag,c.OperationId,"block","BLOCK",Guid.Empty,c.Reason,null,OperationsOwnerV1.Freeze(c),ct);
    }
    public Task<LocationControlResultV1> DecideReleaseAsync(Guid location,string? etag,LocationReleaseDecisionCommandV1 c,CancellationToken ct=default)
    {
        if(c==null||c.BlockId==Guid.Empty)throw Error("LOCATION_BLOCK_REQUIRED",400);
        return Command(location,etag,c.OperationId,"unblock","RELEASE_DECISION",c.BlockId,c.Reason,null,OperationsOwnerV1.Freeze(c),ct);
    }
    public Task<LocationControlResultV1> ReleaseAsync(Guid location,string? etag,LocationBlockReleaseCommandV1 c,CancellationToken ct=default)
    {
        if(c==null||c.BlockId==Guid.Empty||c.DecisionRef==null||c.DecisionRef.Owner!=Owner||c.DecisionRef.Version!="1")throw Error("LOCATION_RELEASE_BASIS_REQUIRED",400);
        return Command(location,etag,c.OperationId,"unblock","RELEASE",c.BlockId,c.Reason,c.DecisionRef,OperationsOwnerV1.Freeze(c),ct);
    }
    private Task<LocationControlResultV1> Command(Guid location,string? etag,Guid operation,string action,string kind,Guid block,
        string reason,EngineeringSourceRefV1? basis,object command,CancellationToken ct)
    {
        if(location==Guid.Empty||operation==Guid.Empty||string.IsNullOrWhiteSpace(reason)||reason.Length>1000||reason.Any(char.IsControl))throw Error("LOCATION_CONTROL_COMMAND_INVALID",400);
        if(string.IsNullOrEmpty(etag)||etag=="*")throw Error("LOCATION_CONTROL_ETAG_REQUIRED",428);
        var bytes=MainOwnerOperationJournalV1.Encode(new{location,etag,kind,command});var j=new MainOwnerOperationJournalV1(db);
        MainOwnerOperationJournalV1.Original? original=null;
        return Authority.ExecuteAsync(OperationsOwnerV1.Location,action,true,async(a,t)=>
        {
            var native=await Target(a,location,t);await Fence(t);
            if(original!=null)return await Disclose(a,location,operation,original,bytes,t);
            var h=await HeadFor(location,t);var before=h?.Revision??1;
            if(Etag(native,before)!=etag)throw Error("LOCATION_CONTROL_PREDECESSOR_CHANGED",412);
            if(before==long.MaxValue)throw Error("LOCATION_CONTROL_VERSION_EXHAUSTED",503);
            if(kind!="BLOCK")
            {
                var b=await Rows(location).SingleOrDefaultAsync(x=>x.Id==block&&x.Kind=="BLOCK",t)??throw Error("LOCATION_BLOCK_NOT_FOUND",404);
                if(Entry(location,b).State!="OPEN")throw Error("LOCATION_BLOCK_ALREADY_RELEASED",409);
            }
            if(kind=="RELEASE")
            {
                var r=await Rows(location).SingleOrDefaultAsync(x=>x.Id==basis!.Id&&x.Kind=="RELEASE_DECISION",t)??throw Error("LOCATION_RELEASE_BASIS_NOT_FOUND",404);
                var v=Entry(location,r);var o=v.Original;
                if(v.Reference!=basis||o.BlockId!=block||o.ActorId!=a.ActorId||o.ControlBefore!=Rev(before)||o.NativeEtag!=etag)
                    throw Error("LOCATION_RELEASE_BASIS_CHANGED",412);
            }
            if(h==null)await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_location_control_v1.controls(tenant_id,location_id,revision,other_blocked)
                VALUES({db.CurrentTenantId},{location},1,{native.IsBlocked})
                """,t);
            var id=Guid.NewGuid();if(kind=="BLOCK")block=id;
            var evidence=new LocationControlOriginalV1(db.CurrentTenantId,location,id,block,kind,operation,a.ActorId,DateTimeOffset.UtcNow,reason,Rev(before),etag!,basis);
            var body=EngineeringProductStoreV1.Encode(evidence);var digest=EngineeringProductStoreV1.Digest(body);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_location_control_v1.events(tenant_id,location_id,id,block_id,kind,revision_before,operation_id,actor_id,body,digest)
                VALUES({db.CurrentTenantId},{location},{id},{block},{kind},{before},{operation},{a.ActorId},{body},{digest})
                """,t);
            native=(Location)await a.RequireMasterNameTarget("locations",location,t);var after=(await HeadFor(location,t))!;
            var result=new LocationControlResultV1(operation,location,new(Owner,id,"1",digest),block,kind,Etag(native,after.Revision),Rev(after.Revision),native.IsBlocked);
            await j.Store(Owner,Contract,operation,a.ActorId,bytes,result,[location,id],new{action,kind,result},t);
            a.AtCommit(async token=>{await Fence(token);if((await HeadFor(location,token))!.Revision!=after.Revision)throw Error("LOCATION_CONTROL_PREDECESSOR_CHANGED",412);});
            return result;
        },ct,actionAtFence:async t=>{original=await j.Read(Owner,operation,t);return original==null?action:"query";});
    }
    private async Task<LocationControlResultV1> Disclose(OperationsAccessV1 a,Guid location,Guid operation,MainOwnerOperationJournalV1.Original o,byte[]? bytes,CancellationToken ct)
    {
        if(o.Actor!=a.ActorId||o.Contract!=Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
        if(o.State=="CLOSED")throw Error("ORIGINAL_OPERATION_CLOSED",409);
        var r=JsonSerializer.Deserialize<LocationControlResultV1>(o.Result);
        if(r==null||r.OperationId!=operation||r.LocationId!=location||!o.Objects.SequenceEqual(new[]{location,r.Reference.Id}))throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT",409);
        await Target(a,location,ct);if(bytes!=null&&!MainOwnerOperationJournalV1.Matches(o,bytes))throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT",409);return r;
    }
    public Task<LocationControlOperationV1> OperationAsync(Guid location,Guid operation,bool close=false,CancellationToken ct=default)
    {
        if(location==Guid.Empty||operation==Guid.Empty)throw Error("ORIGINAL_OPERATION_REQUIRED",400);
        var j=new MainOwnerOperationJournalV1(db);MainOwnerOperationJournalV1.Original? original=null;
        return Authority.ExecuteAsync(OperationsOwnerV1.Location,close?"abandon":"query",close,async(a,t)=>
        {
            await Target(a,location,t);await Fence(t);
            if(original!=null)
            {
                if(original.Actor!=a.ActorId||original.Contract!=Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
                if(original.State!="CLOSED")return new LocationControlOperationV1(operation,location,"COMMITTED",await Disclose(a,location,operation,original,null,t));
                var prior=JsonSerializer.Deserialize<LocationControlOperationV1>(original.Result);
                if(prior?.LocationId!=location||prior.OperationId!=operation)throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT",409);return prior;
            }
            if(!close)return new LocationControlOperationV1(operation,location,"UNKNOWN");
            var result=new LocationControlOperationV1(operation,location,"CLOSED");await j.Store(Owner,Contract,operation,a.ActorId,null,result,[location],new{action="close-uncommitted-local-control",location},t);return result;
        },ct,actionAtFence:async t=>{original=await j.Read(Owner,operation,t);return original!=null||!close?"query":"abandon";});
    }
}
