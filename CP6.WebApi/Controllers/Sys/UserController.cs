using CP6.Core.Auth;
using System.ComponentModel.DataAnnotations;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Platform;
using CP6.WebApi.Localization;
using CP6.Entity.DomainModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CP6.WebApi.Services;

namespace CP6.WebApi.Controllers.Sys;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UserController : LocalizedControllerBase
{
    private readonly CP6Context _context;
    private readonly IPasswordHasher _hasher;
    private readonly IRefreshTokenService _refresh;
    private readonly ITwoFactorService _2fa;
    private readonly IamUserAdministrationV1? _admin;

    public UserController(CP6Context context, IPasswordHasher hasher, IRefreshTokenService refresh, ITwoFactorService twoFa, IamUserAdministrationV1? admin = null)
    {
        _context = context;
        _hasher = hasher;
        _refresh = refresh;
        _2fa = twoFa;
        _admin = admin;
    }

    [HttpGet]
    [RequirePermission("user", "query")]
    public async Task<IActionResult> GetList(int page = 1, int pageSize = 10, string? keyword = null)
    {
        var query = _context.Sys_Users.AsQueryable();
        if (!string.IsNullOrEmpty(keyword))
            query = query.Where(u => u.UserName.Contains(keyword) || (u.NickName != null && u.NickName.Contains(keyword)));

        var total = await query.CountAsync();
        var data = await query
            .OrderByDescending(u => u.CreateDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new
            {
                u.Id,
                u.UserName,
                u.NickName,
                u.RoleId,
                u.Enable,
                u.DeptId,       // PUB 章00 组织字段
                u.ManagerId,
                u.Email,
                u.Creator,
                u.CreateDate
            })
            .ToListAsync();

        return Ok(new { rows = data, total });
    }

    [HttpPost]
    [RequirePermission("user", "add")]
    public async Task<IActionResult> Add([FromBody] UserCreateRequest request)
    {
        if (_context.Database.IsNpgsql()) return await Versioned("user-create", new IamUserCommandV1
        { OperationId=request.OperationId,ExpectedVersion=request.ExpectedVersion,ExpectedReferencesVersion=request.ExpectedReferencesVersion,
            Reason=request.Reason,UserName=request.UserName,NickName=request.NickName,Password=request.Password,RoleId=request.RoleId,
            RoleIds=request.RoleIds,Enable=request.Enable,DeptId=request.DeptId,ManagerId=request.ManagerId,Email=request.Email });
        if (await _context.Sys_Users.AnyAsync(u => u.UserName == request.UserName))
            return BadRequest(new { message = Localizer["用户名已存在"] });

        // Ordinary user:add never grants platform or authentication authority. Construct a
        // new identity from the explicit input contract; entity/security/audit fields are not bound.
        var entity = new Sys_User
        {
            Id = Guid.NewGuid(), TenantId = _context.CurrentTenantId,
            UserName = request.UserName, NickName = request.NickName, RoleId = request.RoleId,
            Enable = request.Enable, DeptId = request.DeptId, ManagerId = request.ManagerId, Email = request.Email,
            Password = _hasher.Hash(request.Password), PasswordChangedAt = DateTime.Now,
            MustChangePassword = true, IsPlatformAdmin = false,
            Creator = HttpContext?.User.Identity?.Name, CreateDate = DateTime.Now
        };
        _context.Sys_Users.Add(entity);
        await _context.SaveChangesAsync();
        return Ok(new { entity.Id, entity.UserName, entity.NickName, entity.RoleId, entity.Enable });
    }

    [HttpPut]
    [RequirePermission("user", "edit")]
    public async Task<IActionResult> Update([FromBody] UserUpdateRequest entity)
    {
        if (_context.Database.IsNpgsql()) return await Versioned("user-update", new IamUserCommandV1
        { Id=entity.Id,OperationId=entity.OperationId,ExpectedVersion=entity.ExpectedVersion,ExpectedReferencesVersion=entity.ExpectedReferencesVersion,
            Reason=entity.Reason,UserName=entity.UserName,NickName=entity.NickName,Password=entity.Password,RoleId=entity.RoleId,
            RoleIds=entity.RoleIds,Enable=entity.Enable,DeptId=entity.DeptId,ManagerId=entity.ManagerId,Email=entity.Email });
        var existing = await _context.Sys_Users.FindAsync(entity.Id);
        if (existing == null)
            return NotFound();

        existing.UserName = entity.UserName;
        existing.NickName = entity.NickName;
        existing.RoleId = entity.RoleId;
        existing.Enable = entity.Enable;
        existing.DeptId = entity.DeptId;       // PUB 章00 组织字段
        existing.ManagerId = entity.ManagerId;
        existing.Email = entity.Email;
        existing.ModifyDate = DateTime.Now;

        // S 类认证加固 T7：密码非空才更新，且落 BCrypt（消除明文写入路径，原 I1 遗留已清）。
        // 管理员重置密码 = 强制用户首登改密 + 吊销其全部刷新令牌（强制其它会话下线，防旧凭证沿用）。
        if (!string.IsNullOrEmpty(entity.Password))
        {
            existing.Password = _hasher.Hash(entity.Password);
            existing.PasswordChangedAt = DateTime.Now;
            existing.MustChangePassword = true;
            // saveChanges:false → 与下方用户更新合并一次原子保存（重置成功 ⇔ 旧令牌全失效）
            await _refresh.RevokeAllForUserAsync(existing.Id, saveChanges: false);
        }

        await SaveUserChangesAsync();
        return Ok(new { existing.Id, existing.UserName, existing.NickName, existing.RoleId, existing.Enable });
    }

