using System.Security.Claims;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Services;

public sealed record CrmOwnerDto(Guid SubjectId, string DisplayName, Guid? DepartmentId, string DepartmentPath);
public sealed record CrmDepartmentDto(Guid DepartmentId, string Name, string Path);
public sealed record CrmContextDto(Guid OrganizationId, string OrganizationSlug, string OrganizationName, string Region, Guid SubjectId,
    string DisplayName, Guid? DepartmentId, string DepartmentPath, string[] AllowedActions, string DataScope,
    Guid[] DepartmentIds, bool IsSupervisor, CrmOwnerDto[] EligibleOwners, CrmDepartmentDto[] Departments);

public sealed class CrmOidcDirectory(CP6Context db, ITenantContext tenant, ITokenBlacklistService blacklist,
    IPasswordPolicyService passwords, CrmOidcOptions options)
{
    public async Task<object?> ResolveOrganizationAsync(string slug)
    {
        var mapping = options.Organizations.SingleOrDefault(o => o.Slug == slug && o.CrmEnabled);
        if (!options.Enabled || mapping == null) return null;
        var organization = await db.Sys_Tenants.AsNoTracking().SingleOrDefaultAsync(t => t.Id == mapping.TenantId && t.Enable);
        if (organization == null || organization.ExpireDate <= DateTime.Now) return null;
        return new { organizationId = mapping.TenantId, organizationSlug = mapping.Slug, region = mapping.Region, crmEnabled = true };
    }

    public static string SecurityStamp(Sys_User user) => CrmOidcCrypto.Hash(
        AuthSessionVersion.For(user) + "|" + user.TwoFactorSecret);

    public async Task<bool> HasCurrentBrowserAuthenticationAsync(string hash, Sys_User user)
    {
        var version = AuthSessionVersion.For(user);
        return await (from r in db.Sys_RefreshTokens.IgnoreQueryFilters().AsNoTracking()
                      join s in db.Sys_BrowserSessions.IgnoreQueryFilters().AsNoTracking() on r.BrowserSessionId equals (Guid?)s.Id
                      where r.TokenHash == hash && r.UserId == user.Id && r.TenantId == user.TenantId && r.ClientKind == "Web"
                          && s.UserId == user.Id && s.TenantId == user.TenantId && s.LoggedOutAtUtc == null
                          && s.AuthenticationVersion == version
                      select s.Id).AnyAsync();
    }

    public async Task<string?> BindRefreshAsync(string? raw, Sys_User user)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        var hash = CrmOidcCrypto.Hash(raw);
        return await HasCurrentBrowserAuthenticationAsync(hash, user)
            && await db.Sys_RefreshTokens.IgnoreQueryFilters().AsNoTracking().AnyAsync(r => r.TokenHash == hash
            && r.UserId == user.Id && r.TenantId == user.TenantId && r.ClientKind == "Web"
            && r.RevokedAt == null && r.ExpiresAt > DateTime.Now) ? hash : null;
    }

    public async Task<bool> RefreshFamilyMatchesAsync(CrmOidcGrant grant, string? presentedRaw = null)
    {
        var wanted = presentedRaw == null ? null : CrmOidcCrypto.Hash(presentedRaw);
        var hash = grant.SourceRefreshHash;
        var source = await db.Sys_RefreshTokens.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(r => r.TokenHash == hash
            && r.UserId == grant.SubjectId && r.TenantId == grant.OrganizationId && r.ClientKind == "Web");
        if (source == null) return false;
        if (source.BrowserSessionId is { } sessionId)
        {
            var session = await db.Sys_BrowserSessions.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(s =>
                s.Id == sessionId && s.UserId == grant.SubjectId && s.TenantId == grant.OrganizationId);
            if (session == null) return false;
            var family = db.Sys_RefreshTokens.IgnoreQueryFilters().AsNoTracking().Where(r =>
                r.BrowserSessionId == sessionId && r.UserId == grant.SubjectId && r.TenantId == grant.OrganizationId && r.ClientKind == "Web");
            // Cookie deletion compares family identity even after administrative revocation.
            if (wanted != null) return await family.AnyAsync(r => r.TokenHash == wanted);
            return session.LoggedOutAtUtc == null && await family.AnyAsync(r => r.RevokedAt == null && r.ExpiresAt > DateTime.Now);
        }
        // Compatibility only for pre-family CP6 cookies. CRM authorization separately requires a recorded login version.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (!string.IsNullOrEmpty(hash) && seen.Add(hash))
        {
            var row = await db.Sys_RefreshTokens.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(r => r.TokenHash == hash
                && r.UserId == grant.SubjectId && r.TenantId == grant.OrganizationId && r.ClientKind == "Web");
            if (row == null) return false;
            if (wanted != null && wanted == row.TokenHash) return true; // Identity comparison for cookie deletion after revocation.
            if (wanted == null && row.RevokedAt == null) return row.ExpiresAt > DateTime.Now;
            hash = row.ReplacedByTokenHash;
        }
        return false;
    }

    public async Task<Sys_User?> FindActiveAsync(Guid subjectId, Guid organizationId, string sourceJti,
        string? stamp = null, long? issuedAt = null)
    {
        if (!options.Enabled || !options.Organizations.Any(o => o.TenantId == organizationId && o.CrmEnabled)
            || string.IsNullOrWhiteSpace(sourceJti) || await blacklist.IsBlacklistedAsync(sourceJti)) return null;
        var organization = await db.Sys_Tenants.AsNoTracking().SingleOrDefaultAsync(t => t.Id == organizationId);
        if (organization is not { Enable: true } || organization.ExpireDate is { } expiry && expiry <= DateTime.Now) return null;
        var user = await db.Sys_Users.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == subjectId && u.TenantId == organizationId);
        if (user is not { Enable: true, MustChangePassword: false } || user.LockedUntil > DateTime.Now
            || passwords.IsExpired(user) || stamp != null && SecurityStamp(user) != stamp) return null;
        if (issuedAt.HasValue && user.PasswordChangedAt is { } changed
            && new DateTimeOffset(DateTime.SpecifyKind(changed, DateTimeKind.Local)).ToUnixTimeSeconds() > issuedAt) return null;
        tenant.CurrentTenantId = organizationId;
        return user;
    }

    public async Task<CrmContextDto> ProjectAsync(Sys_User user)
    {
        tenant.CurrentTenantId = user.TenantId;
        var permissions = await new PermissionAggregator(db).BuildEnabledRolesAsync(user.Id);
        var mapping = options.Organizations.Single(o => o.TenantId == user.TenantId && o.CrmEnabled);
        var organizationName = await db.Sys_Tenants.Where(t => t.Id == user.TenantId).Select(t => t.TenantName).SingleAsync();
        var departments = await db.Sys_Depts.AsNoTracking().Where(d => d.Enable).ToListAsync();
        var ownDepartment = departments.SingleOrDefault(d => d.Id == user.DeptId);
        var scope = permissions.DataScopes.GetValueOrDefault("crm-lead", 1);
        var departmentIds = scope switch
        {
            2 when ownDepartment != null => new[] { ownDepartment.Id },
            3 when ownDepartment != null && ownDepartment.Path.StartsWith('/')
                && ownDepartment.Path.EndsWith($"/{ownDepartment.Id}/", StringComparison.OrdinalIgnoreCase)
                => departments.Where(d => d.Path.StartsWith(ownDepartment.Path, StringComparison.Ordinal)).Select(d => d.Id).ToArray(),
            4 => departments.Where(d => permissions.CustomDeptIds.GetValueOrDefault("crm-lead", []).Contains(d.Id)).Select(d => d.Id).ToArray(),
            _ => Array.Empty<Guid>()
        };
        var dataScope = scope == 5 ? "organization" : scope is 2 or 3 or 4 ? "departments" : "own";
        var actions = permissions.ActionKeys.Where(k => k.StartsWith("crm-", StringComparison.Ordinal)).Order().ToArray();
        var supervisor = actions.Contains("crm-lead:assign") && (scope == 5 || departmentIds.Length > 0);
        var owners = new List<CrmOwnerDto>();
        if (ownDepartment != null && actions.Contains("crm-lead:query") && actions.Contains("crm-lead:edit"))
            owners.Add(new(user.Id, user.NickName ?? user.UserName, ownDepartment.Id, ownDepartment.Path));
        if (supervisor)
        {
            var candidates = db.Sys_Users.AsNoTracking().Where(u => u.Enable && !u.MustChangePassword
                && (u.LockedUntil == null || u.LockedUntil <= DateTime.Now));
            if (scope != 5) candidates = candidates.Where(u => u.DeptId != null && departmentIds.Contains(u.DeptId.Value));
            foreach (var candidate in await candidates.OrderBy(u => u.UserName).ToListAsync())
            {
                if (passwords.IsExpired(candidate)) continue;
                var candidatePermissions = await new PermissionAggregator(db).BuildEnabledRolesAsync(candidate.Id);
                if (!candidatePermissions.ActionKeys.Contains("crm-lead:query") || !candidatePermissions.ActionKeys.Contains("crm-lead:edit")) continue;
                var department = departments.SingleOrDefault(d => d.Id == candidate.DeptId);
                // Disabled/missing departments cannot be used to transfer departmental records.
                if (department == null || candidate.Id == user.Id) continue;
                owners.Add(new(candidate.Id, candidate.NickName ?? candidate.UserName, department?.Id, department?.Path ?? ""));
            }
        }
        return new(user.TenantId, mapping.Slug, organizationName, mapping.Region, user.Id, user.NickName ?? user.UserName,
            ownDepartment?.Id, ownDepartment?.Path ?? "", actions, dataScope, departmentIds, supervisor, owners.ToArray(),
            departments.Where(d => scope == 5 || departmentIds.Contains(d.Id) || d.Id == ownDepartment?.Id)
                .Select(d => new CrmDepartmentDto(d.Id, d.DeptName, d.Path)).ToArray());
    }
}
