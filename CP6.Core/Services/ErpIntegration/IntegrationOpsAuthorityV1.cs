using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.ErpIntegration;

internal sealed class IntegrationOpsAuthorityV1(ErpIntegrationRuntime runtime, IHttpContextAccessor http)
{
    internal const string Resource = "integration-ops";
    internal sealed record Access(Guid Actor, string PolicyVersion, bool Replay, bool Deliver, bool Evidence, bool Audit);

    internal static Task<CP6Context> BindAsync(ErpIntegrationContext queue, Guid tenant, CancellationToken ct)
    {
        if (!queue.Database.IsNpgsql()) throw IntegrationOpsContractV1.Error("SOURCE_UNAVAILABLE", 503);
        return DatabaseSharedContext.CreateAsync<CP6Context>(queue, DatabaseContextKind.Core,
            options => new(options, new TenantContext { CurrentTenantId = tenant }), ct);
    }

    internal async Task<Access> ReadAsync(CP6Context db, CancellationToken ct)
    {
        if (http.HttpContext?.User.HasClaim(c => c.Type is "client_id" or "impersonator_id") != false)
            throw IntegrationOpsContractV1.Error("INTEGRATION_READ_DENIED", 403);
        try { return await ReadSubjectAsync(db, CurrentPermissionContext.RequireCurrentUser(db, http), ct); }
        catch (CurrentPermissionUnavailableException) { throw IntegrationOpsContractV1.Error("INTEGRATION_READ_DENIED", 403); }
    }

    internal async Task<Access> ReadSubjectAsync(CP6Context db, Guid actor, CancellationToken ct)
    {
        var tenant = db.CurrentTenantId;
        if (actor == Guid.Empty || !runtime.Options.Tenants.ContainsKey(tenant))
            throw IntegrationOpsContractV1.Error("INTEGRATION_READ_DENIED", 403);
        try
        {
            var now = runtime.Clock.GetUtcNow().UtcDateTime;
            if (!await db.Sys_Tenants.IgnoreQueryFilters().AsNoTracking().AnyAsync(
                    t => t.Id == tenant && t.Enable && (t.ExpireDate == null || t.ExpireDate > now), ct) ||
                !await db.Sys_Users.AsNoTracking().AnyAsync(
                    u => u.TenantId == tenant && u.Id == actor && u.Enable && (u.LockedUntil == null || u.LockedUntil <= now), ct))
                throw IntegrationOpsContractV1.Error("INTEGRATION_READ_DENIED", 403);

            var facts = await PostgreSqlPermissionReader.ReadAsync(db, actor, ct);
            var defined = await (from a in db.Sys_MenuActions.AsNoTracking()
                join m in db.Sys_Menus.AsNoTracking() on a.MenuId equals m.MenuId
                where a.TenantId == tenant && m.Enable && m.MenuKey == Resource
                select a.ActionCode).ToArrayAsync(ct);
            bool Can(string action) => defined.Contains(action, StringComparer.Ordinal) &&
                facts.ActionKeys.Contains(Resource + ":" + action);
            // 队列没有可信部门归属。只有明确授予本租户队列查询、且范围为全租户时才读取。
            if (!Can("query") || facts.DataScopes.GetValueOrDefault(Resource, 5) != 5)
                throw IntegrationOpsContractV1.Error("INTEGRATION_READ_DENIED", 403);
            var fields = facts.FieldPerms.GetValueOrDefault(Resource);
            bool Visible(string field) => fields?.GetValueOrDefault(field, 1) is null or 1 or 2;
            return new(actor, AuditEvidenceV1.PermissionDecisionVersion(tenant, facts), Can("replay"),
                Can("deliver"), Can("evidence") && Visible("NativeEvidence"), Can("audit"));
        }
        catch (CurrentPermissionUnavailableException)
        {
            throw IntegrationOpsContractV1.Error("INTEGRATION_READ_DENIED", 403);
        }
    }
}
