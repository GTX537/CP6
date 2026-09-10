using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.CrmIdentity;

/// <summary>Captures identifiers before EF detaches deleted rows; never copies personal fields.</summary>
public sealed class IdentityChangeCapture
{
    internal readonly HashSet<(Guid Tenant, string Aggregate)> Targets = [];
    internal readonly HashSet<(Guid Tenant, Guid User)> InvalidatedUsers = [];
    internal readonly HashSet<Guid> DisabledTenants = [];
    internal readonly HashSet<(Guid Tenant, Guid User, Guid Family)> RevokedFamilies = [];
    private readonly HashSet<int> menus = [];
    public bool HasChanges => Targets.Count + InvalidatedUsers.Count + DisabledTenants.Count + RevokedFamilies.Count + menus.Count > 0;

    public static IdentityChangeCapture Capture(CP6Context db, CrmIdentityOptions options)
    {
        var capture = new IdentityChangeCapture();
        foreach (var entry in db.ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            bool Changed(string property) => entry.State != EntityState.Modified || entry.Property(property).IsModified;
            switch (entry.Entity)
            {
                case Sys_Tenant tenant when options.Tenants.ContainsKey(tenant.Id) &&
                    (Changed(nameof(tenant.Enable)) || Changed(nameof(tenant.ExpireDate))):
                    capture.Targets.Add((tenant.Id, $"tenant:{tenant.Id:D}"));
                    if (entry.State == EntityState.Deleted || !tenant.Enable) capture.DisabledTenants.Add(tenant.Id);
                    break;
                case Sys_User user when options.Tenants.ContainsKey(user.TenantId) && new[]
                    {
                        nameof(user.Enable), nameof(user.DeptId), nameof(user.ManagerId), nameof(user.RoleId),
                        nameof(user.Password), nameof(user.PasswordChangedAt), nameof(user.LockedUntil),
                        nameof(user.MustChangePassword), nameof(user.AuthenticationEpoch), nameof(user.TwoFactorEnabled),
                        nameof(user.TwoFactorSecret), nameof(user.ExternalSubject), nameof(user.ExternalProvider)
                    }.Any(Changed):
                    capture.Targets.Add((user.TenantId, $"user:{user.Id:D}"));
                    if (entry.State != EntityState.Added && (entry.State == EntityState.Deleted ||
                        Changed(nameof(user.Password)) || Changed(nameof(user.PasswordChangedAt)) ||
                        Changed(nameof(user.AuthenticationEpoch)) || Changed(nameof(user.TwoFactorEnabled)) ||
                        Changed(nameof(user.TwoFactorSecret)) || Changed(nameof(user.ExternalSubject)) ||
                        Changed(nameof(user.ExternalProvider)) || (!user.Enable && Changed(nameof(user.Enable))) ||
                        (user.MustChangePassword && Changed(nameof(user.MustChangePassword))) ||
                        (user.LockedUntil > DateTime.Now && Changed(nameof(user.LockedUntil)))))
                        capture.InvalidatedUsers.Add((user.TenantId, user.Id));
                    break;
                case Sys_Dept dept when options.Tenants.ContainsKey(dept.TenantId) &&
                    (Changed(nameof(dept.Enable)) || Changed(nameof(dept.ParentId)) || Changed(nameof(dept.Path)) || Changed(nameof(dept.LeaderId))):
                    capture.Targets.Add((dept.TenantId, $"department:{dept.Id:D}"));
                    break;
                case Sys_Role role when options.Tenants.ContainsKey(role.TenantId) && Changed(nameof(role.Enable)):
                    capture.Targets.Add((role.TenantId, $"permission:role:{role.RoleId}"));
                    break;
                case Sys_UserRole membership when options.Tenants.ContainsKey(membership.TenantId):
                    capture.Targets.Add((membership.TenantId, $"user:{membership.UserId:D}"));
                    if (entry.State == EntityState.Modified)
                        capture.Targets.Add((membership.TenantId, $"user:{entry.OriginalValues.GetValue<Guid>(nameof(membership.UserId)):D}"));
                    break;
                case Sys_RoleAction action when options.Tenants.ContainsKey(action.TenantId):
                    capture.Targets.Add((action.TenantId, $"permission:role:{action.RoleId}"));
                    if (entry.State == EntityState.Modified)
                        capture.Targets.Add((action.TenantId, $"permission:role:{entry.OriginalValues.GetValue<int>(nameof(action.RoleId))}"));
                    break;
                case Sys_RoleDataScope scope when options.Tenants.ContainsKey(scope.TenantId):
                    capture.Targets.Add((scope.TenantId, $"permission:role:{scope.RoleId}"));
                    if (entry.State == EntityState.Modified)
                        capture.Targets.Add((scope.TenantId, $"permission:role:{entry.OriginalValues.GetValue<int>(nameof(scope.RoleId))}"));
                    break;
                case Sys_RoleFieldPerm field when options.Tenants.ContainsKey(field.TenantId):
                    capture.Targets.Add((field.TenantId, $"permission:role:{field.RoleId}"));
                    if (entry.State == EntityState.Modified)
                        capture.Targets.Add((field.TenantId, $"permission:role:{entry.OriginalValues.GetValue<int>(nameof(field.RoleId))}"));
                    break;
                case Sys_RoleMenu roleMenu when options.Tenants.ContainsKey(roleMenu.TenantId):
                    capture.Targets.Add((roleMenu.TenantId, $"permission:role:{roleMenu.RoleId}"));
                    break;
                case Sys_Menu menu when Changed(nameof(menu.MenuKey)):
                    capture.menus.Add(menu.MenuId);
                    break;
                case Sys_RefreshToken refresh when options.Tenants.ContainsKey(refresh.TenantId) &&
                    refresh.BrowserSessionId is Guid family && refresh.RevokedAt != null &&
                    refresh.ReplacedByTokenHash == null && Changed(nameof(refresh.RevokedAt)):
                    capture.RevokedFamilies.Add((refresh.TenantId, refresh.UserId, family));
                    break;
                case Sys_BrowserSession session when options.Tenants.ContainsKey(session.TenantId) &&
                    session.LoggedOutAtUtc != null && Changed(nameof(session.LoggedOutAtUtc)):
                    capture.RevokedFamilies.Add((session.TenantId, session.UserId, session.Id));
                    break;
            }
        }
        return capture;
    }

    internal async Task ExpandBeforeSaveAsync(CP6Context db, CrmIdentityOptions options, CancellationToken cancellationToken)
    {
        if (menus.Count == 0) return;
        var ids = menus.ToArray();
        var tenants = options.Tenants.Keys.ToArray();
        var roles = await db.Sys_RoleActions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => ids.Contains(x.MenuId) && tenants.Contains(x.TenantId))
            .Select(x => new { x.TenantId, x.RoleId }).Distinct().ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var role in roles) Targets.Add((role.TenantId, $"permission:role:{role.RoleId}"));
    }

    public void AddTenantTombstones(Guid tenantId, IEnumerable<Guid> users, IEnumerable<Guid> departments, IEnumerable<int> roles)
    {
        Targets.Add((tenantId, $"tenant:{tenantId:D}"));
        DisabledTenants.Add(tenantId);
        foreach (var user in users) Targets.Add((tenantId, $"user:{user:D}"));
        foreach (var department in departments) Targets.Add((tenantId, $"department:{department:D}"));
        foreach (var role in roles) Targets.Add((tenantId, $"permission:role:{role}"));
    }
}
