using System.Security.Claims;
using CP6.Core.EFDbContext;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace CP6.Core.Services.Sys;

/// <summary>
/// Reads current persisted permissions for the authenticated subject and tenant.
/// Unversioned distributed entries are never an authorization source.
/// </summary>
public class CurrentPermissionContext : ICurrentPermissionContext
{
    private readonly IHttpContextAccessor _http;
    private readonly IDistributedCache _cache;
    private readonly CP6Context _db;
    private readonly IPermissionAggregator _aggregator;

    public CurrentPermissionContext(
        IHttpContextAccessor http,
        IDistributedCache cache,
        CP6Context db,
        IPermissionAggregator aggregator)
    {
        _http = http;
        _cache = cache;
        _db = db;
        _aggregator = aggregator;
    }

    public async Task<UserPermissionContext> GetAsync()
    {
        var principal = _http.HttpContext?.User;
        var identities = principal?.Identities.Where(i => i.IsAuthenticated).ToArray();
        if (identities is not { Length: 1 })
            throw new CurrentPermissionUnavailableException();
        // Local access (including impersonation) and OIDC access have no token_use.
        // Reject pending or undefined purposes, even when carried by another identity.
        if (principal!.HasClaim(c => c.Type == "token_use"))
            throw new CurrentPermissionUnavailableException();
        var identity = identities[0];
        // Never splice identifiers from an unauthenticated or second identity.
        if (principal!.Identities.Where(i => !ReferenceEquals(i, identity))
            .Any(i => i.Claims.Any(IsBindingClaim)))
            throw new CurrentPermissionUnavailableException();
        var userId = ReadIdentifier(identity.Claims.Where(c => c.Type is ClaimTypes.NameIdentifier or "sub"));
        var tenantId = ReadIdentifier(identity.FindAll("tenant_id"));
        if (tenantId != _db.CurrentTenantId)
            throw new CurrentPermissionUnavailableException();
        return await ReadAsync(userId);
    }

    // Compatibility entry for login callers: build fresh, without caching a future allow.
    public Task<UserPermissionContext> PrewarmAsync(Guid userId) => ReadAsync(userId);

    private static bool IsBindingClaim(Claim claim)
        => claim.Type is ClaimTypes.NameIdentifier or "sub" or "tenant_id";

    private static Guid ReadIdentifier(IEnumerable<Claim> claims)
    {
        Guid? identifier = null;
        foreach (var claim in claims)
        {
            if (!Guid.TryParse(claim.Value, out var value) || value == Guid.Empty
                || (identifier.HasValue && identifier.Value != value))
                throw new CurrentPermissionUnavailableException();
            identifier = value;
        }
        return identifier ?? throw new CurrentPermissionUnavailableException();
    }

    public void Invalidate(Guid userId)
        => _cache.Remove(CacheKey(userId));

    public void InvalidateByRole(int roleId)
    {
        var users = _db.Sys_UserRoles
            .Where(x => x.RoleId == roleId)
            .Select(x => x.UserId)
            .Union(_db.Sys_Users
                .Where(x => x.RoleId == roleId)
                .Select(x => x.Id))
            .Distinct()
            .ToList();
        foreach (var userId in users)
            _cache.Remove(CacheKey(userId));
    }

    private async Task<UserPermissionContext> ReadAsync(Guid userId)
    {
        try
        {
            if (userId == Guid.Empty || !await _db.Sys_Users.AsNoTracking()
                .AnyAsync(u => u.Id == userId && u.Enable))
                throw new CurrentPermissionUnavailableException();
            return await _aggregator.BuildAsync(userId);
        }
        catch (CurrentPermissionUnavailableException) { throw; }
        catch (OperationCanceledException) when (_http.HttpContext?.RequestAborted.IsCancellationRequested == true) { throw; }
        catch (Exception error)
        {
            // Keep the underlying diagnostic for server-side handling; consumers receive a safe denial.
            throw new CurrentPermissionUnavailableException(error);
        }
    }

    private static string CacheKey(Guid userId) => $"perm-ctx:{userId}";
}
