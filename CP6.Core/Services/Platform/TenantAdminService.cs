using System.Security.Cryptography;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Platform;

/// <summary>
/// <see cref="ITenantAdminService"/> 实现（多租户合规 #5 块①）。
/// <para>建租户原子（R9-a）：<c>Sys_Tenant</c> + 首个 admin 用户走单 <c>SaveChangesAsync</c>，EF 单事务包裹。</para>
/// <para>带外审计（TenantCreated/Updated/Suspended/Reactivated）经 <see cref="TenantScope"/> 强制落平台租户（R5）。</para>
/// </summary>
public class TenantAdminService : ITenantAdminService
{
    private readonly CP6Context _db;
    private readonly IPasswordHasher _hasher;
    private readonly ISecurityAuditService _audit;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUserAccessor? _current;

    public TenantAdminService(
        CP6Context db,
        IPasswordHasher hasher,
        ISecurityAuditService audit,
        ITenantContext tenant,
        ICurrentUserAccessor? current = null)
    {
        _db = db;
        _hasher = hasher;
        _audit = audit;
        _tenant = tenant;
        _current = current;
    }

    public async Task<PagedResult<TenantRow>> ListAsync(string? keyword, bool? enable, int page, int pageSize)
    {
        page = Math.Max(1, page);                 // 防 page<=0 → 负数 Skip 抛异常
        pageSize = Math.Clamp(pageSize, 1, 200);  // 防一次拉全表

        // Sys_Tenant 是共享表（不参与行级过滤），直接查。
        var query = _db.Sys_Tenants.AsQueryable();
        if (!string.IsNullOrWhiteSpace(keyword))
            query = query.Where(t => t.TenantCode.Contains(keyword) || t.TenantName.Contains(keyword));
        if (enable.HasValue)
            query = query.Where(t => t.Enable == enable.Value);

        var total = await query.CountAsync();
        var rows = await query
            .OrderByDescending(t => t.CreateDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new TenantRow(
                t.Id,
                t.TenantCode,
                t.TenantName,
                t.Enable,
                t.ExpireDate,
                // 跨租户用户数：Sys_User 受全局过滤 → IgnoreQueryFilters 数所有租户
                _db.Sys_Users.IgnoreQueryFilters().Count(u => u.TenantId == t.Id),
                t.CreateDate))
            .ToListAsync();

        return new PagedResult<TenantRow>(rows, total);
    }

    public async Task<TenantDetail?> GetAsync(Guid id)
    {
        var t = await _db.Sys_Tenants.FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return null;
        var userCount = await _db.Sys_Users.IgnoreQueryFilters().CountAsync(u => u.TenantId == id);
        return new TenantDetail(t.Id, t.TenantCode, t.TenantName, t.Enable, t.ExpireDate, t.Remark, userCount, t.TimeZoneId);
    }

    public async Task<CreateTenantResult> CreateAsync(string code, string name, DateTime? expire, string? remark, string adminUserName)
    {
        // 1. 编码冲突校验（共享表，不需要 IgnoreQueryFilters）。
        if (await _db.Sys_Tenants.AnyAsync(t => t.TenantCode == code))
            throw new InvalidOperationException("E-SEC-033");

        // 2. 新租户。
        var tenant = new Sys_Tenant
        {
            Id = Guid.NewGuid(),
            TenantCode = code,
            TenantName = name,
            Enable = true,
            ExpireDate = expire,
            Remark = remark
        };

        // 3. 一次性临时密码（16 字符，混大小写+数字+符号）。
        var tempPwd = GenerateRandomPassword(16);

        // 4. 首个 admin：显式落新租户 TenantId（不靠 StampTenant），强制改密。
        var admin = new Sys_User
        {
            Id = Guid.NewGuid(),
            UserName = adminUserName,
            Password = _hasher.Hash(tempPwd),
            TenantId = tenant.Id,
            RoleId = 1,
            IsPlatformAdmin = false,
            Enable = true,
            MustChangePassword = true
        };

        // 4b. P0-T3：新租户预置默认管理员角色（RoleId=1，与 admin.RoleId 对应）。
        //     Sys_Role 已租户化（复合主键 (TenantId,RoleId)）→ 每租户须自带角色集，否则新租户 admin 无角色可解析。
        var adminRole = new Sys_Role
        {
            TenantId = tenant.Id,
            RoleId = 1,
            RoleName = "管理员",
            Description = "拥有全部权限",
            Enable = true,
            OrderNo = 0,
        };

        // 4c. P0-T3 补口：Sys_RoleMenu 已租户化 → 新租户管理员角色须自带菜单映射副本
        //     （从默认租户 RoleId=1 的映射集复制；否则新租户 admin 登录后菜单为空）。
        var defaultAdminMenuIds = await _db.Sys_RoleMenus.IgnoreQueryFilters()
            .Where(rm => rm.TenantId == TenantContext.DefaultTenant && rm.RoleId == 1)
            .Select(rm => rm.MenuId)
            .Distinct()
            .ToListAsync();
        var adminMenus = defaultAdminMenuIds
            .Select(mid => new Sys_RoleMenu { TenantId = tenant.Id, RoleId = 1, MenuId = mid })
            .ToList();

        // 5. 单 SaveChanges → EF 单事务包裹 → 任一失败整体回滚（R9-a）。
        _db.Sys_Tenants.Add(tenant);
        _db.Sys_Roles.Add(adminRole);
        _db.Sys_RoleMenus.AddRange(adminMenus);
        _db.Sys_Users.Add(admin);
        await _db.SaveChangesAsync();

        // 6. 带外审计落平台租户（R5）。
        using (new TenantScope(_tenant, TenantContext.DefaultTenant))
        {
            await _audit.LogAsync(SecurityEventType.TenantCreated, _current?.UserId, _current?.UserName,
                code, null, null, $"admin={adminUserName}");
        }

        // 7. 临时明文仅本次返回。
        return new CreateTenantResult(tenant.Id, adminUserName, tempPwd);
    }