    /// <summary>管理员重置用户 2FA（清启用/密钥/入会时间，复用 user:edit 权限点）。POST /api/user/reset-2fa</summary>
    [HttpPost("reset-2fa")]
    [RequirePermission("user", "edit")]
    public async Task<IActionResult> ResetTwoFactor([FromBody] Guid userId)
    {
        var u = await _context.Sys_Users.FirstOrDefaultAsync(x => x.Id == userId);
        if (u == null) return NotFound();
        await _2fa.ResetAsync(u, "admin-reset");
        await _context.SaveChangesAsync();
        return Ok(new { code = 0 });
    }

    [HttpDelete]
    [RequirePermission("user", "delete")]
    public async Task<IActionResult> Delete([FromBody] Guid[] ids)
    {
        if (_context.Database.IsNpgsql()) return Conflict(new { code = "IAM_DELETE_REQUIRES_RETENTION_REVIEW", alternative = "disable" });
        var entities = await _context.Sys_Users.Where(u => ids.Contains(u.Id)).ToListAsync();
        _context.Sys_Users.RemoveRange(entities);
        var count = await SaveUserChangesAsync();
        return Ok(new { count });
    }

    private async Task<int> SaveUserChangesAsync()
    {
        try { return await _context.SaveChangesAsync(HttpContext?.RequestAborted ?? CancellationToken.None); }
        catch (DbUpdateException error) when (PlatformAdminMutationV1.IsSurvivorViolation(error))
        { throw new BizException("E-SEC-037"); }
    }
    private Task<IActionResult> Versioned(string kind,IamUserCommandV1 command)
        => IamAdministrationHttpV1.Execute(this,async () =>
        {
            IamAdministrationV1.Validate(command);
            if (_admin == null) throw new IamAdministrationExceptionV1(503,"IAM_AUTHORITY_UNAVAILABLE");
            return (object)await _admin.ChangeAsync(kind,command,ct=>IamAdministrationHttpV1.CurrentActor(HttpContext,ct),HttpContext.RequestAborted);
        });
}

/// <summary>Ordinary creation input. Identity, tenant, authentication state and audit metadata are server owned.</summary>
public sealed class UserCreateRequest
{
    public Guid OperationId { get; set; }
    public string? ExpectedVersion { get; set; }
    public string? ExpectedReferencesVersion { get; set; }
    public string? Reason { get; set; }
    public int[]? RoleIds { get; set; }
    [Required, MaxLength(100)] public string UserName { get; set; } = "";
    [Required, MaxLength(200)] public string Password { get; set; } = "";
    [MaxLength(100)] public string? NickName { get; set; }
    public int? RoleId { get; set; }
    public bool Enable { get; set; } = true;
    public Guid? DeptId { get; set; }
    public Guid? ManagerId { get; set; }
    [MaxLength(100)] public string? Email { get; set; }
}

/// <summary>Editing identity attributes does not require resetting the user's password.</summary>
public sealed class UserUpdateRequest
{
    public Guid OperationId { get; set; }
    public string? ExpectedVersion { get; set; }
    public string? ExpectedReferencesVersion { get; set; }
    public string? Reason { get; set; }
    public int[]? RoleIds { get; set; }
    public Guid Id { get; set; }
    [Required, MaxLength(100)] public string UserName { get; set; } = "";
    [MaxLength(200)] public string? Password { get; set; }
    [MaxLength(100)] public string? NickName { get; set; }
    public int? RoleId { get; set; }
    public bool Enable { get; set; } = true;
    public Guid? DeptId { get; set; }
    public Guid? ManagerId { get; set; }
    [MaxLength(100)] public string? Email { get; set; }
}
