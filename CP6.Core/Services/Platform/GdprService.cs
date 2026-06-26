using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Entity;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Platform;

/// <summary>
/// <see cref="IGdprService"/> 实现（多租户合规 #5 块③ T7）。
/// <para><b>导出</b>双粒度（整租户 / 单主体）均经 <see cref="SensitiveFieldPolicy.IsSensitive"/> 逐行剔密钥
/// （投影到 <c>Dictionary&lt;string,object?&gt;</c> 排除敏感属性）→ <c>System.Text.Json</c> 序列化 → <c>MemoryStream</c>。</para>
/// <para><b>擦除</b>：单主体匿名化（<see cref="SensitiveFieldPolicy.EraseSubject"/> + RevokeAll refresh + 停用，保行保 Id）；
/// 整租户 <c>anonymize</c>（逐 PII 实体反射擦 + 停租户）/ <c>purge</c>（R6 拓扑 + <c>ExecuteDeleteAsync</c> 单 SQL + relational 事务，
/// InMemory 抛 <see cref="NotSupportedException"/> 降级）。</para>
/// <para>防护：擦平台租户/平台超管 → E-SEC-036；擦最后一个启用平台超管 → E-SEC-037；mode 非法 → E-SEC-038。</para>
/// <para>审计（27~30）经 <see cref="TenantScope"/> 强制落平台租户（R5）。</para>
/// </summary>
public class GdprService : IGdprService
{
    private readonly CP6Context _db;
    private readonly IPasswordHasher _hasher;
    private readonly IRefreshTokenService _refresh;
    private readonly ITokenBlacklistService _blacklist;
    private readonly ISecurityAuditService _audit;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUserAccessor? _current;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    public GdprService(
        CP6Context db,
        IPasswordHasher hasher,
        IRefreshTokenService refresh,
        ITokenBlacklistService blacklist,
        ISecurityAuditService audit,
        ITenantContext tenant,
        ICurrentUserAccessor? current = null)
    {
        _db = db;
        _hasher = hasher;
        _refresh = refresh;
        _blacklist = blacklist;
        _audit = audit;
        _tenant = tenant;
        _current = current;
    }

    // ─────────────────────────── 导出（整租户）───────────────────────────

    public async Task<Stream> ExportTenantAsync(Guid tenantId)
    {
        var tenant = await _db.Sys_Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == tenantId)
            ?? throw new InvalidOperationException("E-SEC-032");

        var data = new Dictionary<string, object?>();
        foreach (var clr in TenantPurgeTopology.GetOwnerEntityTypes(_db.Model))
        {
            var rows = await QueryByTenantAsync(clr, tenantId);
            if (rows.Count == 0) continue;
            data[clr.Name] = rows.Select(StripSensitive).ToList();
        }

        var package = new
        {
            tenant = StripSensitive(tenant),
            exportedAt = DateTime.UtcNow,
            data
        };

