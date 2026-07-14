using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CP6.Core.EFDbContext;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf.Executors;

/// <summary>连接器目录项（合并两源，管理/设计器目录用）。</summary>
public sealed class ConnectorCatalogItem
{
    public string Name { get; init; } = "";
    public string DisplayName { get; init; } = "";
    /// <summary>来源："tenant"（Wf_Connector 行）或 "app"（DI 注册连接器）。</summary>
    public string Source { get; init; } = "";
}

/// <summary>
/// 连接器解析器（WFS infra ④ / spec §5 D5）：解析 name 时<b>先查租户表</b>（<see cref="Wf_Connector"/> Enabled 行 →
/// 包成动态 <see cref="DbWfConnector"/>）→ 未命中回落 app 级 DI 注册字典。目录合并两源、租户行按 Name 去重优先。
/// <para>租户表查询走当前 scoped <see cref="CP6Context"/> 的全局租户过滤，故天然按 <c>ITenantContext.CurrentTenantId</c>
/// 隔离（worker 经 <c>TenantScopeRunner</c> 逐租户切换）。</para>
/// <para>app-only 模式：<paramref name="db"/> 为 null 时跳过租户表，纯 app 字典解析——供 <see cref="WebApiExecutor"/>
/// 旧构造（既有单测）零改写复用。</para>
/// </summary>
public sealed class TenantConnectorResolver
{
    private readonly CP6Context? _db;
    private readonly Dictionary<string, IWfConnector> _appConnectors;
    private readonly IDataProtector? _protector;
    private readonly IHttpClientFactory? _httpFactory;

    public TenantConnectorResolver(
        CP6Context? db,
        IEnumerable<IWfConnector> appConnectors,
        IDataProtectionProvider? dp = null,
        IHttpClientFactory? httpFactory = null)
    {
        _db = db;
        _appConnectors = new Dictionary<string, IWfConnector>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in appConnectors ?? Enumerable.Empty<IWfConnector>())
            _appConnectors[c.Name] = c;
        // 与 WfConnectorService 加密同 purpose 串，故服务端加密 / 解析端解密互通
        _protector = dp?.CreateProtector("Wfs.Connector.Auth");
        _httpFactory = httpFactory;
    }

    /// <summary>解析连接器：租户 Enabled 行优先（→ DbWfConnector）→ app 字典兜底 → 均未命中返回 null。</summary>
    public async Task<IWfConnector?> ResolveAsync(string name, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(name)) return null;

        if (_db != null)
        {
            var row = await _db.Wf_Connectors.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Name == name && c.Enabled, ct);
            if (row != null)
            {
                string? auth = null;
                if (!string.IsNullOrEmpty(row.AuthJsonEncrypted))
                {
                    // 解密需 provider——生产/单测都传入；app-only 模式不会走到此分支
                    if (_protector == null)
                        throw new InvalidOperationException(
                            "TenantConnectorResolver：解析加密连接器需 IDataProtectionProvider（app-only 模式不支持租户行）");
                    auth = _protector.Unprotect(row.AuthJsonEncrypted);
                }
                if (_httpFactory == null)
                    throw new InvalidOperationException(
                        "TenantConnectorResolver：解析租户连接器需 IHttpClientFactory");
                return new DbWfConnector(row, auth, _httpFactory);
            }
        }

        return _appConnectors.TryGetValue(name, out var app) ? app : null;
    }

    /// <summary>目录合并：租户 Enabled 行 + app 注册，租户行按 Name 去重优先；app-only 项保留。</summary>
    public async Task<IReadOnlyList<ConnectorCatalogItem>> ListCatalogAsync(CancellationToken ct = default)
    {
        var byName = new Dictionary<string, ConnectorCatalogItem>(StringComparer.OrdinalIgnoreCase);

        if (_db != null)
        {
            var rows = await _db.Wf_Connectors.AsNoTracking().Where(c => c.Enabled).ToListAsync(ct);
            foreach (var r in rows)
                byName[r.Name] = new ConnectorCatalogItem { Name = r.Name, DisplayName = r.DisplayName, Source = "tenant" };
        }

        foreach (var kv in _appConnectors)
            if (!byName.ContainsKey(kv.Key))
                byName[kv.Key] = new ConnectorCatalogItem { Name = kv.Value.Name, DisplayName = kv.Value.DisplayName, Source = "app" };

        return byName.Values.ToList();
    }
}
