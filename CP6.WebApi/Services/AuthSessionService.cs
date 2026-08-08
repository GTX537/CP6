using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Core.Utilities;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DTOs.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CP6.WebApi.Services;

public interface IAuthSessionService
{
    string BuildAccessToken(Sys_User user, string jti, bool mustChange, bool isPlatformAdmin = false);
    DateTimeOffset AccessExpiresAt { get; }
    DateTimeOffset RefreshExpiresAt { get; }
    Task<ClientProfileDto> BuildProfileAsync(Sys_User user, bool mustChange);
}

/// <summary>Web Cookie 与原生 Bearer 登录共用的 JWT/权限画像签发器。</summary>
public sealed class AuthSessionService : IAuthSessionService
{
    private readonly IConfiguration _config;
    private readonly ICurrentPermissionContext _permissions;
    private readonly CP6Context _db;
    private readonly SecurityOptions _security;

    public AuthSessionService(
        IConfiguration config,
        ICurrentPermissionContext permissions,
        CP6Context db,
        IOptions<SecurityOptions> security)
    {
        _config = config;
        _permissions = permissions;
        _db = db;
        _security = security.Value;
    }

    public DateTimeOffset AccessExpiresAt =>
        DateTimeOffset.UtcNow.AddMinutes(_security.Token.AccessTokenMinutes);

    public DateTimeOffset RefreshExpiresAt =>
        DateTimeOffset.UtcNow.AddDays(_security.Token.RefreshTokenDays);

    public string BuildAccessToken(
        Sys_User user, string jti, bool mustChange, bool isPlatformAdmin = false)
    {
        var jwt = _config.GetSection("JWT");
        return JwtHelper.GenerateToken(
            user.Id.ToString(),
            user.UserName,
            jwt["Secret"]!,
            jwt["Issuer"]!,
            jwt["Audience"]!,
            _security.Token.AccessTokenMinutes,
            user.TenantId,
            jti,
            mustChange,
            isPlatformAdmin);
    }

    public async Task<ClientProfileDto> BuildProfileAsync(Sys_User user, bool mustChange)
    {
        var context = await _permissions.PrewarmAsync(user.Id);
        var roleIds = context.RoleIds;
        var menuIds = await _db.Sys_RoleMenus
            .Where(x => roleIds.Contains(x.RoleId))
            .Select(x => x.MenuId)
            .Distinct()
            .ToListAsync();

        var menus = await _db.Sys_Menus
            .Where(x => menuIds.Contains(x.MenuId) && x.Enable)
            .OrderBy(x => x.OrderNo)
            .Select(x => new ClientMenuDto
            {
                Id = x.MenuId,
                MenuName = x.MenuName,
                RoutePath = x.RoutePath,
                Icon = x.Icon,
                ParentId = x.ParentId,
                OrderNo = x.OrderNo
            })
            .ToListAsync();

        return new ClientProfileDto
        {
            UserName = user.UserName,
            NickName = user.NickName,
            RoleId = user.RoleId,
            Menus = menus,
            MustChangePassword = mustChange,
            IsPlatformAdmin = user.IsPlatformAdmin
        };
    }
}
