using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.Contracts.Wire;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public sealed partial class PersonalFormOwnerV2(CP6Context db, IHttpContextAccessor http, ScopeAuthorizationFenceV1 fence,
    Func<CancellationToken,Task<Guid>> currentActor, FlowAuthoringOwnerV2 flows)
{
    public const string Resource = "workflow-personal-form";
    public static readonly string[] Fields = ["Title", "DataJson"];
    internal const string Create = "FORM_DRAFT_CREATE", Save = "FORM_DRAFT_SAVE", Delete = "FORM_DRAFT_DELETE",
        Restore = "FORM_DRAFT_RESTORE", Rebase = "FORM_DRAFT_REBASE", Direct = "FORM_DIRECT_SUBMIT", Submit = "FORM_DRAFT_SUBMIT";
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = false };
    private static ScopeAuthorizationExceptionV1 Error(string code, int status = 503) => new(code, status);
    private static T Parse<T>(string value) => JsonSerializer.Deserialize<T>(value, Json)!;
    private static string Encode(object value) => JsonSerializer.Serialize(value, Json);
    internal Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;
    internal Func<AuthorizationCatalogSnapshotV1> Catalog { get; set; } = AuthorizationCatalogV1.Capture;
    internal void Entry()
    {
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction != null || db.ChangeTracker.HasChanges()
            || System.Transactions.Transaction.Current != null) throw Error("AUTHORITY_FENCE_UNAVAILABLE");
        _ = fence.EnvironmentId;
    }
    private async Task Ready(CancellationToken ct)
    {
        if (!await db.Database.SqlQueryRaw<bool>("SELECT cp6_personal_form_v2.ready() AS \"Value\"").SingleAsync(ct)) throw Error("FORM_WRITER_COVERAGE_UNAVAILABLE");
    }
    internal async Task<ScopeAuthorizationLeaseV1> ReadLease(CancellationToken ct)
    {
        await fence.Root(ct);var value=await fence.ReadLocked(ct)??throw Error("SCOPE_MAPPING_REQUIRED");
        return new(db,db.Database.CurrentTransaction!.TransactionId,value);
    }
    private async Task WriteReady(ScopeAuthorizationLeaseV1 lease,CancellationToken ct)
    {
        if(!lease.Snapshot.Enabled||lease.Snapshot.WriterProfile!=ScopeAuthorizationFenceV1.NativeWriterProfile)throw Error("SCOPE_MAPPING_DISABLED");
        await Ready(ct);
    }
    internal async Task<UserPermissionContext> Actor(CancellationToken ct)
    {
        var id = await currentActor(ct);
        if (id != CurrentPermissionContext.RequireCurrentUser(db, http) || http.HttpContext!.User.HasClaim(x => x.Type == "impersonator_id")) throw Error("REAUTH_REQUIRED", 401);
        var exp = http.HttpContext.User.FindFirst("exp")?.Value;
        if (exp != null && (!long.TryParse(exp,out var seconds) || seconds <= Clock().ToUnixTimeSeconds())
            || !await db.Sys_Tenants.AsNoTracking().AnyAsync(t => t.Id == db.CurrentTenantId && t.Enable && (t.ExpireDate == null || t.ExpireDate > DateTime.Now), ct)
            || !await db.Sys_Users.AsNoTracking().AnyAsync(u => u.Id == id && u.Enable && (u.LockedUntil == null || u.LockedUntil <= DateTime.Now), ct)) throw Error("REAUTH_REQUIRED", 401);
        return await PostgreSqlPermissionReader.ReadAsync(db, id, ct);
    }
    internal async Task Action(UserPermissionContext actor, string action, CancellationToken ct, string menu = "oa-form-catalog")
    {
        if (!actor.MenuKeys.Contains(menu) || !actor.ActionKeys.Contains(menu + ":" + action)
            || !await (from a in db.Sys_MenuActions join m in db.Sys_Menus on a.MenuId equals m.MenuId
                where m.Enable && m.MenuKey == menu && a.ActionCode == action select a).AnyAsync(ct)) throw Error("ACTION_FORBIDDEN", 403);
    }
    private sealed record Head(Guid Id, string Key);
    private IQueryable<Head> Heads() => db.Database.SqlQuery<Head>($"""
        SELECT id AS "Id",form_key AS "Key" FROM cp6_form_authoring_v2.heads
        WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId}
        """);
    private sealed record PolicyRow(Guid Version, string Body);
    private async Task<PersonalFormPolicyV2?> Policy(Guid head, CancellationToken ct)
    {
        var row = await db.Database.SqlQuery<PolicyRow>($"""
            SELECT row_version AS "Version",body AS "Body" FROM cp6_personal_form_v2.policy
            WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND head_id={head}
            """).SingleOrDefaultAsync(ct);
        return row == null ? null : Parse<PersonalFormPolicyV2>(row.Body) with { Version = row.Version };
    }
    private async Task<FormPublicationV2> Publication(Guid head, Guid version, CancellationToken ct)
    {
        var json = await db.Database.SqlQuery<string>($"""
            SELECT snapshot_json AS "Value" FROM cp6_form_authoring_v2.versions
            WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND head_id={head} AND id={version}
            """).SingleOrDefaultAsync(ct) ?? throw Error("FORM_VERSION_NOT_FOUND", 404);
        return Parse<FormPublicationV2>(json);
    }
    private async Task<Dictionary<string,string>> Mask(UserPermissionContext actor, Guid head, FormPublicationV2 publication, CancellationToken ct)
    {
        var policy = await Policy(head,ct) ?? throw Error("FORM_USE_POLICY_REQUIRED", 403);
        var grants = policy.Roles.Where(x => actor.RoleIds.Contains(x.RoleId)).ToArray();
        if (grants.Length == 0) throw Error("FORM_NOT_FOUND", 404);
        var catalog = Catalog();
        if (!catalog.IsSealed || !catalog.Fields.TryGetValue(Resource, out var fields) || !fields.Select(f => f.Name).Order().SequenceEqual(Fields.Order())) throw Error("FIELD_CATALOG_UNAVAILABLE");
        var permission = actor.FieldPerms.GetValueOrDefault(Resource) ?? [];
        if (permission.Any(x => !Fields.Contains(x.Key) || x.Value is < 1 or > 3)) throw Error("FIELD_AUTHORITY_UNAVAILABLE");
        var access = permission.GetValueOrDefault("DataJson", 1);
        static int Rank(string value) => value switch { "edit" => 1, "readonly" => 2, "hidden" => 3, _ => throw new InvalidOperationException("FORM_POLICY_INVALID") };
        return PersonalFormValuesV2.Schema(publication.Fields["SchemaJson"]!).Fields.ToDictionary(f => f.Name,
            f => Math.Max(access, grants.Min(g => Rank(g.Fields.GetValueOrDefault(f.Name, "edit")))) switch { 1 => "edit", 2 => "readonly", _ => "hidden" }, StringComparer.Ordinal);
    }
    private static bool TitleReadable(UserPermissionContext actor) => (actor.FieldPerms.GetValueOrDefault(Resource)?.GetValueOrDefault("Title",1) ?? 1) < 3;
    private static string TitleAccess(UserPermissionContext actor) => (actor.FieldPerms.GetValueOrDefault(Resource)?.GetValueOrDefault("Title",1) ?? 1) switch { 1=>"edit", 2=>"readonly", _=>"hidden" };
    private static void TitleWritable(UserPermissionContext actor, string? before, string? after)
    {
        var access = actor.FieldPerms.GetValueOrDefault(Resource)?.GetValueOrDefault("Title",1) ?? 1;
        if (access == 3 && after != null || access != 1 && after != before) throw Error("FIELD_WRITE_FORBIDDEN",403);
    }
    internal sealed record Command<T>(string Id,string? Expected,T Body,string Digest,byte[] Raw,string? Correlation);
    internal static Command<T> CommandInput<T>(byte[] raw, bool versionRequired)
    {
        try
        {
            using var doc = WorkflowCommandJsonV1.Parse(raw); var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Any(p => p.Name is not ("commandId" or "expectedVersion" or "body" or "clientCorrelationId"))) throw new JsonException();
            var id = root.GetProperty("commandId").GetString(); var expected = root.GetProperty("expectedVersion").GetString();
            if (id == null || !Regex.IsMatch(id,"\\A[A-Za-z0-9_.-]{1,100}\\z")
                || versionRequired && (!Guid.TryParseExact(expected,"D",out var token) || token == Guid.Empty) || !versionRequired && expected != null) throw new JsonException();
            var body = root.GetProperty("body"); var value = body.Deserialize<T>(Json) ?? throw new JsonException();
            var correlation = root.TryGetProperty("clientCorrelationId",out var c) ? c.GetString() : null;
            if (correlation?.Length > 100) throw new JsonException();
            return new(id,expected,value,WorkflowCommandJsonV1.Digest(body,expected),raw.ToArray(),correlation);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException) { throw Error("COMMAND_SHAPE_INVALID",400); }
    }
    private sealed record StoredCommand(Guid Id, Guid Actor, string Operation, string Resource, string Key, string Digest, string Result,
        Guid? Draft, Guid? Data, Guid? Root, bool Closed);
    private static void Match(StoredCommand old,string digest)
    {
        if(old.Closed)throw Error("COMMAND_CLOSED",409);
        if(old.Digest!=digest)throw Error("COMMAND_BODY_CONFLICT",409);
    }
    private Task<StoredCommand?> Original(Guid actor, string operation, string resource, string command, CancellationToken ct) => db.Database.SqlQuery<StoredCommand>($"""
        SELECT id AS "Id",actor_id AS "Actor",operation AS "Operation",resource_key AS "Resource",command_id AS "Key",digest AS "Digest",result_json AS "Result",
          draft_id AS "Draft",form_data_id AS "Data",root_id AS "Root",closed AS "Closed" FROM cp6_personal_form_v2.commands
        WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND actor_id={actor} AND operation={operation} AND resource_key={resource} AND command_id={command} AND completed
        """).SingleOrDefaultAsync(ct);
    private Task BeginCommand<T>(Guid id, Guid actor, string operation, string resource, Command<T> command,
        Guid? draft, Guid? data, Guid? root, Guid? task, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO cp6_personal_form_v2.commands(id,tenant_id,environment_id,actor_id,operation,resource_key,command_id,digest,raw_body,expected_version,correlation,draft_id,form_data_id,root_id,task_id)
        VALUES({id},{db.CurrentTenantId},{fence.EnvironmentId},{actor},{operation},{resource},{command.Id},{command.Digest},{command.Raw},{command.Expected},{command.Correlation},{draft},{data},{root},{task})
        """,ct);
    private async Task<PersonalFormResultV2> Complete<T>(Guid id, Command<T> command, Guid resource, string? version, object value, object audit, CancellationToken ct)
    {
        var result = new PersonalFormResultV2(command.Id,"SUCCEEDED",resource,version,JsonSerializer.SerializeToElement(value,Json),Clock(),command.Correlation);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_personal_form_v2.commands SET result_json={Encode(result)},audit_json={Encode(audit)},completed=true WHERE id={id}
            """,ct);
        return result;
    }
    internal async Task Final(UserPermissionContext actor, ScopeAuthorizationLeaseV1 lease, CancellationToken ct,bool write=true)
    {
        if ((await Actor(ct)).UserId != actor.UserId) throw Error("REAUTH_REQUIRED",401);
        await fence.ValidateAsync(lease,ct); if(write)await Ready(ct); ct.ThrowIfCancellationRequested();
    }
    internal async Task Rollback(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx, bool committing, Exception error)
    {
        // PostgreSQL's structured rejection of COMMIT (for example a deferred constraint)
        // proves rollback. A transport failure or 40003 still has an unknown outcome.
        if(committing && error is Npgsql.PostgresException pg && pg.SqlState!="40003"
            && pg.SqlState[..2] is "22" or "23" or "40" or "42")
        {db.ChangeTracker.Clear();return;}
        try { await tx.RollbackAsync(CancellationToken.None); }
        catch (Exception rollback) { throw new ScopeAuthorizationExceptionV1("COMMAND_OUTCOME_UNKNOWN",503,new AggregateException(error,rollback)); }
        finally { db.ChangeTracker.Clear(); }
        if (committing) throw new ScopeAuthorizationExceptionV1("COMMAND_OUTCOME_UNKNOWN",503,error);
    }
    private static string ProjectSchema(string schema, Dictionary<string,string> mask)
    {
        var root = JsonNode.Parse(schema)!.AsObject(); var fields = PersonalFormValuesV2.SchemaProperty(root,"fields")!.AsArray();
        for (var i=fields.Count-1;i>=0;i--) if(mask[PersonalFormValuesV2.SchemaProperty(fields[i]!.AsObject(),"name")!.GetValue<string>()]=="hidden") fields.RemoveAt(i);
        // Rule expressions can mention hidden fields. The server performs authoritative rules;
        // do not disclose hidden field identifiers through a client-side rule bundle.
        if(mask.Values.Contains("hidden"))
            foreach(var key in root.Select(x=>x.Key).Where(x=>string.Equals(x,"rules",StringComparison.OrdinalIgnoreCase)).ToArray())root.Remove(key);
        return root.ToJsonString();
    }
}
