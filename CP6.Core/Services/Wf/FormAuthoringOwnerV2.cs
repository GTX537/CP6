using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Entity.Contracts.Wire;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormAuthoringBodyV2(string Name, string SchemaJson, string? Category, string? SubCategory);
public sealed record FormAuthoringViewV2(Guid DefinitionId, Guid DraftId, string Key, string Version,
    Dictionary<string,string?> Fields, string[] WritableFields, bool Enabled = false, bool Published = false, int VersionNumber = 1);
public sealed record FormAuthoringResultV2(string CommandId, string Outcome, Guid? ResourceId, string? ResourceVersion,
    FormAuthoringViewV2? Result, DateTimeOffset? RecordedAtUtc, string? CorrelationId);

/// <summary>FORM draft authority. New drafts remain disabled and separate from native executable definitions.</summary>
public sealed partial class FormAuthoringOwnerV2
{
    public const string Resource="workflow-form-definition", Operation="FORM_SAVE_DRAFT";
    public static readonly string[] Fields=["Name","SchemaJson","Category","SubCategory"];
    private const string Mapping="Workflow.FormAuthoring.v2.1";
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive=false };
    private readonly CP6Context db;
    private readonly IHttpContextAccessor http;
    private readonly ScopeAuthorizationFenceV1 fence;
    private readonly Func<CancellationToken,Task<Guid>> currentActor;
    private readonly Func<AuthorizationCatalogSnapshotV1> catalog;
    private DateTime? currentTenantExpiry;
    public FormAuthoringOwnerV2(CP6Context db,IHttpContextAccessor http,ScopeAuthorizationFenceV1 fence,Func<CancellationToken,Task<Guid>> currentActor)
        :this(db,http,fence,currentActor,AuthorizationCatalogV1.Capture) { }
    internal FormAuthoringOwnerV2(CP6Context db,IHttpContextAccessor http,ScopeAuthorizationFenceV1 fence,Func<CancellationToken,Task<Guid>> currentActor,Func<AuthorizationCatalogSnapshotV1> catalog)
    { this.db=db;this.http=http;this.fence=fence;this.currentActor=currentActor;this.catalog=catalog; }
    private static ScopeAuthorizationExceptionV1 Error(string code,int status=503)=>new(code,status);
    private void Entry()
    {
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction != null || db.ChangeTracker.HasChanges()
            || System.Transactions.Transaction.Current != null) throw Error("AUTHORITY_FENCE_UNAVAILABLE");
        _ = fence.EnvironmentId;
    }
    private async Task Root(CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='10s'", ct);
        await AuthenticationCompletionGateV1.AcquireAsync(db, ct);
    }
    private async Task<UserPermissionContext> Actor(CancellationToken ct)
    {
        var actor = await currentActor(ct);
        if (actor != CurrentPermissionContext.RequireCurrentUser(db, http)
            || http.HttpContext!.User.HasClaim(c => c.Type == "impersonator_id")) throw Error("REAUTH_REQUIRED", 401);
        var permission = await PostgreSqlPermissionReader.ReadAsync(db, actor, ct);
        var tenant = await db.Sys_Tenants.AsNoTracking().Where(x => x.Id == db.CurrentTenantId && x.Enable).Select(x => new { x.ExpireDate }).SingleOrDefaultAsync(ct);
        if (tenant == null || tenant.ExpireDate <= DateTime.Now
            || !await db.Sys_Users.AsNoTracking().AnyAsync(x => x.Id == actor && x.Enable && (x.LockedUntil == null || x.LockedUntil <= DateTime.Now), ct))
            throw Error("REAUTH_REQUIRED", 401);
        currentTenantExpiry = tenant.ExpireDate;
        return permission;
    }
    private void CompletionClock()
    {
        var expiry = http.HttpContext?.User.FindFirst("exp")?.Value;
        if (currentTenantExpiry <= DateTime.Now || expiry != null && (!long.TryParse(expiry, out var seconds) || seconds <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()))
            throw Error("REAUTH_REQUIRED", 401);
    }
    private async Task Action(UserPermissionContext actor, string menu, string action, CancellationToken ct)
    {
        if (!actor.ActionKeys.Contains(menu + ":" + action) || !await
            (from a in db.Sys_MenuActions join m in db.Sys_Menus on a.MenuId equals m.MenuId
             where m.Enable && m.MenuKey == menu && a.ActionCode == action select a).AnyAsync(ct)) throw Error("ACTION_FORBIDDEN", 403);
    }

    private sealed record Head(Guid Id,Guid DraftId,string Key,Guid OwnerId,Guid? DeptId,Guid Version,
        string Name,string SchemaJson,string? Category,string? SubCategory,Guid SavedBy);
    private IQueryable<Head> Heads()=>db.Database.SqlQuery<Head>($"""
        SELECT h.id AS "Id",h.draft_id AS "DraftId",h.form_key AS "Key",h.owner_id AS "OwnerId",h.owner_dept_id AS "DeptId",
          d.row_version AS "Version",d.name AS "Name",d.schema_json AS "SchemaJson",d.category AS "Category",d.sub_category AS "SubCategory",d.saved_by AS "SavedBy"
        FROM cp6_form_authoring_v2.heads h JOIN cp6_form_authoring_v2.drafts d
          ON d.tenant_id=h.tenant_id AND d.environment_id=h.environment_id AND d.head_id=h.id AND d.id=h.draft_id
        WHERE h.tenant_id={db.CurrentTenantId} AND h.environment_id={fence.EnvironmentId}
        """);
    private async Task Ready(CancellationToken ct)
    {
        if(!await db.Database.SqlQueryRaw<bool>("SELECT cp6_form_authoring_v2.ready() AS \"Value\"").SingleAsync(ct))
            throw Error("FORM_AUTHORING_WRITER_COVERAGE_UNAVAILABLE");
    }
    private int ResourceScope(UserPermissionContext actor)
    {
        var c = catalog();
        if (!c.IsSealed || !c.Scopes.TryGetValue(Resource, out var definition) || definition.Default != 1
            || !definition.Supports.Order().SequenceEqual(new[] { 1, 2, 3, 4, 5 })) throw Error("RESOURCE_CATALOG_UNAVAILABLE");
        var scope = actor.DataScopes.GetValueOrDefault(Resource, 1);
        if (scope is < 1 or > 5) throw Error("RESOURCE_SCOPE_UNAVAILABLE");
        return scope;
    }
    private async Task<bool> Visible(UserPermissionContext actor, Head head, CancellationToken ct)
    {
        var scope = ResourceScope(actor);
        if (scope == 1) return head.OwnerId == actor.UserId;
        if (scope == 5) return true;
        if (head.DeptId == null) return false;
        var department = await PostgreSqlOrganizationV1.Healthy(db).SingleOrDefaultAsync(x => x.Id == head.DeptId, ct);
        if (department == null) return false;
        return scope switch
        {
            2 => actor.DeptId == department.Id,
            3 => actor.DeptPath.Length > 0 && department.Path.StartsWith(actor.DeptPath, StringComparison.Ordinal),
            4 => actor.CustomDeptIds.GetValueOrDefault(Resource)?.Contains(department.Id) == true,
            _ => false
        };
    }
    private async Task ReadCapability(UserPermissionContext actor, CancellationToken ct, string menu = "oa-designer")
    {
        // Each controlled entry requires its actual enabled menu. Removing a write action alone does not revoke read.
        if (!actor.MenuKeys.Contains(menu) || !await db.Sys_Menus.AnyAsync(m => m.Enable && m.MenuKey == menu, ct))
            throw Error("OBJECT_FORBIDDEN", 403);
    }
    private async Task<FieldPermissionDecisionV1> Permission(UserPermissionContext actor, Head head, bool write, bool creating, CancellationToken ct, string readMenu = "oa-designer")
    {
        await ReadCapability(actor, ct, readMenu);
        if (!await Visible(actor, head, ct)) throw Error("DEFINITION_NOT_FOUND", 404);
        if (write)
        {
            await Action(actor, "oa-designer", "form-save", ct);
            var policy = await ReadPolicy(ct) ?? throw Error("AUTHORING_POLICY_REQUIRED");
            if (!policy.Enabled || policy.Profile != "DIRECT_FORM_AUTHORING_V1") throw Error("AUTHORING_POLICY_DISABLED", 403);
            if (policy.ForbiddenRoles.Intersect(actor.RoleIds).Any()) throw Error("AUTHORING_SOD_FORBIDDEN", 403);
        }
        var source = await new CurrentFieldPermissionSourceReaderV1(db, http, catalog).ReadAsync(Resource, ct);
        if (!source.Fields.Order().SequenceEqual(Fields.Order())) throw Error("FIELD_CATALOG_UNAVAILABLE");
        var uses = new[] { FieldPermissionUseV1.Read, FieldPermissionUseV1.Write };
        var maps = Fields.SelectMany(f => uses.Select(u => new FieldPermissionMappingV1(f, u, "draft." + f, true, [])));
        // Direct human authoring, tenant-local objects, no impersonation/delegation/projection or external PII source.
        // Content fields (including SchemaJson as an opaque unit) are governed by this actual registered policy.
        var yes = FieldPermissionCheckV1.Allowed; var na = FieldPermissionCheckV1.NotApplicable;
        var constraints = Fields.SelectMany(f => uses.Select(u => new FieldPermissionConstraintsV1(f, u,
            u == FieldPermissionUseV1.Write && !write ? FieldPermissionCheckV1.Denied : yes, yes, yes, na, yes, yes, yes)));
        var decision = source.Evaluate(source.Versions(Mapping), Mapping, maps, constraints);
        if (!decision.IsAvailable) throw Error("FIELD_AUTHORITY_UNAVAILABLE");
        return decision;
    }

    private static Dictionary<string,string?> Values(Head head)=>new()
    { ["Name"]=head.Name,["SchemaJson"]=head.SchemaJson,["Category"]=head.Category,["SubCategory"]=head.SubCategory };
    private static FormAuthoringViewV2 View(Head head,FieldPermissionDecisionV1 permission)=>new(head.Id,head.DraftId,head.Key,head.Version.ToString("D"),
        Values(head).Where(x=>permission.Fields[x.Key].CanRead).ToDictionary(),Fields.Where(x=>permission.Fields[x].CanWrite).ToArray());
    private async Task<bool> CanEdit(UserPermissionContext actor, CancellationToken ct)
    {
        var policy = await ReadPolicy(ct);
        return policy is { Enabled: true, Profile: "DIRECT_FORM_AUTHORING_V1" } && !policy.ForbiddenRoles.Intersect(actor.RoleIds).Any()
            && actor.ActionKeys.Contains("oa-designer:form-save") && await
            (from a in db.Sys_MenuActions join m in db.Sys_Menus on a.MenuId equals m.MenuId where m.Enable && m.MenuKey == "oa-designer" && a.ActionCode == "form-save" select a).AnyAsync(ct);
    }

    public async Task<FormAuthoringViewV2> ReadAsync(string key,CancellationToken ct=default)
    {
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);await Root(ct);var actor=await Actor(ct);
        var head=await Heads().SingleOrDefaultAsync(x=>x.Key==key,ct)??throw Error("DEFINITION_NOT_FOUND",404);
        var permission=await Permission(actor,head,await CanEdit(actor,ct),false,ct);
        if((await Actor(ct)).UserId!=actor.UserId)throw Error("REAUTH_REQUIRED",401);CompletionClock();return View(head,permission);
    }
    public async Task<FormAuthoringViewV2[]> ListAsync(int page=1,CancellationToken ct=default)
    {
        if(page<1||page>100000)throw Error("PAGE_INVALID",400);
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);await Root(ct);var actor=await Actor(ct);
        await ReadCapability(actor,ct);var query=Heads();var scope=ResourceScope(actor);
        if(scope==1)query=query.Where(h=>h.OwnerId==actor.UserId);
        else if(scope is 2 or 3 or 4)
        {
            var departments=(await PostgreSqlOrganizationV1.Healthy(db).ToArrayAsync(ct)).Where(d=>scope switch
            {2=>d.Id==actor.DeptId,3=>actor.DeptPath.Length>0&&d.Path.StartsWith(actor.DeptPath,StringComparison.Ordinal),
             _=>actor.CustomDeptIds.GetValueOrDefault(Resource)?.Contains(d.Id)==true}).Select(d=>d.Id).ToArray();
            query=query.Where(h=>h.DeptId!=null&&departments.Contains(h.DeptId.Value));
        }
        var result=new List<FormAuthoringViewV2>();
        foreach(var head in await query.OrderBy(h=>h.Key).Skip((page-1)*100).Take(100)
            .Select(h=>new Head(h.Id,h.DraftId,h.Key,h.OwnerId,h.DeptId,h.Version,h.Name,"",h.Category,h.SubCategory,h.SavedBy)).ToArrayAsync(ct))
        {
            var permission=await Permission(actor,head,false,false,ct);var view=View(head,permission);view.Fields.Remove("SchemaJson");result.Add(view);
        }
        if((await Actor(ct)).UserId!=actor.UserId)throw Error("REAUTH_REQUIRED",401);CompletionClock();return result.ToArray();
    }
    private sealed record Command(string Id,string? Expected,FormAuthoringBodyV2 Body,string Digest,byte[] Raw,string? Correlation);
    private static Command Parse(byte[] raw)
    {
        try
        {
            using var doc=WorkflowCommandJsonV1.Parse(raw);var root=doc.RootElement;
            if(root.ValueKind!=JsonValueKind.Object||root.EnumerateObject().Any(p=>p.Name is not ("commandId" or "expectedVersion" or "body" or "clientCorrelationId"))
                ||!root.TryGetProperty("expectedVersion",out var version))throw new JsonException();
            var id=root.GetProperty("commandId").GetString();
            if(id==null||!Regex.IsMatch(id,"\\A[A-Za-z0-9_.-]{1,100}\\z"))throw new JsonException();
            var expected=version.ValueKind==JsonValueKind.Null?null:version.GetString();
            if(expected!=null&&!Guid.TryParseExact(expected,"D",out _))throw new JsonException();
            var correlation=root.TryGetProperty("clientCorrelationId",out var c)?c.GetString():null;
            if(correlation?.Length>100)throw new JsonException();
            var body=root.GetProperty("body");var typed=body.Deserialize<FormAuthoringBodyV2>(Json)??throw new JsonException();
            if(string.IsNullOrWhiteSpace(typed.Name)||typed.Name.Length>200||typed.Name.Contains('\0')||typed.SchemaJson==null
                ||typed.Category?.Length>100||typed.SubCategory?.Length>100||typed.Category?.Contains('\0')==true||typed.SubCategory?.Contains('\0')==true)throw new JsonException();
            return new(id,expected,typed,WorkflowCommandJsonV1.Digest(body,expected),raw.ToArray(),correlation);
        }
        catch(Exception e)when(e is JsonException or InvalidOperationException or KeyNotFoundException){throw Error("COMMAND_SHAPE_INVALID",400);}
    }
    private sealed record Saved(string BodyDigest,string ResultJson);
    private Task<Saved?> Original(Guid actor,string key,string command,CancellationToken ct)=>db.Database.SqlQuery<Saved>($"""
        SELECT body_digest AS "BodyDigest",result_json AS "ResultJson" FROM cp6_form_authoring_v2.commands
        WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND actor_id={actor}
          AND operation='FORM_SAVE_DRAFT' AND resource_key={key} AND command_id={command}
        """).SingleOrDefaultAsync(ct);
    private async Task<FormAuthoringResultV2> ProjectOriginal(UserPermissionContext actor,string key,Saved original,CancellationToken ct)
    {
        var head=await Heads().SingleOrDefaultAsync(x=>x.Key==key,ct)??throw Error("DEFINITION_NOT_FOUND",404);
        var permission=await Permission(actor,head,false,false,ct);
        var result=JsonSerializer.Deserialize<FormAuthoringResultV2>(original.ResultJson,Json)!;
        return result with {Result=result.Result! with {Fields=result.Result!.Fields.Where(x=>permission.Fields[x.Key].CanRead).ToDictionary(),WritableFields=[]}};
    }
    public async Task<FormAuthoringResultV2> OriginalAsync(string key,string command,CancellationToken ct=default)
    {
        if(!Regex.IsMatch(command,"\\A[A-Za-z0-9_.-]{1,100}\\z"))throw Error("COMMAND_SHAPE_INVALID",400);
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);await Root(ct);var actor=await Actor(ct);
        var head=await Heads().SingleOrDefaultAsync(x=>x.Key==key,ct)??new Head(Guid.Empty,Guid.Empty,key,actor.UserId,actor.DeptId,Guid.Empty,"","",null,null,actor.UserId);
        await Permission(actor,head,false,false,ct);var previous=await Original(actor.UserId,key,command,ct);
        FormAuthoringResultV2 result=previous==null?new(command,"NOT_OBSERVED",null,null,null,null,null):await ProjectOriginal(actor,key,previous,ct);
        if((await Actor(ct)).UserId!=actor.UserId)throw Error("REAUTH_REQUIRED",401);CompletionClock();return result;
    }
    public async Task<FormAuthoringResultV2> SaveAsync(string key,byte[] raw,CancellationToken ct=default)
    {
        if(string.IsNullOrWhiteSpace(key)||key.Length>100||key.Contains('\0'))throw Error("DEFINITION_KEY_INVALID",400);
        var command=Parse(raw);Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var committing=false;
        try
        {
            await Root(ct);var actor=await Actor(ct);var original=await Original(actor.UserId,key,command.Id,ct);
            if(original!=null)
            {
                var result=await ProjectOriginal(actor,key,original,ct);
                if(original.BodyDigest!=command.Digest)throw Error("COMMAND_BODY_CONFLICT",409);
                if((await Actor(ct)).UserId!=actor.UserId)throw Error("REAUTH_REQUIRED",401);CompletionClock();return result;
            }
            var lease=await fence.AcquireAsync(ct);await Ready(ct);
            var old=await Heads().SingleOrDefaultAsync(x=>x.Key==key,ct);
            var head=old??new Head(Guid.NewGuid(),Guid.NewGuid(),key,actor.UserId,actor.DeptId,Guid.Empty,"","",null,null,actor.UserId);
            var permission=await Permission(actor,head,true,old==null,ct);
            if(old!=null&&command.Expected==null)throw Error("DEFINITION_EXISTS",409);
            if(old==null&&command.Expected!=null||old!=null&&command.Expected!=old.Version.ToString("D"))throw Error("DEFINITION_CHANGED",409);
            var b=command.Body;var next=head with {Name=b.Name,SchemaJson=b.SchemaJson,Category=b.Category,SubCategory=b.SubCategory,SavedBy=actor.UserId};
            CheckWrites(old,next,permission);
            try{_ = FormSchemaValidationV1.Parse(b.SchemaJson);}
            catch(InvalidOperationException e){throw Error(e.Message,422);}
            if(old==null)
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO cp6_form_authoring_v2.heads(tenant_id,environment_id,id,form_key,owner_id,owner_dept_id,draft_id)
                    VALUES({db.CurrentTenantId},{fence.EnvironmentId},{head.Id},{key},{actor.UserId},{actor.DeptId},{head.DraftId})
                    """,ct);
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO cp6_form_authoring_v2.drafts(tenant_id,environment_id,head_id,id,name,schema_json,category,sub_category,saved_by)
                    VALUES({db.CurrentTenantId},{fence.EnvironmentId},{head.Id},{head.DraftId},{b.Name},{b.SchemaJson},{b.Category},{b.SubCategory},{actor.UserId})
                    """,ct);
            }
            else
            {
                var changed=await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE cp6_form_authoring_v2.drafts SET name={b.Name},schema_json={b.SchemaJson},category={b.Category},sub_category={b.SubCategory},saved_by={actor.UserId}
                    WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND head_id={head.Id} AND row_version={old.Version}
                    """,ct);
                if(changed!=1)throw Error("DEFINITION_CHANGED",409);
            }
            next=await Heads().SingleAsync(x=>x.Key==key,ct);var at=DateTimeOffset.UtcNow;
            var snapshot=new FormAuthoringViewV2(next.Id,next.DraftId,key,next.Version.ToString("D"),Values(next),[]);
            var saved=new FormAuthoringResultV2(command.Id,"SUCCEEDED",next.Id,snapshot.Version,snapshot,at,command.Correlation);
            var text=JsonSerializer.Serialize(saved,Json);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_form_authoring_v2.commands(tenant_id,environment_id,actor_id,operation,resource_key,command_id,canonical_version,body_digest,raw_body,raw_digest,result_json,recorded_at)
                VALUES({db.CurrentTenantId},{fence.EnvironmentId},{actor.UserId},'FORM_SAVE_DRAFT',{key},{command.Id},'WFC-CJ-1',{command.Digest},{command.Raw},{MainOwnerOperationJournalV1.Digest(command.Raw)},{text},{at})
                """,ct);
            var finalActor=await Actor(ct);if(finalActor.UserId!=actor.UserId)throw Error("REAUTH_REQUIRED",401);
            var finalPermission=await Permission(finalActor,head,true,old==null,ct);CheckWrites(old,next,finalPermission);
            var policy=await ReadPolicy(ct);
            var evidence=JsonSerializer.Serialize(new {ActualActor=actor.UserId,EffectiveActor=actor.UserId,Policy=policy,Scope=lease.Snapshot,
                CheckedAtUtc=DateTimeOffset.UtcNow,Action="oa-designer:form-save",finalActor.RoleIds,ActorDepartment=finalActor.DeptId,finalActor.DeptPath,
                ResourceScope=finalActor.DataScopes.GetValueOrDefault(Resource,1),CustomDepartments=finalActor.CustomDeptIds.GetValueOrDefault(Resource),
                FieldVersions=finalPermission.Input.Versions,FieldRows=finalPermission.Input.Rows,FieldAccess=finalPermission.Fields.ToDictionary(x=>x.Key,x=>x.Value.Access),
                next.OwnerId,OwnerDepartment=next.DeptId,command.Digest,RawDigest=MainOwnerOperationJournalV1.Digest(command.Raw)},Json);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_form_authoring_v2.audit(tenant_id,environment_id,actor_id,resource_key,command_id,evidence_json,recorded_at)
                VALUES({db.CurrentTenantId},{fence.EnvironmentId},{actor.UserId},{key},{command.Id},{evidence},{at})
                """,ct);
            if(await Heads().SingleAsync(x=>x.Key==key,ct)!=next)throw Error("DEFINITION_CHANGED",409);
            await Ready(ct);await fence.ValidateAsync(lease,ct);CompletionClock();ct.ThrowIfCancellationRequested();
            committing=true;await tx.CommitAsync(ct);return saved with {Result=View(next,finalPermission)};
        }
        catch(Exception error)
        {
            try{await tx.RollbackAsync(CancellationToken.None);}
            catch(Exception rollback){throw new ScopeAuthorizationExceptionV1("COMMAND_OUTCOME_UNKNOWN",503,new AggregateException(error,rollback));}
            if(committing)throw new ScopeAuthorizationExceptionV1("COMMAND_OUTCOME_UNKNOWN",503,error);
            if(error is Npgsql.PostgresException {SqlState:"23505", TableName:"heads"}
                or Npgsql.PostgresException {SqlState:"23514", MessageText:"DEFINITION_IDENTITY_CONFLICT"})throw Error("DEFINITION_IDENTITY_CONFLICT",409);
            if(error is Npgsql.NpgsqlException or CurrentPermissionUnavailableException)throw new ScopeAuthorizationExceptionV1("AUTHORITY_FENCE_UNAVAILABLE",503,error);
            throw;
        }
    }
    private static void CheckWrites(Head? old,Head next,FieldPermissionDecisionV1 permission)
    {
        var prior=old==null?null:Values(old);
        foreach(var (field,value) in Values(next))
            if(!permission.Fields[field].CanRead||(prior==null||prior[field]!=value)&&!permission.Fields[field].CanWrite)throw Error("FIELD_WRITE_FORBIDDEN",403);
    }
}
