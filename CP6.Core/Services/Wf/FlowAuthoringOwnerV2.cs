using System.Text;
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
public sealed record FlowAuthoringBodyV2(string Name, string SchemaJson, string? FormKey, string? FunctionId, string? FlowCode);
public sealed record FlowAuthoringViewV2(Guid DefinitionId, Guid DraftId, string Key, string Version,
    Dictionary<string, string?> Fields, string[] WritableFields, string[] Diagnostics, bool Enabled = false, bool Published = false, int VersionNumber = 1);
public sealed record WorkflowCommandResultV1(string CommandId, string Outcome, Guid? ResourceId, string? ResourceVersion,
    FlowAuthoringViewV2? Result, DateTimeOffset? RecordedAtUtc, string? CorrelationId);

/// <summary>FLOW definition Owner. Publication remains separate from activation and executable runtime projection.</summary>
public sealed partial class FlowAuthoringOwnerV2
{
    public const string Resource = "workflow-definition", Operation = "FLOW_SAVE_DRAFT";
    public static readonly string[] Fields = ["Name", "SchemaJson", "FormKey", "FunctionId", "FlowCode"];
    private const string Mapping = "Workflow.FlowAuthoring.v2.1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = false };
    private readonly CP6Context db;
    private readonly IHttpContextAccessor http;
    private readonly ScopeAuthorizationFenceV1 fence;
    private readonly Func<CancellationToken, Task<Guid>> currentActor;
    private readonly Func<AuthorizationCatalogSnapshotV1> catalog;
    private readonly IEnumerable<IServiceTaskExecutor> executors;
    private readonly IEnumerable<IWfConnector> connectors;
    private DateTime? currentTenantExpiry;
    public FlowAuthoringOwnerV2(CP6Context db, IHttpContextAccessor http, ScopeAuthorizationFenceV1 fence,
        Func<CancellationToken, Task<Guid>> currentActor, IEnumerable<IServiceTaskExecutor> executors, IEnumerable<IWfConnector> connectors)
        : this(db, http, fence, currentActor, executors, connectors, AuthorizationCatalogV1.Capture) { }
    internal FlowAuthoringOwnerV2(CP6Context db, IHttpContextAccessor http, ScopeAuthorizationFenceV1 fence,
        Func<CancellationToken, Task<Guid>> currentActor, IEnumerable<IServiceTaskExecutor> executors, IEnumerable<IWfConnector> connectors,
        Func<AuthorizationCatalogSnapshotV1> catalog)
    { this.db = db; this.http = http; this.fence = fence; this.currentActor = currentActor; this.catalog = catalog; this.executors = executors; this.connectors = connectors; }
    private static ScopeAuthorizationExceptionV1 Error(string code, int status = 503) => new(code, status);
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
    private sealed record Head(Guid Id, Guid DraftId, string Key, Guid OwnerId, Guid? DeptId, Guid Version,
        string Name, string SchemaJson, string? FormKey, string? FunctionId, string? FlowCode, int Status = 0, int Number = 1, Guid? SavedBy = null);
    private IQueryable<Head> Heads() => db.Database.SqlQuery<Head>($"""
        SELECT h.id AS "Id",h.draft_id AS "DraftId",h.flow_key AS "Key",h.owner_id AS "OwnerId",h.owner_dept_id AS "DeptId",
          d.row_version AS "Version",d.name AS "Name",d.schema_json AS "SchemaJson",d.form_key AS "FormKey",
          h.function_id AS "FunctionId",h.flow_code AS "FlowCode",d.status AS "Status",d.version_number AS "Number",d.saved_by AS "SavedBy" FROM cp6_workflow_authoring_v2.heads h
          JOIN cp6_workflow_authoring_v2.drafts d ON d.tenant_id=h.tenant_id AND d.environment_id=h.environment_id AND d.head_id=h.id AND d.id=h.draft_id
          WHERE h.tenant_id={db.CurrentTenantId} AND h.environment_id={fence.EnvironmentId}
        """);
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
    private async Task ReadCapability(UserPermissionContext actor, CancellationToken ct)
    {
        // The actual enabled designer menu is this resource's read capability. Removing edit alone does not revoke read.
        if (!actor.MenuKeys.Contains("oa-designer") || !await db.Sys_Menus.AnyAsync(m => m.Enable && m.MenuKey == "oa-designer", ct))
            throw Error("OBJECT_FORBIDDEN", 403);
    }
    private async Task<FieldPermissionDecisionV1> Permission(UserPermissionContext actor, Head head, bool write, bool creating, CancellationToken ct)
    {
        await ReadCapability(actor, ct);
        if (!await Visible(actor, head, ct)) throw Error("DEFINITION_NOT_FOUND", 404);
        if (write)
        {
            await Action(actor, "oa-designer", creating ? "add" : "edit", ct);
            var policy = await ReadPolicy(ct) ?? throw Error("AUTHORING_POLICY_REQUIRED");
            if (!policy.Enabled || policy.Profile != "DIRECT_AUTHORING_V1") throw Error("AUTHORING_POLICY_DISABLED", 403);
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
    private static Dictionary<string, string?> Values(Head head) => new()
    { ["Name"] = head.Name, ["SchemaJson"] = head.SchemaJson, ["FormKey"] = head.FormKey, ["FunctionId"] = head.FunctionId, ["FlowCode"] = head.FlowCode };
    private static FlowAuthoringViewV2 View(Head head, FieldPermissionDecisionV1 permission, string[] diagnostics) => new(
        head.Id, head.DraftId, head.Key, head.Version.ToString("D"), Values(head).Where(x => permission.Fields[x.Key].CanRead).ToDictionary(),
        head.Status == 0 ? Fields.Where(x => permission.Fields[x].CanWrite).ToArray() : [], permission.Fields["SchemaJson"].CanRead ? diagnostics : [], false, head.Status == 1, head.Number);
    private async Task<bool> CanEdit(UserPermissionContext actor, CancellationToken ct)
    {
        var policy = await ReadPolicy(ct);
        return policy is { Enabled: true, Profile: "DIRECT_AUTHORING_V1" } && !policy.ForbiddenRoles.Intersect(actor.RoleIds).Any()
            && actor.ActionKeys.Contains("oa-designer:edit") && await
            (from a in db.Sys_MenuActions join m in db.Sys_Menus on a.MenuId equals m.MenuId where m.Enable && m.MenuKey == "oa-designer" && a.ActionCode == "edit" select a).AnyAsync(ct);
    }
    public async Task<FlowAuthoringViewV2> ReadAsync(string key, CancellationToken ct = default)
    {
        Entry(); await using var tx = await db.Database.BeginTransactionAsync(ct); await Root(ct);
        var actor = await Actor(ct); var head = await Heads().SingleOrDefaultAsync(x => x.Key == key, ct) ?? throw Error("DEFINITION_NOT_FOUND", 404);
        var permission = await Permission(actor, head, await CanEdit(actor, ct), false, ct);
        if (head.Status != 0) throw Error("DRAFT_REQUIRED", 404);
        if ((await Actor(ct)).UserId != actor.UserId) throw Error("REAUTH_REQUIRED", 401);
        return View(head, permission, []);
    }
    public async Task<FlowAuthoringViewV2[]> ListAsync(int page = 1, CancellationToken ct = default)
    {
        if (page < 1 || page > 100000) throw Error("PAGE_INVALID", 400);
        Entry(); await using var tx = await db.Database.BeginTransactionAsync(ct); await Root(ct); var actor = await Actor(ct);
        // Verify catalog/read admission even for an empty page. Scope filtering precedes pagination.
        await ReadCapability(actor, ct);
        var query = Heads(); var scope = ResourceScope(actor);
        if (scope == 1) query = query.Where(h => h.OwnerId == actor.UserId);
        else if (scope is 2 or 3 or 4)
        {
            var departments = (await PostgreSqlOrganizationV1.Healthy(db).ToArrayAsync(ct)).Where(d => scope switch
            { 2 => d.Id == actor.DeptId, 3 => actor.DeptPath.Length > 0 && d.Path.StartsWith(actor.DeptPath, StringComparison.Ordinal),
              _ => actor.CustomDeptIds.GetValueOrDefault(Resource)?.Contains(d.Id) == true }).Select(d => d.Id).ToArray();
            query = query.Where(h => h.DeptId != null && departments.Contains(h.DeptId.Value));
        }
        var result = new List<FlowAuthoringViewV2>();
        var pageRows = await query.OrderBy(h => h.Key).Skip((page - 1) * 100).Take(100)
            .Select(h => new Head(h.Id, h.DraftId, h.Key, h.OwnerId, h.DeptId, h.Version, h.Name, "", h.FormKey, h.FunctionId, h.FlowCode, h.Status, h.Number, h.SavedBy)).ToListAsync(ct);
        foreach (var head in pageRows)
        {
            var permission = await Permission(actor, head, false, false, ct);
            var view = View(head, permission, []); view.Fields.Remove("SchemaJson"); result.Add(view);
        }
        if ((await Actor(ct)).UserId != actor.UserId) throw Error("REAUTH_REQUIRED", 401);
        return result.ToArray();
    }
    private sealed record Command(string CommandId, string? ExpectedVersion, FlowAuthoringBodyV2 Body, string Digest, byte[] Raw, string? Correlation);
    private static Command Parse(byte[] raw)
    {
        try
        {
            using var doc = WorkflowCommandJsonV1.Parse(raw); var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Any(p => p.Name is not ("commandId" or "expectedVersion" or "body" or "clientCorrelationId"))
                || !root.TryGetProperty("expectedVersion", out var version)) throw new JsonException();
            var id = root.GetProperty("commandId").GetString();
            if (id == null || !Regex.IsMatch(id, "\\A[A-Za-z0-9_.-]{1,100}\\z")) throw new JsonException();
            var expected = version.ValueKind == JsonValueKind.Null ? null : version.GetString();
            if (expected != null && !Guid.TryParseExact(expected, "D", out _)) throw new JsonException();
            var correlation = root.TryGetProperty("clientCorrelationId", out var c) ? c.GetString() : null;
            if (correlation?.Length > 100) throw new JsonException();
            var body = root.GetProperty("body"); var typed = body.Deserialize<FlowAuthoringBodyV2>(Json) ?? throw new JsonException();
            if (string.IsNullOrWhiteSpace(typed.Name) || typed.Name.Length > 200 || typed.SchemaJson == null
                || typed.FormKey?.Length > 100 || typed.FunctionId?.Length > 50 || typed.FlowCode?.Length > 50) throw new JsonException();
            return new(id, expected, typed, WorkflowCommandJsonV1.Digest(body, expected), raw.ToArray(), correlation);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException)
        { throw Error("COMMAND_SHAPE_INVALID", 400); }
    }
    private string[] ValidateSchema(string key, string schemaJson, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            using var doc = WorkflowCommandJsonV1.Parse(Encoding.UTF8.GetBytes(schemaJson));
            var schema = doc.Deserialize<FlowSchema>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new JsonException();
            if (schema.Nodes == null || schema.Edges == null || schema.Nodes.Any(x => x == null || string.IsNullOrWhiteSpace(x.Id)
                || x.Type?.Trim().ToLowerInvariant() is not ("start" or "end" or "approval" or "gateway" or "parallelsplit" or "paralleljoin" or "inclusivesplit" or "inclusivejoin" or "servicetask" or "subflow"))
                || schema.Nodes.GroupBy(x => x.Id, StringComparer.Ordinal).Any(g => g.Count() != 1) || schema.Edges.Any(x => x == null))
                throw Error("SCHEMA_SHAPE_INVALID", 422);
            var errors = FlowSchemaValidator.Validate(schema);
            foreach (var node in schema.Nodes.Where(x => string.Equals(x.Type?.Trim(), "serviceTask", StringComparison.OrdinalIgnoreCase)))
            {
                // E-WF-016 also covers a missing outgoing edge. Keep that graph diagnostic
                // without permitting unknown executable kinds, modes or unsafe references.
                var kind = node.ServiceKind?.Trim(); var mode = node.ServiceMode?.Trim();
                if (kind is not (ServiceKind.DataWriteback or ServiceKind.WebApi or ServiceKind.Timer)
                    || !string.IsNullOrWhiteSpace(mode) && mode is not (ServiceMode.Sync or ServiceMode.Async)
                    || kind == ServiceKind.Timer && (string.IsNullOrWhiteSpace(node.ServiceDelayMode) || string.IsNullOrWhiteSpace(node.ServiceDelayValue))
                    || kind == ServiceKind.WebApi && string.IsNullOrWhiteSpace(node.ServicePath)
                    || ServiceVarsHelper.ContainsUnsupportedSubscript(node.ServicePath) || ServiceVarsHelper.ContainsUnsupportedSubscript(node.ServiceParamsJson))
                    throw Error("SCHEMA_EXECUTION_INVALID", 422);
                if (kind == ServiceKind.DataWriteback && !executors.Any(e => e.Kind == ServiceKind.DataWriteback && string.Equals(e.Key, node.ServiceActionName, StringComparison.OrdinalIgnoreCase))
                    || kind == ServiceKind.WebApi && !connectors.Any(c => string.Equals(c.Name, node.ServiceConnectorName, StringComparison.OrdinalIgnoreCase))) throw Error("SERVICE_REFERENCE_INVALID", 422);
                if (node.ServiceTimeoutSec is int seconds && seconds >= WfServiceJobService.LeaseDuration.TotalSeconds) throw Error("SERVICE_TIMEOUT_INVALID", 422);
            }
            // Graph incompleteness remains explicit diagnostics, not an executable published artifact.
            var fatal = errors.FirstOrDefault(e => e is "E-WF-028");
            if (fatal != null) throw Error(fatal, 422);
            SubFlowRefValidator.Validate(db, key, schema);
            return errors.Distinct().ToArray();
        }
        catch (ScopeAuthorizationExceptionV1) { throw; }
        catch (Exception e) when (e is JsonException or InvalidOperationException) { throw Error("SCHEMA_INVALID", 422); }
    }
    private sealed record Saved(string BodyDigest, string ResultJson);
    private Task<Saved?> Original(Guid actor, string key, string command, CancellationToken ct) => db.Database.SqlQuery<Saved>($"""
        SELECT body_digest AS "BodyDigest",result_json AS "ResultJson" FROM cp6_workflow_authoring_v2.commands
        WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND actor_id={actor}
          AND operation='FLOW_SAVE_DRAFT' AND resource_key={key} AND command_id={command}
        """).SingleOrDefaultAsync(ct);
    private async Task<WorkflowCommandResultV1> ProjectOriginal(UserPermissionContext actor, string key, Saved saved, CancellationToken ct)
    {
        var head = await Heads().SingleOrDefaultAsync(x => x.Key == key, ct) ?? throw Error("DEFINITION_NOT_FOUND", 404);
        var permissions = await Permission(actor, head, false, false, ct);
        var original = JsonSerializer.Deserialize<WorkflowCommandResultV1>(saved.ResultJson, Json) ?? throw Error("ORIGINAL_RESULT_INVALID");
        if (original.Result == null || original.ResourceId != head.Id || original.Result.Key != key) throw Error("ORIGINAL_RESULT_INVALID");
        var projected = original.Result with { Fields = original.Result.Fields.Where(x => permissions.Fields[x.Key].CanRead).ToDictionary(), WritableFields = [],
            Diagnostics = permissions.Fields["SchemaJson"].CanRead ? original.Result.Diagnostics : [] };
        return original with { Result = projected };
    }
    public async Task<WorkflowCommandResultV1> OriginalAsync(string key, string command, CancellationToken ct = default)
    {
        if (!Regex.IsMatch(command, "\\A[A-Za-z0-9_.-]{1,100}\\z")) throw Error("COMMAND_SHAPE_INVALID", 400);
        Entry(); await using var tx = await db.Database.BeginTransactionAsync(ct); await Root(ct); var actor = await Actor(ct);
        var head = await Heads().SingleOrDefaultAsync(x => x.Key == key, ct)
            ?? new Head(Guid.Empty, Guid.Empty, key, actor.UserId, actor.DeptId, Guid.Empty, "", "", null, null, null);
        await Permission(actor, head, false, false, ct);
        var saved = await Original(actor.UserId, key, command, ct);
        var result = saved == null ? new(command, "NOT_OBSERVED", null, null, null, null, null) : await ProjectOriginal(actor, key, saved, ct);
        if ((await Actor(ct)).UserId != actor.UserId) throw Error("REAUTH_REQUIRED", 401);
        return result;
    }
    public async Task<WorkflowCommandResultV1> SaveAsync(string key, byte[] raw, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100) throw Error("DEFINITION_KEY_INVALID", 400);
        var command = Parse(raw); Entry();
        await using var tx = await db.Database.BeginTransactionAsync(ct); var committing = false;
        try
        {
            await Root(ct); var actor = await Actor(ct);
            var oldCommand = await Original(actor.UserId, key, command.CommandId, ct);
            if (oldCommand != null)
            {
                var original = await ProjectOriginal(actor, key, oldCommand, ct);
                if (oldCommand.BodyDigest != command.Digest) throw Error("COMMAND_BODY_CONFLICT", 409);
                if ((await Actor(ct)).UserId != actor.UserId) throw Error("REAUTH_REQUIRED", 401);
                return original;
            }
            var lease = await fence.AcquireAsync(ct);
            if (!await db.Database.SqlQueryRaw<bool>("SELECT cp6_workflow_authoring_v2.ready() AS \"Value\"").SingleAsync(ct)) throw Error("AUTHORING_WRITER_COVERAGE_UNAVAILABLE");
            var old = await Heads().SingleOrDefaultAsync(x => x.Key == key, ct);
            var head = old ?? new Head(Guid.NewGuid(), Guid.NewGuid(), key, actor.UserId, actor.DeptId, Guid.NewGuid(), "", "", null, null, null);
            var permission = await Permission(actor, head, true, old == null, ct);
            if (old?.Status == 1) throw Error("DRAFT_REQUIRED", 409);
            if (old != null && command.ExpectedVersion == null) throw Error("DEFINITION_EXISTS", 409);
            if (old == null && command.ExpectedVersion != null || old != null && command.ExpectedVersion != old.Version.ToString("D")) throw Error("DEFINITION_CHANGED", 409);
            var body = command.Body with { FunctionId = string.IsNullOrWhiteSpace(command.Body.FunctionId) ? null : command.Body.FunctionId,
                FlowCode = string.IsNullOrWhiteSpace(command.Body.FlowCode) ? null : command.Body.FlowCode };
            var next = head with { Version = Guid.NewGuid(), SavedBy = actor.UserId, Name = body.Name, SchemaJson = body.SchemaJson, FormKey = body.FormKey, FunctionId = body.FunctionId, FlowCode = body.FlowCode };
            CheckWrites(old, next, permission);
            var diagnostics = ValidateSchema(key, body.SchemaJson, ct);
            if (old == null)
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO cp6_workflow_authoring_v2.heads(tenant_id,environment_id,id,flow_key,owner_id,owner_dept_id,function_id,flow_code,draft_id,row_version)
                    VALUES({db.CurrentTenantId},{fence.EnvironmentId},{next.Id},{key},{actor.UserId},{actor.DeptId},{body.FunctionId},{body.FlowCode},{next.DraftId},{next.Version})
                    """, ct);
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO cp6_workflow_authoring_v2.drafts(tenant_id,environment_id,head_id,id,name,form_key,schema_json,row_version,saved_by)
                    VALUES({db.CurrentTenantId},{fence.EnvironmentId},{next.Id},{next.DraftId},{body.Name},{body.FormKey},{body.SchemaJson},{next.Version},{actor.UserId})
                    """, ct);
            }
            else
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE cp6_workflow_authoring_v2.heads SET function_id={body.FunctionId},flow_code={body.FlowCode},row_version={next.Version}
                    WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND id={next.Id}
                    """, ct);
                var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE cp6_workflow_authoring_v2.drafts SET name={body.Name},form_key={body.FormKey},schema_json={body.SchemaJson},row_version={next.Version},saved_by={actor.UserId}
                    WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND head_id={next.Id} AND row_version={old.Version}
                    """, ct);
                if (changed != 1) throw Error("DEFINITION_CHANGED", 409);
            }
            var at = DateTimeOffset.UtcNow;
            // Store the complete original snapshot. Each response (including replay) applies current field projection.
            var snapshot = new FlowAuthoringViewV2(next.Id, next.DraftId, key, next.Version.ToString("D"), Values(next), [], diagnostics);
            var saved = new WorkflowCommandResultV1(command.CommandId, "SUCCEEDED", next.Id, snapshot.Version, snapshot, at, command.Correlation);
            var text = JsonSerializer.Serialize(saved, Json);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_workflow_authoring_v2.commands(tenant_id,environment_id,actor_id,operation,resource_key,command_id,canonical_version,body_digest,raw_body,raw_digest,result_json,recorded_at)
                VALUES({db.CurrentTenantId},{fence.EnvironmentId},{actor.UserId},'FLOW_SAVE_DRAFT',{key},{command.CommandId},'WFC-CJ-1',{command.Digest},{command.Raw},{MainOwnerOperationJournalV1.Digest(command.Raw)},{text},{at})
                """, ct);
            var finalActor = await Actor(ct);
            if (finalActor.UserId != actor.UserId) throw Error("REAUTH_REQUIRED", 401);
            var finalPermission = await Permission(finalActor, head, true, old == null, ct); CheckWrites(old, next, finalPermission);
            var policy = await ReadPolicy(ct);
            var evidence = JsonSerializer.Serialize(new { ActualActor = actor.UserId, EffectiveActor = actor.UserId, Policy = policy, Scope = lease.Snapshot,
                PermissionDecisionRef = Guid.NewGuid(), CheckedAtUtc = DateTimeOffset.UtcNow, Fields = finalPermission.Input.Versions,
                Action = old == null ? "oa-designer:add" : "oa-designer:edit", EffectiveRoles = finalActor.RoleIds,
                ResourceScope = finalActor.DataScopes.GetValueOrDefault(Resource, 1),
                ActorDepartment = finalActor.DeptId, ActorDepartmentPath = finalActor.DeptPath,
                CustomDepartments = finalActor.CustomDeptIds.GetValueOrDefault(Resource),
                FieldRoles = finalPermission.Input.Roles, FieldRows = finalPermission.Input.Rows,
                FieldAccess = finalPermission.Fields.ToDictionary(x => x.Key, x => x.Value.Access),
                next.OwnerId, next.DeptId, CommandDigest = command.Digest, RawDigest = MainOwnerOperationJournalV1.Digest(command.Raw) }, Json);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_workflow_authoring_v2.audit(tenant_id,environment_id,actor_id,resource_key,command_id,evidence_json,recorded_at)
                VALUES({db.CurrentTenantId},{fence.EnvironmentId},{actor.UserId},{key},{command.CommandId},{evidence},{at})
                """, ct);
            if (await Heads().SingleAsync(x => x.Key == key, ct) != next) throw Error("DEFINITION_CHANGED", 409);
            if (!await db.Database.SqlQueryRaw<bool>("SELECT cp6_workflow_authoring_v2.ready() AS \"Value\"").SingleAsync(ct)) throw Error("AUTHORING_WRITER_COVERAGE_UNAVAILABLE");
            await fence.ValidateAsync(lease, ct);
            CompletionClock(); ct.ThrowIfCancellationRequested(); committing = true; await tx.CommitAsync(ct);
            return saved with { Result = View(next, finalPermission, diagnostics) };
        }
        catch (Exception error)
        {
            try { await tx.RollbackAsync(CancellationToken.None); }
            catch (Exception rollback) { throw new ScopeAuthorizationExceptionV1("COMMAND_OUTCOME_UNKNOWN", 503, new AggregateException(error, rollback)); }
            if (committing) throw new ScopeAuthorizationExceptionV1("COMMAND_OUTCOME_UNKNOWN", 503, error);
            if (error is Npgsql.PostgresException { SqlState: "23505" or "23514" }) throw Error("DEFINITION_IDENTITY_CONFLICT", 409);
            if (error is Npgsql.NpgsqlException or CurrentPermissionUnavailableException) throw new ScopeAuthorizationExceptionV1("AUTHORITY_FENCE_UNAVAILABLE", 503, error);
            throw;
        }
    }
    private static void CheckWrites(Head? old, Head next, FieldPermissionDecisionV1 permission)
    {
        var prior = old == null ? null : Values(old);
        foreach (var (field, value) in Values(next))
            if (!permission.Fields[field].CanRead || (prior == null || prior[field] != value) && !permission.Fields[field].CanWrite)
                throw Error("FIELD_WRITE_FORBIDDEN", 403);
    }
}
