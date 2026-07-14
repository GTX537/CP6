using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wf;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

/// <summary>连接器保存请求（管理端写入 DTO）。<see cref="AuthJson"/> 为明文凭证，仅在本服务边界内经
/// DataProtection 加密落库；读端点绝不回显。</summary>
public sealed class WfConnectorSaveReq
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    /// <summary>明文认证 JSON（{type:"bearer|basic|apiKey", ...}）。CreateAsync：空→AuthJsonEncrypted=null；
    /// UpdateAsync：空→保留原密文（掩码读契约：编辑元数据不得清空凭证）。</summary>
    public string? AuthJson { get; set; }
    public int TimeoutSec { get; set; } = 30;
    public bool Enabled { get; set; }
}

/// <summary>连接器掩码视图（读端点 DTO）。<see cref="HasAuth"/> 指示是否已配置凭证；<see cref="AuthJson"/> 恒为 null
/// （明文绝不出服务边界，与 <see cref="TenantSsoConfigService"/> 掩码口径一致）。</summary>
public sealed class WfConnectorView
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string BaseUrl { get; init; } = "";
    public int TimeoutSec { get; init; }
    public bool Enabled { get; init; }
    public bool HasAuth { get; init; }
    /// <summary>掩码占位：读路径恒为 null，永不回显明文凭证。</summary>
    public string? AuthJson => null;
}

/// <summary>租户级连接器管理服务（WFS infra ④ / spec §5）。
/// DataProtection 加密写（purpose="Wfs.Connector.Auth"）+ 掩码读 + 执行侧解密 + E-WF-028 保存校验。
/// 明文凭证只在 <see cref="CreateAsync"/>/<see cref="UpdateAsync"/> 入参与 <see cref="DecryptAuthAsync"/> 出参出现，
/// 绝不进入 <see cref="WfConnectorView"/> 或日志。</summary>
public interface IWfConnectorService
{
    Task<Guid> CreateAsync(WfConnectorSaveReq req, CancellationToken ct = default);
    Task UpdateAsync(Guid id, WfConnectorSaveReq req, CancellationToken ct = default);
    Task<WfConnectorView?> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<WfConnectorView>> ListAsync(CancellationToken ct = default);
    Task SetEnabledAsync(Guid id, bool enabled, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    /// <summary>执行侧解密：返回明文认证 JSON（无凭证→null）。仅供服务任务执行链路，绝不回给读端点。</summary>
    Task<string?> DecryptAuthAsync(Guid id, CancellationToken ct = default);
}

/// <inheritdoc cref="IWfConnectorService"/>
public sealed class WfConnectorService : IWfConnectorService
{
    private readonly CP6Context _db;
    private readonly IDataProtector _protector;
    private readonly WfsInfraOptions _opts;
    /// <summary>租约时长（秒）。E-WF-028 保存校验与之比对——生产由 DI 传
    /// <see cref="WfServiceJobService.LeaseDuration"/> 同源常量（300s），单测注入。</summary>
    private readonly int _leaseSeconds;

    public WfConnectorService(CP6Context db, IDataProtectionProvider dp, WfsInfraOptions opts, int leaseSeconds)
    {
        _db = db;
        // 固定 purpose 串（spec §5.2）；与资源解析侧 TenantConnectorResolver 同串，故服务加密/解析解密互通
        _protector = dp.CreateProtector("Wfs.Connector.Auth");
        _opts = opts;
        _leaseSeconds = leaseSeconds;
    }

    public async Task<Guid> CreateAsync(WfConnectorSaveReq req, CancellationToken ct = default)
    {
        ValidateLease(req.TimeoutSec);
        var row = new Wf_Connector
        {
            Name = req.Name,
            DisplayName = req.DisplayName,
            BaseUrl = req.BaseUrl,
            TimeoutSec = req.TimeoutSec,
            Enabled = req.Enabled,
            // 空明文→null（无认证连接器）；非空→加密
            AuthJsonEncrypted = string.IsNullOrEmpty(req.AuthJson) ? null : _protector.Protect(req.AuthJson),
        };
        _db.Wf_Connectors.Add(row);
        await _db.SaveChangesAsync(ct);
        return row.Id;
    }

    public async Task UpdateAsync(Guid id, WfConnectorSaveReq req, CancellationToken ct = default)
    {
        ValidateLease(req.TimeoutSec);
        var row = await _db.Wf_Connectors.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new InvalidOperationException($"Wf_Connector not found: {id}");
        row.Name = req.Name;
        row.DisplayName = req.DisplayName;
        row.BaseUrl = req.BaseUrl;
        row.TimeoutSec = req.TimeoutSec;
        row.Enabled = req.Enabled;
        // 空明文=保留原密文（掩码读契约：读端点看不到明文，编辑表单空提交不得清空凭证；SSO UpsertAsync 同向）。
        // 非空=重新加密覆盖。
        if (!string.IsNullOrEmpty(req.AuthJson))
            row.AuthJsonEncrypted = _protector.Protect(req.AuthJson);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<WfConnectorView?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var row = await _db.Wf_Connectors.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return row == null ? null : ToView(row);
    }

    public async Task<IReadOnlyList<WfConnectorView>> ListAsync(CancellationToken ct = default)
    {
        var rows = await _db.Wf_Connectors.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);
        return rows.Select(ToView).ToList();
    }

    public async Task SetEnabledAsync(Guid id, bool enabled, CancellationToken ct = default)
    {
        var row = await _db.Wf_Connectors.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new InvalidOperationException($"Wf_Connector not found: {id}");
        row.Enabled = enabled;
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var row = await _db.Wf_Connectors.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row == null) return;
        _db.Wf_Connectors.Remove(row);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<string?> DecryptAuthAsync(Guid id, CancellationToken ct = default)
    {
        var row = await _db.Wf_Connectors.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row == null || string.IsNullOrEmpty(row.AuthJsonEncrypted)) return null;
        return _protector.Unprotect(row.AuthJsonEncrypted);
    }

    /// <summary>E-WF-028：TimeoutSec ≥ 租约 → 拒绝（spec §5「保存时前移波①启动护栏」，与
    /// <see cref="WfConnectorLeaseGuard"/> 同向：单次调用上界必须严格小于租约，否则 reaper 误判崩溃重投→重复外呼）。
    /// 另做基本值域下限校验（TimeoutSec ≥ 1）。</summary>
    private void ValidateLease(int timeoutSec)
    {
        if (timeoutSec < 1)
            throw new InvalidOperationException($"E-WF-028|timeoutOutOfRange:{timeoutSec}");
        if (timeoutSec >= _leaseSeconds)
            throw new InvalidOperationException(
                $"E-WF-028|timeoutGteLease:{timeoutSec}>={_leaseSeconds}");
    }

    private static WfConnectorView ToView(Wf_Connector r) => new()
    {
        Id = r.Id,
        Name = r.Name,
        DisplayName = r.DisplayName,
        BaseUrl = r.BaseUrl,
        TimeoutSec = r.TimeoutSec,
        Enabled = r.Enabled,
        HasAuth = !string.IsNullOrEmpty(r.AuthJsonEncrypted),
    };
}
