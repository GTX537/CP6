using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Space;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.ToolInstanceStoreV1;
namespace CP6.Core.Services.Wms;

/// <summary>Real registration provenance. A cycle observation is never an execution permit.</summary>
public sealed class ToolRegistrationOwnerV1(CP6Context db,OperationsOwnerV1 owner)
{
    internal const string RegistrationOwner="Main.Wms.ToolRegistration.v1";
    private const string Contract="tool.registration.v2",LegacyContract="tool.registration.v1";
    private static bool KnownContract(string value)=>value is Contract or LegacyContract;
    private OperationsOwnerV1 Authority=>owner.Owns(db)?owner:throw Error("AUTHORITY_FENCE_UNAVAILABLE",503);
    public Task<ToolRegistrationCreationContextV1> CreationContextAsync(CancellationToken ct=default)
        =>Authority.ExecuteAsync(OperationsOwnerV1.ToolInstance,"add",false,(a,t)=>a.ToolCreationContext(t),ct);
    public Task<EngineeringSiteChoiceV1[]> SitesAsync(string? search,CancellationToken ct=default)
    {
        if(search?.Length>100)throw Error("TOOL_SITE_QUERY_INVALID",400);
        return Authority.ExecuteAsync(OperationsOwnerV1.ToolInstance,"query",false,async(a,t)=>
        {
            var site=await a.SourceAsync(OperationsOwnerV1.PhysicalSite,t);
            var rows=db.Space_Sites.AsNoTracking().Where(x=>!x.IsDeleted&&x.Enable);
            if(search!=null)rows=rows.Where(x=>x.SiteCode.Contains(search));
            var ids=await rows.OrderBy(x=>x.SiteCode).ThenBy(x=>x.Id).Take(50).Select(x=>x.Id).ToArrayAsync(t);
            var choices=new List<EngineeringSiteChoiceV1>();foreach(var id in ids)choices.Add(await PhysicalSiteIdentityReaderV1.ReadOwnedAsync(db,site,id,t));
            return choices.ToArray();
        },ct);
    }
    private async Task Fence(CancellationToken ct,bool write=false)
    {
        if(!db.Database.IsNpgsql()||db.Database.CurrentTransaction==null)throw Error("AUTHORITY_FENCE_UNAVAILABLE",503);
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE cp6_tool_instance_v1.registrations "+(write?"IN SHARE ROW EXCLUSIVE MODE":"IN SHARE MODE"),ct);
        if(!await db.Database.SqlQueryRaw<bool>("""
            SELECT count(*)=2 AS "Value" FROM pg_catalog.pg_trigger WHERE tgrelid='cp6_tool_instance_v1.registrations'::regclass
              AND tgname IN ('tool_registration_immutable','tool_registration_no_truncate') AND tgenabled IN ('O','A') AND NOT tgisinternal
            """).SingleAsync(ct))throw Error("TOOL_REGISTRATION_UNAVAILABLE",503);
    }
    private sealed class Stored { public string Json{get;set;}="";public string Digest{get;set;}=""; }
    private async Task<(Stored Row,ToolRegistrationOriginalV1 Value)?> StoredAsync(Guid id,CancellationToken ct)
    {
        var row=await db.Database.SqlQuery<Stored>($"""
            SELECT original_json AS "Json",original_digest AS "Digest" FROM cp6_tool_instance_v1.registrations
            WHERE tenant_id={db.CurrentTenantId} AND object_id={id}
            """).SingleOrDefaultAsync(ct);
        if(row==null)return null;
        var value=JsonSerializer.Deserialize<ToolRegistrationOriginalV1>(row.Json);
        if(value==null||value.TenantId!=db.CurrentTenantId||value.ObjectId!=id||Hash(row.Json)!=row.Digest||!KnownContract(value.InputContract))
            throw Error("TOOL_REGISTRATION_UNAVAILABLE",503);
        return (row,value);
    }
    public Task<ToolRegistrationViewV1?> ReadAsync(Guid id,CancellationToken ct=default)
    {
        if(id==Guid.Empty)throw Error("TOOL_INSTANCE_ID_INVALID",400);
        return Authority.ExecuteAsync(OperationsOwnerV1.ToolInstance,"query",false,async(a,t)=>
        {
            var store=new ToolInstanceStoreV1(db);await store.Fence(t);await Fence(t);
            var native=await store.Read(a,id,null,t);var saved=await StoredAsync(id,t);if(saved==null)return null;
            var (row,value)=saved.Value;var site=await a.SourceAsync(OperationsOwnerV1.PhysicalSite,t);
            var currentSite=await PhysicalSiteIdentityReaderV1.ReadOwnedAsync(db,site,value.Site.Id,t);
            // Inspect the intervening immutable observations, not only today's equal-looking counter.
            var revision=Revision(value.NativeAtRegistration.Version);
            var coherent=await db.Database.SqlQuery<bool>($"""
                SELECT NOT EXISTS(
                  SELECT FROM cp6_tool_instance_v1.versions v
                  JOIN cp6_tool_instance_v1.versions initial ON initial.tenant_id=v.tenant_id AND initial.object_id=v.object_id AND initial.revision={revision}
                  WHERE v.tenant_id={db.CurrentTenantId} AND v.object_id={id} AND v.revision>={revision}
                    AND (NOT v.source_present OR
                      (v.source_json::jsonb->'usedShots',v.source_json::jsonb->'lastUsedAt',v.source_json::jsonb->'nextMaintenanceDate',v.source_json::jsonb->'status')
                      IS DISTINCT FROM
                      (initial.source_json::jsonb->'usedShots',initial.source_json::jsonb->'lastUsedAt',initial.source_json::jsonb->'nextMaintenanceDate',initial.source_json::jsonb->'status'))
                ) AS "Value"
                """).SingleAsync(t);
            return new ToolRegistrationViewV1(db.CurrentTenantId,new(RegistrationOwner,id,"1",row.Digest),native.Reference,value.Site,currentSite,value.CycleAnchorId,
                value.Origin,value.InitialCycleShots,coherent?value.CycleNo:null,coherent?value.Origin:"RECONCILIATION_REQUIRED",coherent?value.InitialCycleShots:null,
                coherent?value.LifetimeKnowledge:"UNKNOWN",coherent?value.LifetimeShots:null,row.Json,InputContract:value.InputContract);
        },ct);
    }
    private async Task<EngineeringSiteChoiceV1> ExactSite(OperationsAccessV1 site,EngineeringSourceRefV1 expected,CancellationToken ct)
    {
        var current=await PhysicalSiteIdentityReaderV1.ReadOwnedAsync(db,site,expected.Id,ct);
        if(current.Reference!=expected)throw Error("TOOL_SITE_CHANGED",412);
        if(!current.Enabled)throw Error("TOOL_SITE_DISABLED",409);
        return current;
    }
    internal async Task HoldExactAsync(OperationsAccessV1 query,ToolRegistrationRefV1 expected,ToolInstanceRefV1 native,EngineeringSourceRefV1 site,CancellationToken ct)
    {
        if(expected==null||native==null||site==null||expected.Owner!=RegistrationOwner||expected.Version!="1"||expected.Id!=native.Id)
            throw Error("TOOL_REGISTRATION_REF_INVALID",400);
        await new ToolInstanceStoreV1(db).HoldExactAsync(query,native,ct);await Fence(ct);
        var saved=await StoredAsync(expected.Id,ct)??throw Error("TOOL_REGISTRATION_NOT_FOUND",404);
        if(saved.Row.Digest!=expected.Digest||saved.Value.Site.Id!=site.Id)throw Error("TOOL_REGISTRATION_CHANGED",412);
        var source=await query.SourceAsync(OperationsOwnerV1.PhysicalSite,ct);await ExactSite(source,site,ct);
        query.AtCommit(async t=>{await ExactSite(source,site,t);var final=await StoredAsync(expected.Id,t);
            if(final==null||final.Value.Row.Digest!=expected.Digest)throw Error("TOOL_REGISTRATION_CHANGED",412);});
    }
    private static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static readonly UTF8Encoding StrictUtf8=new(false,true);
    private static string? OriginalText(string? value)
    {
        // A malformed UTF-16 sequence cannot be preserved as Unicode; never replace it silently.
        try { if(value!=null)StrictUtf8.GetByteCount(value);return value; }
        catch(EncoderFallbackException) { throw Error("TOOL_REGISTRATION_INVALID",400); }
    }
    private static byte[] EncodeCommand(ToolRegistrationCommandV1 command,string? etag,string contract)
    {
        // Only an already-stored v1 operation may use its historical, lossy codec.
        // New operations always use v2; do not rewrite old input/result/original bytes.
        if(contract==LegacyContract)command=command with{Evidence=command.Evidence.Normalize(NormalizationForm.FormC),
            Profile=command.Profile is {} p?p with{SizeNote=p.SizeNote?.Normalize(NormalizationForm.FormC),Remarks=p.Remarks?.Normalize(NormalizationForm.FormC)}:null};
        return MainOwnerOperationJournalV1.Encode(new{contract,etag,command});
    }
    private static decimal? Cost(string? value)=>value==null?null:Regex.IsMatch(value,"\\A(?:0|[1-9][0-9]{0,15})(?:\\.[0-9]{1,2})?\\z")
        &&decimal.TryParse(value,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var n)?n:throw Error("TOOL_PROFILE_COST_INVALID",400);
    private static DateTime? Date(string? value)=>value==null?null:DateTime.TryParseExact(value,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var d)
        &&d.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)==value?d:throw Error("TOOL_PROFILE_DATE_INVALID",400);
    private static ToolRegistrationCommandV1 Validate(ToolRegistrationCommandV1 input,string? etag)
    {
        if(input==null||input.OperationId==Guid.Empty||input.ObjectId==Guid.Empty||input.Kind is not("CREATE" or "BIND_LEGACY")
            ||input.Site==null||input.Site.Owner!=EngineeringProductValuesV1.SiteOwner||input.Site.Id==Guid.Empty
            ||input.Site.Digest==null||!Regex.IsMatch(input.Site.Digest,"\\A[0-9a-f]{64}\\z")||string.IsNullOrWhiteSpace(input.Evidence)||input.Evidence.Length>1000)
            throw Error("TOOL_REGISTRATION_INVALID",400);
        Revision(input.Site.Version);OriginalText(input.Evidence);
        if(input.Kind=="BIND_LEGACY")
        {
            if(input.Expected==null||input.Expected.Owner!=Owner||input.Expected.Id!=input.ObjectId||input.Profile!=null
                ||input.DepartmentId!=null||input.CreationAuthorityVersion!=null)throw Error("TOOL_REGISTRATION_INVALID",400);
            Revision(input.Expected.Version);return input;
        }
        var p=input.Profile;
        if(input.Expected!=null||!string.IsNullOrEmpty(etag)||p==null||p.PlateType is not("PLATE" or "MOLD" or "CYL" or "OTHER")
            ||p.ColorCount is <1 or >12||p.MaxShots is <1||p.SizeNote?.Length>40||p.Remarks?.Length>500)
            throw Error("TOOL_REGISTRATION_INVALID",400);
        Date(p.MadeDate);
        return input with{Profile=p with{MadeCost=Cost(p.MadeCost)?.ToString("0.00",CultureInfo.InvariantCulture),SizeNote=OriginalText(p.SizeNote),Remarks=OriginalText(p.Remarks)}};
    }
    public Task<ToolRegistrationResultV1> CommandAsync(ToolRegistrationCommandV1 input,string? etag,CancellationToken ct=default)
    {
        var command=OperationsOwnerV1.Freeze(Validate(input,etag));var action=command.Kind=="CREATE"?"add":"initialize";
        var bytes=EncodeCommand(command,etag,Contract);var journal=new MainOwnerOperationJournalV1(db);
        MainOwnerOperationJournalV1.Original? original=null;
        return Authority.ExecuteAsync(OperationsOwnerV1.ToolInstance,action,true,async(a,t)=>
        {
            var store=new ToolInstanceStoreV1(db);await store.Fence(t,true);await Fence(t,true);
            var query=await a.SourceAsync(OperationsOwnerV1.ToolInstance,t);
            if(original!=null)return await Disclose(query,store,command.OperationId,original,EncodeCommand(command,etag,original.Contract),t);
            var site=await a.SourceAsync(OperationsOwnerV1.PhysicalSite,t);await ExactSite(site,command.Site,t);
            a.AtCommit(async token=>{await ExactSite(site,command.Site,token);});
            ToolInstanceViewV1 native;
            if(command.Kind=="CREATE")
            {
                var ticket=await a.BeginToolCreation(command.ObjectId,command.CreationAuthorityVersion,command.DepartmentId,t);
                if(await db.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT FROM cp6_tool_instance_v1.heads WHERE object_id={command.ObjectId}) AS \"Value\"").SingleAsync(t))
                    throw Error("TOOL_INSTANCE_IDENTITY_CONFLICT",409);
                var p=command.Profile!;if(p.MadeCost!=null)a.RequireToolFieldWrite("MadeCost");if(p.Remarks!=null)a.RequireToolFieldWrite("Remarks");
                if(await db.WmsSequences.AnyAsync(x=>x.Prefix=="PLT2"&&x.DateKey=="*"&&(x.NextNo<0||x.NextNo==int.MaxValue),t))throw Error("TOOL_NUMBER_UNAVAILABLE",503);
                var number=await new WmsSequenceService(db).NextAsync("PLT2");
                if(await db.PlateMoldStocks.IgnoreQueryFilters().AnyAsync(x=>x.TenantId==db.CurrentTenantId&&x.PlateNo==number,t))throw Error("TOOL_NUMBER_CONFLICT",409);
                db.PlateMoldStocks.Add(new PlateMoldStock{Id=command.ObjectId,PlateNo=number,PlateType=p.PlateType,ColorCount=p.ColorCount,
                    SizeNote=p.SizeNote,MadeDate=Date(p.MadeDate),MadeCost=Cost(p.MadeCost),MaxShots=p.MaxShots,Remarks=p.Remarks,
                    UsedShots=0,Status=0,Creator=a.Actor,CreateDate=DateTime.Now});
                await db.SaveChangesAsync(t);await a.CompleteToolCreation(ticket,t);native=await store.Read(query,command.ObjectId,null,t);
            }
            else
            {
                native=await store.Read(query,command.ObjectId,null,t);await a.RequireToolInstance(db,command.ObjectId,t);
                if(string.IsNullOrEmpty(etag))throw Error("IF_MATCH_REQUIRED",428);
                if(etag!=native.Etag||command.Expected!=native.Reference)throw Error("TOOL_INSTANCE_CHANGED",412);
                if(native.Source.GetProperty("status").GetInt32()==3)throw Error("TOOL_INSTANCE_DISCARDED",409);
            }
            if(await StoredAsync(command.ObjectId,t)!=null)throw Error("TOOL_ALREADY_REGISTERED",409);
            var shots=native.Source.GetProperty("usedShots").GetInt32();if(shots<0)throw Error("TOOL_CYCLE_OBSERVATION_INVALID",409);
            var fresh=command.Kind=="CREATE";
            var value=new ToolRegistrationOriginalV1(db.CurrentTenantId,command.ObjectId,a.ActorId,fresh?"NEW_REGISTERED":"LEGACY_OBSERVED",
                Guid.NewGuid(),fresh?"1":null,shots.ToString(CultureInfo.InvariantCulture),fresh?"KNOWN_FROM_REGISTRATION":"UNKNOWN",fresh?"0":null,
                native.Reference,command.Site,command.Evidence,Contract);
            var json=JsonSerializer.Serialize(value);var digest=Hash(json);var rev=Revision(native.Reference.Version);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_tool_instance_v1.registrations(tenant_id,object_id,native_revision,original_json,original_digest)
                VALUES({db.CurrentTenantId},{command.ObjectId},{rev},{json},{digest})
                """,t);
            var result=new ToolRegistrationResultV1(command.OperationId,fresh?"TOOL_CREATE":"TOOL_INITIALIZE",command.ObjectId,"1",digest,Contract);
            await journal.Store(RegistrationOwner,Contract,command.OperationId,a.ActorId,bytes,result,[command.ObjectId],
                new{action=command.Kind,actor=a.ActorId,native=native.Reference,site=command.Site,value.CycleAnchorId,value.Origin},t);
            // New registration does not upgrade query or warehouse scope; every participant is checked again.
            await HoldExactAsync(query,new(RegistrationOwner,command.ObjectId,"1",digest),native.Reference,command.Site,t);
            a.AtCommit(token=>a.RequireToolInstance(db,command.ObjectId,token));return result;
        },ct,actionAtFence:async t=>{original=await journal.Read(RegistrationOwner,command.OperationId,t);return original==null?action:"query";});
    }
    private async Task<ToolRegistrationResultV1> Disclose(OperationsAccessV1 query,ToolInstanceStoreV1 store,Guid operation,MainOwnerOperationJournalV1.Original original,byte[]? input,CancellationToken ct)
    {
        if(original.Actor!=query.ActorId||!KnownContract(original.Contract))throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
        if(original.State=="CLOSED")throw Error("ORIGINAL_OPERATION_CLOSED",409);
        if(input!=null&&!MainOwnerOperationJournalV1.Matches(original,input))throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT",409);
        var result=JsonSerializer.Deserialize<ToolRegistrationResultV1>(original.Result);
        if(original.State!="COMMITTED"||result==null||result.OperationId!=operation||result.Kind is not("TOOL_CREATE" or "TOOL_INITIALIZE")
            ||result.Version!="1"||result.InputContract!=original.Contract||original.Objects.Length!=1||original.Objects[0]!=result.ObjectId)throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
        var saved=await StoredAsync(result.ObjectId,ct)??throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
        await store.Read(query,result.ObjectId,Revision(saved.Value.NativeAtRegistration.Version),ct);
        if(saved.Row.Digest!=result.Digest)throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
        return result;
    }
    public Task<ToolRegistrationOperationV1> OperationAsync(Guid id,bool close,CancellationToken ct=default)
    {
        if(id==Guid.Empty)throw Error("ORIGINAL_OPERATION_REQUIRED",400);
        return Authority.ExecuteAsync(OperationsOwnerV1.ToolInstance,"query",close,async(a,t)=>
        {
            var store=new ToolInstanceStoreV1(db);await store.Fence(t,close);await Fence(t,close);var journal=new MainOwnerOperationJournalV1(db);
            var original=await journal.Read(RegistrationOwner,id,t);
            if(original!=null)
            {
                if(original.Actor!=a.ActorId||!KnownContract(original.Contract))throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
                return original.State=="CLOSED"?new ToolRegistrationOperationV1(id,"CLOSED"):new(id,"COMMITTED",await Disclose(a,store,id,original,null,t));
            }
            if(!close)return new ToolRegistrationOperationV1(id,"UNKNOWN");
            var result=new ToolRegistrationOperationV1(id,"CLOSED");await journal.Store(RegistrationOwner,Contract,id,a.ActorId,null,result,[],new{action="close-tool-registration",actor=a.ActorId},t);return result;
        },ct,readOnlyAuthorityFence:!close);
    }
}