    public async Task UpdateAsync(Guid id, string name, DateTime? expire, string? remark, string? timeZoneId = null)
    {
        var t = await _db.Sys_Tenants.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("E-SEC-032");
        // E-T2：TimeZoneId 保存前校验（E-WF-028 家族）。空串/空白 → 清空（沿用 app 默认）；
        // 非空且不可解析（既非 IANA 亦非 Windows id）→ 拒绝保存，不落任一字段。
        var normalizedTz = string.IsNullOrWhiteSpace(timeZoneId) ? null : timeZoneId.Trim();
        if (normalizedTz != null)
        {
            try { TimeZoneInfo.FindSystemTimeZoneById(normalizedTz); }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            { throw new InvalidOperationException("E-WF-028"); }
        }
        t.TenantName = name;
        t.ExpireDate = expire;
        t.Remark = remark;
        t.TimeZoneId = normalizedTz;
        await _db.SaveChangesAsync();

        using (new TenantScope(_tenant, TenantContext.DefaultTenant))
        {
            await _audit.LogAsync(SecurityEventType.TenantUpdated, _current?.UserId, _current?.UserName,
                t.TenantCode, null, null, null);
        }
    }

    public async Task SuspendAsync(Guid id) => await SetEnableAsync(id, false, SecurityEventType.TenantSuspended);

    public async Task ReactivateAsync(Guid id) => await SetEnableAsync(id, true, SecurityEventType.TenantReactivated);

    private async Task SetEnableAsync(Guid id, bool enable, SecurityEventType evt)
    {
        var t = await _db.Sys_Tenants.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("E-SEC-032");
        t.Enable = enable;
        await _db.SaveChangesAsync();

        using (new TenantScope(_tenant, TenantContext.DefaultTenant))
        {
            await _audit.LogAsync(evt, _current?.UserId, _current?.UserName, t.TenantCode, null, null, null);
        }
    }

    // 16 字符随机临时密码：四类字符（大写/小写/数字/符号）各保证至少一个，其余随机，洗牌去除可预测位置。
    private static readonly char[] Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ".ToCharArray();
    private static readonly char[] Lower = "abcdefghijkmnpqrstuvwxyz".ToCharArray();
    private static readonly char[] Digit = "23456789".ToCharArray();
    private static readonly char[] Symbol = "!@#$%^&*-_=+".ToCharArray();

    private static string GenerateRandomPassword(int length)
    {
        if (length < 4) length = 4;
        var all = Upper.Concat(Lower).Concat(Digit).Concat(Symbol).ToArray();
        var chars = new char[length];
        chars[0] = Pick(Upper);
        chars[1] = Pick(Lower);
        chars[2] = Pick(Digit);
        chars[3] = Pick(Symbol);
        for (var i = 4; i < length; i++) chars[i] = Pick(all);
        // Fisher–Yates 洗牌（用 CSPRNG），打散四类保底字符的固定位置。
        for (var i = length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
        return new string(chars);
    }

    private static char Pick(char[] set) => set[RandomNumberGenerator.GetInt32(set.Length)];
}
