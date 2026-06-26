using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Platform;

/// <summary>
/// <see cref="IPlatformAdminService"/> 实现（多租户合规 #5 块②）。
/// <para>跨租户授撤 <c>Sys_User.IsPlatformAdmin</c>：目标用户经 <c>IgnoreQueryFilters</c> 检索（不限当前租户）。</para>
/// <para>防自锁死（E-SEC-037）：撤销目标若本身是启用中的平台超管，且为<b>最后一个</b>启用超管 → 拒绝，
/// 防平台失去全部超管入口。撤非超管/已停用超管为允许操作。</para>
/// <para>带外审计（PlatformAdminGranted/Revoked）经 <see cref="TenantScope"/> 强制落平台租户（R5）。</para>
/// </summary>
public class PlatformAdminService : IPlatformAdminService
{
    private readonly CP6Context _db;
    private readonly ISecurityAuditService _audit;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUserAccessor? _current;

    public PlatformAdminService(
        CP6Context db,
        ISecurityAuditService audit,
        ITenantContext tenant,
        ICurrentUserAccessor? current = null)
    {
        _db = db;
        _audit = audit;
        _tenant = tenant;
        _current = current;
    }

    public async Task<List<PlatformAdminRow>> ListAsync()
    {
        // Sys_User 受全局租户过滤 → IgnoreQueryFilters 跨所有租户列出平台超管。
        return await _db.Sys_Users.IgnoreQueryFilters()
            .Where(u => u.IsPlatformAdmin)
            .Select(u => new PlatformAdminRow(u.Id, u.UserName, u.NickName, u.TenantId, u.Enable))
            .ToListAsync();
    }

    public async Task GrantAsync(Guid userId)
    {
        var u = await _db.Sys_Users.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == userId)
            ?? throw new InvalidOperationException("E-SEC-032");

        u.IsPlatformAdmin = true;   // 幂等：已是超管时重复置 true 无害
        await _db.SaveChangesAsync();

        using (new TenantScope(_tenant, TenantContext.DefaultTenant))
        {
            await _audit.LogAsync(SecurityEventType.PlatformAdminGranted, _current?.UserId, _current?.UserName,
                null, null, null, $"target={u.UserName}");
        }
    }

    public async Task RevokeAsync(Guid userId)
    {
        var target = await _db.Sys_Users.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == userId)
            ?? throw new InvalidOperationException("E-SEC-032");

        // 防自锁死：仅当目标本身是启用中的平台超管时才计数把关；撤非超管/已停用超管为无害操作。
        if (target.IsPlatformAdmin && target.Enable)
        {
            var enabledCount = await _db.Sys_Users.IgnoreQueryFilters()
                .CountAsync(u => u.IsPlatformAdmin && u.Enable);
            if (enabledCount == 1)
                throw new InvalidOperationException("E-SEC-037");   // 最后一个启用超管，拒撤
        }

        target.IsPlatformAdmin = false;
        await _db.SaveChangesAsync();

        using (new TenantScope(_tenant, TenantContext.DefaultTenant))
        {
            await _audit.LogAsync(SecurityEventType.PlatformAdminRevoked, _current?.UserId, _current?.UserName,
                null, null, null, $"target={target.UserName}");
        }
    }
}
