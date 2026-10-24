using CP6.Core.EFDbContext;
using CP6.Core.Services.CrmIdentity;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Sys.IamAdministrationV1;

namespace CP6.Core.Services.Sys;

public sealed record IamInquiryScopeCommandV1 : IamCommandV1
{
    public InquirySiteFormScopeV1[]? SiteForms { get; init; }
}
public sealed record IamInquiryScopeEditorV1(Guid TenantId, Guid ActorId, int RoleId, bool RoleEnabled,
    string Version, string ReferencesVersion, InquirySiteFormScopeV1[] SiteForms);

/// <summary>Main IAM is the authority for explicit role scopes. Exact pairs grant no action,
/// field, object existence or permission in another tenant. The CRM Owner must resolve its
/// actual Site/Form relationship and intersect current actions, identity and these pairs.</summary>
public sealed class IamInquiryScopeAdministrationV1(CP6Context db, IamAdministrationV1 authority)
{
    public const string Kind = "role-inquiry-scopes";
    public Task<IamInquiryScopeEditorV1> ReadAsync(int roleId, Func<CancellationToken, Task<Guid>> actor, CancellationToken ct)
        => authority.ExecuteAsync("pub-data-scope:query", actor, async (who, token) =>
        {
            await CheckReadyAsync(db, token);
            var role = await db.Sys_Roles.AsNoTracking().SingleOrDefaultAsync(x => x.RoleId == roleId, token)
                ?? throw Error(404, "IAM_ROLE_NOT_FOUND");
            return new IamInquiryScopeEditorV1(db.CurrentTenantId, who, roleId, role.Enable,
                await authority.Version(IdentityEventContracts.RoleSubject(roleId), token),
                await authority.ReferencesVersion(token), await ReadSourceAsync(db, db.CurrentTenantId, roleId, token));
        }, ct);

    public Task<IamOperationV1> ChangeAsync(int roleId, IamInquiryScopeCommandV1 command,
        Func<CancellationToken, Task<Guid>> actor, CancellationToken ct)
    {
        Validate(command);
        if (command.SiteForms is null || command.SiteForms.Any(x => x is null || x.SiteId == Guid.Empty || x.FormId == Guid.Empty)
            || command.SiteForms.Distinct().Count() != command.SiteForms.Length)
            throw Error(400, "IAM_INQUIRY_SCOPE_INVALID");
        // Snapshot before the first await. Exact semantic set ordering is also the request fingerprint.
        var pairs = command.SiteForms.OrderBy(x => x.SiteId.ToString("D"), StringComparer.Ordinal)
            .ThenBy(x => x.FormId.ToString("D"), StringComparer.Ordinal).ToArray();
        var bound = new BoundCommand { OperationId = command.OperationId, ExpectedVersion = command.ExpectedVersion,
            ExpectedReferencesVersion = command.ExpectedReferencesVersion, Reason = command.Reason, RoleId = roleId, SiteForms = pairs };
        return authority.ExecuteAsync("pub-data-scope:edit", actor, async (who, token) =>
        {
            await CheckReadyAsync(db, token);
            var prior = await authority.Original(who, Kind, bound, token); if (prior is not null) return prior;
            var role = await db.Sys_Roles.AsNoTracking().SingleOrDefaultAsync(x => x.RoleId == roleId, token)
                ?? throw Error(404, "IAM_ROLE_NOT_FOUND");
            if (!role.Enable && pairs.Length != 0) throw Error(409, "IAM_ROLE_DISABLED");
            var key = IdentityEventContracts.RoleSubject(roleId);
            if (bound.ExpectedVersion != await authority.Version(key, token)
                || bound.ExpectedReferencesVersion != await authority.ReferencesVersion(token)) throw Error(412, "IAM_REFERENCES_CHANGED");
            try { db.RequireIdentityCapture(db.CurrentTenantId); }
            catch (InvalidOperationException) { throw Error(503, "IAM_IDENTITY_WRITER_UNAVAILABLE"); }
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cp6_inquiry_scope_v1.role_scopes WHERE tenant_id={db.CurrentTenantId} AND role_id={roleId}", token);
            foreach (var pair in pairs)
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_inquiry_scope_v1.role_scopes VALUES({db.CurrentTenantId},{roleId},{pair.SiteId},{pair.FormId})", token);
            await db.CaptureIdentitySourceAsync(db.CurrentTenantId, "permission:" + key, token);
            authority.Audit(who, bound.OperationId, Kind, bound.Reason!, null, roleId);
            await db.SaveChangesAsync(token);
            var result = new IamOperationV1(bound.OperationId, Kind, "COMMITTED", Version: await authority.Version(key, token),
                ReferencesVersion: await authority.ReferencesVersion(token), RoleId: roleId);
            await authority.Store(who, bound, result, token);
            return result;
        }, ct);
    }

    // Trusted in-process source read. Callers must establish current identity/roles and hold
    // their authority transaction; this method does not authorize a subject or an action.
    public static async Task<InquirySiteFormScopeV1[]> ReadSourceAsync(CP6Context db, Guid tenant, int role, CancellationToken ct)
    {
        // Old migrations/legacy PostgreSQL fixtures have no resource authority. No absence can grant.
        if (!await db.Database.SqlQueryRaw<bool>("SELECT to_regclass('cp6_inquiry_scope_v1.role_scopes') IS NOT NULL AS \"Value\"").SingleAsync(ct)) return [];
        await CheckReadyAsync(db, ct);
        var rows = await db.Database.SqlQuery<ScopeRow>($"SELECT site_id AS \"SiteId\",form_id AS \"FormId\" FROM cp6_inquiry_scope_v1.role_scopes WHERE tenant_id={tenant} AND role_id={role} ORDER BY site_id,form_id").ToArrayAsync(ct);
        return rows.Select(x => new InquirySiteFormScopeV1(x.SiteId, x.FormId)).ToArray();
    }
    internal static async Task CheckReadyAsync(CP6Context db, CancellationToken ct)
    {
        var ready = await db.Database.SqlQueryRaw<bool>("""
            SELECT count(*)=4 AS "Value" FROM pg_trigger
            WHERE NOT tgisinternal AND tgenabled IN ('O','A') AND
              ((tgrelid=to_regclass('cp6_inquiry_scope_v1.role_scopes')
                AND tgname IN ('cp6_authentication_gate_v1','cp6_inquiry_scope_revision_v1','cp6_inquiry_scope_no_truncate_v1'))
               OR (tgrelid=to_regclass('cp6_inquiry_scope_v1.pending_roles') AND tgname='cp6_inquiry_scope_commit_v1' AND tgdeferrable AND tginitdeferred))
            """).SingleAsync(ct);
        if (!ready) throw Error(503, "IAM_INQUIRY_SCOPE_UNAVAILABLE");
    }
    public sealed class ScopeRow { public Guid SiteId { get; set; } public Guid FormId { get; set; } }
    private sealed record BoundCommand : IamCommandV1
    { public int RoleId { get; init; } public InquirySiteFormScopeV1[] SiteForms { get; init; } = []; }
}
