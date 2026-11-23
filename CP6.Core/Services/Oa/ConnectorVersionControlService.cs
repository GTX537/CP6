using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CP6.Core.Services.Oa;

public sealed partial class ConnectorVersionControlService(CP6Context db, IHttpContextAccessor http,
    ScopeAuthorizationFenceV1 fence, FlowAuthoringOwnerV2 flows, Func<CancellationToken,Task<Guid>> currentActor,
    IOptions<OaConnectorControlOptionsV1> options,IEnumerable<IConnectorDispatchAdmissionV1> providers,
    IEnumerable<ITriggerRegistrationAuthorityV1> triggerAuthorities)
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive=false };
    internal const string Menu = "oa-flow-admin";
    private Guid Tenant => db.CurrentTenantId;
    private Guid Environment => fence.EnvironmentId;
    private static ScopeAuthorizationExceptionV1 Error(string code,int status=422) => new(code,status);
    private sealed record Head(Guid Id,string Kind,string SourceKind,string Name,Guid Owner,Guid Revision,Guid Latest,Guid? Active);
    private sealed record Version(Guid Id,int Number,Guid Maker,Guid ProfileId,string DisplayName,string Content,bool Published,string State);
    private sealed record Profile(Guid Id,string Kind,string Label,string Content,string Adoption,string? Evidence,bool Enabled,Guid Revision);
    private sealed record Existing(string Operation,Guid? Target,string Digest,string Receipt);
    private sealed record Applied(Guid Root,Guid Version,object Result,DateTimeOffset? Expires=null);
    private IQueryable<Head> Roots() => db.Database.SqlQuery<Head>($"""
        SELECT id AS "Id",kind AS "Kind",source_kind AS "SourceKind",name AS "Name",owner_id AS "Owner",
            revision AS "Revision",latest_version AS "Latest",active_version AS "Active" FROM cp6_oa_conn_v1.roots
        WHERE tenant_id={Tenant} AND environment_id={Environment}
        """);
    private IQueryable<Version> Versions(Guid root) => db.Database.SqlQuery<Version>($"""
        SELECT v.id AS "Id",v.number AS "Number",v.maker AS "Maker",v.profile_id AS "ProfileId",v.display_name AS "DisplayName",
            v.content::text AS "Content",c.published AS "Published",c.state AS "State" FROM cp6_oa_conn_v1.versions v
        JOIN cp6_oa_conn_v1.version_control c ON (c.tenant_id,c.environment_id,c.root_id,c.version_id)=(v.tenant_id,v.environment_id,v.root_id,v.id)
        WHERE v.tenant_id={Tenant} AND v.environment_id={Environment} AND v.root_id={root}
        """);
    private IQueryable<Profile> Profiles() => db.Database.SqlQuery<Profile>($"""
        SELECT p.id AS "Id",p.kind AS "Kind",p.label AS "Label",p.content::text AS "Content",p.adoption AS "Adoption",
            p.evidence_ref AS "Evidence",COALESCE(c.enabled,false) AS "Enabled",COALESCE(c.revision,p.id) AS "Revision"
        FROM cp6_oa_conn_v1.profiles p LEFT JOIN cp6_oa_conn_v1.profile_control c
            ON (c.tenant_id,c.environment_id,c.profile_id)=(p.tenant_id,p.environment_id,p.id)
        WHERE p.tenant_id={Tenant} AND p.environment_id={Environment}
        """);
    private Task<Existing?> Original(Guid actor,string id,CancellationToken ct) => db.Database.SqlQuery<Existing>($"""
        SELECT operation AS "Operation",target AS "Target",digest AS "Digest",receipt::text AS "Receipt" FROM cp6_oa_conn_v1.commands
        WHERE tenant_id={Tenant} AND environment_id={Environment} AND actor={actor} AND command_id={id}
        """).SingleOrDefaultAsync(ct);
    private async Task<Head> Root(string kind,Guid id,CancellationToken ct) =>
        await Roots().SingleOrDefaultAsync(r=>r.Kind==kind && r.Id==id,ct) ?? throw Error("RESOURCE_NOT_FOUND",404);
    private async Task<Version> Exact(Head root,Guid version,CancellationToken ct) =>
        await Versions(root.Id).SingleOrDefaultAsync(v=>v.Id==version,ct) ?? throw Error("PIN_MISMATCH",409);
    private async Task<Profile> ProfileFor(Guid id,CancellationToken ct) =>
        await Profiles().SingleOrDefaultAsync(p=>p.Id==id,ct) ?? throw Error("REGISTRATION_UNAVAILABLE",422);
    private static ConnProfileV1 Definition(Profile p) =>
        JsonSerializer.Deserialize<ConnProfileV1>(p.Content,Json) ?? throw Error("REGISTRATION_UNAVAILABLE",503);
    private static JsonElement Value(object value) => JsonSerializer.SerializeToElement(value,Json);
    private static JsonElement Stored(string text) => JsonSerializer.Deserialize<JsonElement>(text);
    private void Entry()
    {
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction!=null || db.ChangeTracker.HasChanges() ||
            System.Transactions.Transaction.Current!=null) throw Error("AUTHORITY_FENCE_UNAVAILABLE",503);
        _=Environment;
    }
    private async Task<UserPermissionContext> Actor(CancellationToken ct)
    {
        var id=await currentActor(ct);
        if (id!=CurrentPermissionContext.RequireCurrentUser(db,http) || http.HttpContext!.User.HasClaim(x=>x.Type=="impersonator_id"))
            throw Error("AUTHENTICATION_REQUIRED",401);
        var exp=http.HttpContext.User.FindFirst("exp")?.Value;
        if (exp!=null && (!long.TryParse(exp,out var end) || DateTimeOffset.UtcNow.ToUnixTimeSeconds()>=end))
            throw Error("AUTHENTICATION_REQUIRED",401);
        if (!await db.Sys_Tenants.AsNoTracking().AnyAsync(t=>t.Id==Tenant && t.Enable && (t.ExpireDate==null || t.ExpireDate>DateTime.Now),ct)
            || !await db.Sys_Users.AsNoTracking().AnyAsync(u=>u.Id==id && u.Enable && (u.LockedUntil==null || u.LockedUntil<=DateTime.Now),ct))
            throw Error("AUTHENTICATION_REQUIRED",401);
        return await PostgreSqlPermissionReader.ReadAsync(db,id,ct);
    }
    private async Task Require(UserPermissionContext actor,string action,CancellationToken ct)
    {
        if (!actor.MenuKeys.Contains(Menu) || !actor.ActionKeys.Contains(Menu+":"+action) ||
            !await (from a in db.Sys_MenuActions join m in db.Sys_Menus on a.MenuId equals m.MenuId
                    where m.Enable && m.MenuKey==Menu && a.ActionCode==action select a).AnyAsync(ct))
            throw Error("ACTION_FORBIDDEN",403);
    }
    private static string Permission(string kind,string action) => "conn."+kind.ToLowerInvariant()+"."+action;
    private static void CheckKind(string kind)
    {
        if (kind is not ("TRIGGER" or "CONNECTOR")) throw Error("RESOURCE_NOT_FOUND",404);
    }
    private static void Expected(string expected,Guid revision)
    {
        if(expected!=revision.ToString("D")) throw Error("CONTROL_CHANGED",412);
    }
    private async Task<T> Read<T>(Func<UserPermissionContext,CancellationToken,Task<T>> query,CancellationToken ct)
    {
        Entry();
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        await AuthenticationCompletionGateV1.AcquireAsync(db,ct);
        var actor=await Actor(ct);
        await Require(actor,"conn.read",ct);
        var result=await query(actor,ct);
        var final=await Actor(ct);
        if(final.UserId!=actor.UserId) throw Error("AUTHENTICATION_REQUIRED",401);
        await Require(final,"conn.read",ct);
        await tx.CommitAsync(ct);
        return result;
    }
    public Task<JsonElement> CommandAsync(string commandId,CancellationToken ct) => Read(async(actor,token)=>
    {
        var original=await Original(actor.UserId,commandId,token);
        return original==null ? Value(new {commandId,outcome="NOT_OBSERVED"}) : Stored(original.Receipt);
    },ct);

    public async Task<JsonElement> WriteAsync(string kind,Guid? id,string operation,byte[] bytes,CancellationToken ct)
    {
        CheckKind(kind);
        var wire=ConnWireV1.Parse(bytes);
        Entry();
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        await AuthenticationCompletionGateV1.AcquireAsync(db,ct);
        var actor=await Actor(ct);
        await Require(actor,"conn.read",ct);
        string op=kind+"_"+operation;
        var old=await Original(actor.UserId,wire.Id,ct);
        if(old!=null)
        {
            if(old.Operation!=op || old.Target!=id || old.Digest!=wire.Digest) throw Error("COMMAND_PAYLOAD_CONFLICT",409);
            await Require(await Actor(ct),"conn.read",ct);
            await tx.CommitAsync(ct);
            return Stored(old.Receipt);
        }
        var lease=await fence.AcquireAsync(ct);
        if(!await db.Database.SqlQueryRaw<bool>("SELECT cp6_oa_conn_v1.ready() AS \"Value\"").SingleAsync(ct))
            throw Error("IMPACT_WRITER_COVERAGE_UNAVAILABLE",503);
        string action=RequiredAction(kind,operation,wire);
        await Require(actor,action,ct);
        var head=id.HasValue ? await Root(kind,id.Value,ct) : null;
        var applied=operation switch
        {
            "CREATE" or "REVISE"=> await Draft(kind,head,wire,actor,lease.Snapshot,ct),
            "PREVIEW"=> await Preview(head!,wire,actor,lease.Snapshot,ct),
            "SUBMIT"=> await Submit(head!,wire,actor,lease.Snapshot,ct),
            "DECIDE"=> await Decide(head!,wire,actor,lease.Snapshot,ct),
            "PUBLISH" or "ENABLE" or "DISABLE" or "ACTIVATE" or "RETIRE"=>
                await Control(head!,operation,wire,actor,lease.Snapshot,ct),
            _=>throw Error("RESOURCE_NOT_FOUND",404)
        };
        var receipt=Value(new {commandId=wire.Id,operation=op,outcome="SUCCEEDED",rootId=applied.Root,versionId=applied.Version,result=applied.Result});
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_oa_conn_v1.commands(tenant_id,environment_id,actor,command_id,operation,target,digest,receipt)
            VALUES({Tenant},{Environment},{actor.UserId},{wire.Id},{op},{id},{wire.Digest},CAST({receipt.GetRawText()} AS jsonb));
            INSERT INTO cp6_oa_conn_v1.audit(tenant_id,environment_id,actor,command_id,operation,root_id,version_id,authority_epoch)
            VALUES({Tenant},{Environment},{actor.UserId},{wire.Id},{op},{applied.Root},{applied.Version},{lease.Snapshot.Epoch})
            """,ct);
        var final=await Actor(ct);
        if(final.UserId!=actor.UserId) throw Error("AUTHENTICATION_REQUIRED",401);
        await Require(final,"conn.read",ct);
        await Require(final,action,ct);
        await fence.ValidateAsync(lease,ct);
        if(applied.Expires is DateTimeOffset expires) await RequireFresh(expires,ct);
        try { await tx.CommitAsync(ct); }
        catch(Exception e) when(e is not ScopeAuthorizationExceptionV1)
        { throw new ScopeAuthorizationExceptionV1("COMMAND_OUTCOME_UNKNOWN",503,e); }
        return receipt;
    }
    private static string RequiredAction(string kind,string op,ConnWireV1 wire) => Permission(kind,op switch
    {
        "CREATE" or "REVISE"=>"write",
        "DECIDE"=>"review",
        "PUBLISH"=>"publish",
        "ACTIVATE"=>"activate",
        "ENABLE" or "DISABLE" or "RETIRE"=>"control",
        "PREVIEW"=>wire.Read<ConnPreviewInputV1>().Action=="ACTIVATE" ? "activate" : "control",
        "SUBMIT"=>wire.Read<ConnReviewInputV1>().Kind switch { "TRIGGER_PUBLISH"=>"write","CONNECTOR_ACTIVATE"=>"activate",_=>"control" },
        _=>throw Error("RESOURCE_NOT_FOUND",404)
    });
}