        var stream = Serialize(package);
        await AuditAsync(SecurityEventType.GdprTenantExported, tenant.TenantCode, $"tenant={tenant.TenantCode}");
        return stream;
    }

    // ─────────────────────────── 导出（单主体）───────────────────────────

    public async Task<Stream> ExportSubjectAsync(Guid userId)
    {
        var user = await _db.Sys_Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new InvalidOperationException("E-SEC-032");

        var securityLogs = await _db.Sys_SecurityLogs.IgnoreQueryFilters()
            .Where(l => l.UserId == userId).ToListAsync();
        var operLogs = await _db.Sys_OperLogs.IgnoreQueryFilters()
            .Where(l => l.UserName == user.UserName).ToListAsync();

        // 注：Creator/Modifier == user.UserName 的全表反扫为可选 best-effort，本期从简不做
        //（数据量与遍历成本高，主体导出聚焦用户身份 + 其登录/操作留痕，足覆盖 GDPR 访问权核心）。
        var package = new
        {
            user = StripSensitive(user),
            securityLogs = securityLogs.Select(StripSensitive).ToList(),
            operLogs = operLogs.Select(StripSensitive).ToList(),
            exportedAt = DateTime.UtcNow
        };

        var stream = Serialize(package);
        await AuditAsync(SecurityEventType.GdprSubjectExported, null, $"user={user.UserName}");
        return stream;
    }

    // ─────────────────────────── 擦除（单主体）───────────────────────────

    public async Task EraseSubjectAsync(Guid userId)
    {
        var user = await _db.Sys_Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new InvalidOperationException("E-SEC-032");

        // 防护：不擦平台超管（(a) 平台租户超管 → E-SEC-036；(b) 最后一个启用超管 → E-SEC-037）。
        if (user.IsPlatformAdmin)
        {
            if (user.TenantId == TenantContext.DefaultTenant)
                throw new InvalidOperationException("E-SEC-036");
            var enabledAdmins = await _db.Sys_Users.IgnoreQueryFilters()
                .CountAsync(u => u.IsPlatformAdmin && u.Enable);
            if (enabledAdmins == 1)
                throw new InvalidOperationException("E-SEC-037");
        }

        SensitiveFieldPolicy.EraseSubject(_db, user, _hasher);

        // 吊销 refresh 令牌族（access TTL 内仍可用 → 自然过期；§10 局限）。
        await _refresh.RevokeAllForUserAsync(userId);

        await _db.SaveChangesAsync();

        await AuditAsync(SecurityEventType.GdprSubjectErased, null, $"user-id={userId:N}");
    }

    // ─────────────────────────── 擦除（整租户）───────────────────────────

    public async Task EraseTenantAsync(Guid tenantId, string mode)
    {
        if (tenantId == TenantContext.DefaultTenant)
            throw new InvalidOperationException("E-SEC-036");   // 不擦平台租户
        if (mode != "anonymize" && mode != "purge")
            throw new InvalidOperationException("E-SEC-038");   // 非法 mode

        var tenant = await _db.Sys_Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == tenantId)
            ?? throw new InvalidOperationException("E-SEC-032");

        if (mode == "anonymize")
            await AnonymizeTenantAsync(tenantId, tenant);
        else
            await PurgeTenantAsync(tenantId);

        await AuditAsync(SecurityEventType.GdprTenantErased, tenant.TenantCode, $"{mode}:{tenant.TenantCode}");
    }

    private async Task AnonymizeTenantAsync(Guid tenantId, Sys_Tenant tenant)
    {
        foreach (var clr in TenantPurgeTopology.GetOwnerEntityTypes(_db.Model))
        {
            // 仅处理含 [PiiField] 列的实体（无 PII 行不动）。
            var piiProps = clr.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanWrite && p.GetCustomAttribute<PiiFieldAttribute>() != null)
                .ToList();
            var isUser = clr == typeof(Sys_User);
            if (piiProps.Count == 0 && !isUser) continue;

            var rows = await QueryByTenantAsync(clr, tenantId);
            foreach (var row in rows)
            {
                if (isUser)
                {
                    // 用户走完整匿名化（含 UserName/Password/Enable）。
                    SensitiveFieldPolicy.EraseSubject(_db, (Sys_User)row, _hasher);
                }
                else
                {
                    foreach (var p in piiProps)
                    {
                        var pii = p.GetCustomAttribute<PiiFieldAttribute>()!;
                        object? newValue = pii.Mode == PiiErase.Null
                            ? null
                            : $"REDACTED-{tenantId.ToString("N")[..8]}";
                        p.SetValue(row, newValue);
                    }
                }
            }
        }

        tenant.Enable = false;   // 停租户
        await _db.SaveChangesAsync();
    }

    private async Task PurgeTenantAsync(Guid tenantId)
    {
        if (!_db.Database.IsRelational())
            throw new NotSupportedException("purge requires relational DB; use anonymize for tests");

        var (order, cycleNodes) = TenantPurgeTopology.BuildDeleteOrder(_db.Model);

        await using var tx = await _db.Database.BeginTransactionAsync();

        // 1. 先打断自引用环：把 cycleNodes 的自指 FK 列 null 化（按 TenantId 过滤）。
        foreach (var clr in cycleNodes)
            await NullSelfReferenceAsync(clr, tenantId);

        // 2. leaf-first 删除每个 owner 表的本租户行（单 SQL 批量，无 ChangeTracker 装入）。
        foreach (var clr in order)
            await ExecuteDeleteByTenantAsync(clr, tenantId);

        // 3. 最后删 Sys_Tenant 行本身。
        await _db.Sys_Tenants.Where(t => t.Id == tenantId).ExecuteDeleteAsync();

        await tx.CommitAsync();
    }

    // ─────────────────────────── 反射查询/删除工具 ───────────────────────────

    /// <summary>反射构建 <c>_db.Set(clr).IgnoreQueryFilters().Where(EF.Property&lt;Guid&gt;(e,"TenantId")==tenantId)</c>
    /// 并 ToListAsync，返回 object 列表。</summary>
    private async Task<List<object>> QueryByTenantAsync(Type clr, Guid tenantId)
    {
        var query = BuildTenantQueryable(clr, tenantId);
        return await ToObjectListAsync(query, clr);
    }

    /// <summary>构建按 TenantId 过滤的 <see cref="IQueryable"/>（已 IgnoreQueryFilters）。</summary>
    private IQueryable BuildTenantQueryable(Type clr, Guid tenantId)
    {
        // _db.Set<T>()（反射构造泛型 DbSet → IQueryable；EF Core 无公开非泛型 Set(Type)）
        var setGeneric = typeof(DbContext).GetMethods()
            .First(m => m.Name == nameof(DbContext.Set) && m.IsGenericMethod && m.GetParameters().Length == 0)
            .MakeGenericMethod(clr);
        var set = (IQueryable)setGeneric.Invoke(_db, null)!;
        // .IgnoreQueryFilters()（按名字 + 泛型构造，避开 EF8 重载歧义）
        var ignoreGeneric = typeof(EntityFrameworkQueryableExtensions).GetMethods()
            .First(m => m.Name == nameof(EntityFrameworkQueryableExtensions.IgnoreQueryFilters)
                        && m.GetParameters().Length == 1)
            .MakeGenericMethod(clr);
        var ignored = (IQueryable)ignoreGeneric.Invoke(null, new object[] { set })!;

        // .Where(e => EF.Property<Guid>(e, "TenantId") == tenantId)
        var param = Expression.Parameter(clr, "e");
        var efProperty = typeof(EF).GetMethod(nameof(EF.Property))!.MakeGenericMethod(typeof(Guid));
        var propAccess = Expression.Call(efProperty, param, Expression.Constant("TenantId"));
        var equal = Expression.Equal(propAccess, Expression.Constant(tenantId));
        var lambda = Expression.Lambda(equal, param);

        var whereGeneric = typeof(Queryable).GetMethods()
            .First(m => m.Name == nameof(Queryable.Where)
                        && m.GetParameters().Length == 2
                        && m.GetParameters()[1].ParameterType.GetGenericArguments()[0].GetGenericArguments().Length == 2)
            .MakeGenericMethod(clr);
        var filtered = (IQueryable)whereGeneric.Invoke(null, new object[] { ignored, lambda })!;
        return filtered;
    }

    /// <summary>反射 ToListAsync(IQueryable&lt;T&gt;) → List&lt;object&gt;。</summary>
    private static async Task<List<object>> ToObjectListAsync(IQueryable query, Type clr)
    {
        var toListAsync = typeof(EntityFrameworkQueryableExtensions).GetMethods()
            .First(m => m.Name == nameof(EntityFrameworkQueryableExtensions.ToListAsync)
                        && m.GetParameters().Length == 2)
            .MakeGenericMethod(clr);
        var task = (Task)toListAsync.Invoke(null, new object[] { query, CancellationToken.None })!;
        await task.ConfigureAwait(false);
        var resultProp = task.GetType().GetProperty("Result")!;
        var list = (System.Collections.IEnumerable)resultProp.GetValue(task)!;
        return list.Cast<object>().ToList();
    }

    /// <summary>反射 <c>_db.Set(clr).IgnoreQueryFilters().Where(TenantId==..).ExecuteDeleteAsync()</c>。</summary>
    private async Task ExecuteDeleteByTenantAsync(Type clr, Guid tenantId)
    {
        var query = BuildTenantQueryable(clr, tenantId);
        var exec = typeof(RelationalQueryableExtensions).GetMethods()
            .First(m => m.Name == nameof(RelationalQueryableExtensions.ExecuteDeleteAsync)
                        && m.GetParameters().Length == 2)
            .MakeGenericMethod(clr);
        var task = (Task)exec.Invoke(null, new object[] { query, CancellationToken.None })!;
        await task.ConfigureAwait(false);
    }

    /// <summary>把自引用 FK 列（探测：指向自身且唯一外键属性）按 TenantId 置 null（打断环）。
    /// 用 raw SQL 兜底（ExecuteUpdate 反射构造 SetProperty 复杂），按列名直拼安全（列名来自模型元数据，无注入）。</summary>
    private async Task NullSelfReferenceAsync(Type clr, Guid tenantId)
    {
        var et = _db.Model.FindEntityType(clr);
        if (et == null) return;
        var tableName = et.GetTableName();
        if (tableName == null) return;

        foreach (var fk in et.GetForeignKeys())
        {
            if (fk.PrincipalEntityType.ClrType != clr) continue;     // 仅自引用 FK
            foreach (var prop in fk.Properties)
            {
                var col = prop.GetColumnName();
                if (col == null) continue;
                // 列名/表名来自 EF 模型元数据（非用户输入），且 tenantId 走参数化 → 无注入面。
                var sql = $"UPDATE [{tableName}] SET [{col}] = NULL WHERE [TenantId] = {{0}}";
                await _db.Database.ExecuteSqlRawAsync(sql, tenantId);
            }
        }
    }

    // ─────────────────────────── 序列化 / 剔密钥 / 审计 ───────────────────────────

    /// <summary>投影实体到 <c>Dictionary&lt;string,object?&gt;</c>，排除密钥字段（剔出后不入 JSON）。</summary>
    private static Dictionary<string, object?> StripSensitive(object entity)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var p in entity.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!p.CanRead) continue;
            if (p.GetIndexParameters().Length > 0) continue;      // 跳过索引器
            if (SensitiveFieldPolicy.IsSensitive(p.Name)) continue;
            // 仅取标量/字符串/值类型/可空，避免拉入导航属性（EF 代理/集合）造成循环。
            if (!IsScalar(p.PropertyType)) continue;
            dict[p.Name] = p.GetValue(entity);
        }
        return dict;
    }

    private static bool IsScalar(Type t)
    {
        var u = Nullable.GetUnderlyingType(t) ?? t;
        return u.IsPrimitive || u.IsEnum
            || u == typeof(string) || u == typeof(Guid) || u == typeof(DateTime)
            || u == typeof(DateTimeOffset) || u == typeof(decimal) || u == typeof(TimeSpan);
    }

    private static MemoryStream Serialize(object package)
    {
        var json = JsonSerializer.Serialize(package, JsonOpts);
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        stream.Position = 0;
        return stream;
    }

    /// <summary>审计落平台租户（R5，TenantScope）。</summary>
    private async Task AuditAsync(SecurityEventType type, string? requestTenantCode, string? reason)
    {
        using (new TenantScope(_tenant, TenantContext.DefaultTenant))
        {
            await _audit.LogAsync(type, _current?.UserId, _current?.UserName, requestTenantCode, null, null, reason);
        }
    }
}
