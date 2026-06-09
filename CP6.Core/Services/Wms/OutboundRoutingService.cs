using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

/// <inheritdoc />
public class OutboundRoutingService : IOutboundRoutingService
{
    private readonly CP6Context _db;

    public OutboundRoutingService(CP6Context db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<List<string>> ResolveCandidateWarehousesAsync(
        string? customerCd,
        string productCd,
        int outboundType,
        string fallbackWarehouseCd,
        CancellationToken ct = default)
    {
        var ordered = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? cd)
        {
            if (string.IsNullOrWhiteSpace(cd)) return;
            if (seen.Add(cd)) ordered.Add(cd);
        }

        // ① マッチしたルール（SortOrder 昇順）の優先倉庫
        //    接頭辞条件は DB 翻訳できる範囲に絞り、StartsWith はメモリ側で評価。
        var rules = await _db.OutboundRoutingRules
            .AsNoTracking()
            .Where(r => r.Enabled && !r.IsDeleted
                        && (r.CustomerCd == null || r.CustomerCd == customerCd)
                        && (r.OutboundType == null || r.OutboundType == outboundType))
            .OrderBy(r => r.SortOrder)
            .ThenBy(r => r.CreateDate)
            .ToListAsync(ct);

        foreach (var r in rules)
        {
            if (!string.IsNullOrEmpty(r.ProductCdPrefix)
                && !productCd.StartsWith(r.ProductCdPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            Add(r.TargetWarehouseCd);
        }

        // ② 残り倉庫を OutboundPriority 昇順
        var warehouses = await _db.Warehouses
            .AsNoTracking()
            .Where(w => !w.IsDeleted)
            .OrderBy(w => w.OutboundPriority)
            .ThenBy(w => w.WarehouseCd)
            .Select(w => w.WarehouseCd)
            .ToListAsync(ct);
        foreach (var w in warehouses) Add(w);

        // ③ 最終フォールバック（ヘッダ倉庫）。候補が皆無の場合の保険も兼ねる。
        Add(fallbackWarehouseCd);

        return ordered;
    }

    // ───────── ルール CRUD ─────────

    public async Task<List<OutboundRoutingRule>> ListRulesAsync(bool includeDisabled = true, CancellationToken ct = default)
    {
        var q = _db.OutboundRoutingRules.AsNoTracking().Where(r => !r.IsDeleted);
        if (!includeDisabled) q = q.Where(r => r.Enabled);
        return await q
            .OrderBy(r => r.SortOrder)
            .ThenBy(r => r.CreateDate)
            .ToListAsync(ct);
    }

    public async Task<OutboundRoutingRule> CreateRuleAsync(OutboundRoutingRule rule, string? userName, CancellationToken ct = default)
    {
        Validate(rule);
        rule.Id = rule.Id == Guid.Empty ? Guid.NewGuid() : rule.Id;
        rule.Creator = userName;
        rule.CreateDate = DateTime.Now;
        rule.IsDeleted = false;
        _db.OutboundRoutingRules.Add(rule);
        await _db.SaveChangesAsync(ct);
        return rule;
    }

    public async Task UpdateRuleAsync(Guid id, OutboundRoutingRule rule, string? userName, CancellationToken ct = default)
    {
        Validate(rule);
        var existing = await _db.OutboundRoutingRules
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted, ct)
            ?? throw new InvalidOperationException("WM-MSG-070: ルーティングルールが見つかりません");

        existing.RuleName = rule.RuleName;
        existing.SortOrder = rule.SortOrder;
        existing.CustomerCd = rule.CustomerCd;
        existing.ProductCdPrefix = rule.ProductCdPrefix;
        existing.OutboundType = rule.OutboundType;
        existing.TargetWarehouseCd = rule.TargetWarehouseCd;
        existing.Enabled = rule.Enabled;
        existing.Remarks = rule.Remarks;
        existing.Modifier = userName;
        existing.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteRuleAsync(Guid id, string? userName, CancellationToken ct = default)
    {
        var existing = await _db.OutboundRoutingRules
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted, ct)
            ?? throw new InvalidOperationException("WM-MSG-070: ルーティングルールが見つかりません");
        existing.IsDeleted = true;
        existing.Modifier = userName;
        existing.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync(ct);
    }

    private static void Validate(OutboundRoutingRule rule)
    {
        if (string.IsNullOrWhiteSpace(rule.RuleName))
            throw new InvalidOperationException("WM-MSG-021: ルール名は必須です");
        if (string.IsNullOrWhiteSpace(rule.TargetWarehouseCd))
            throw new InvalidOperationException("WM-MSG-021: 引当先倉庫CDは必須です");
    }
}

/// <summary>
/// ルーティング無効時（OutboundRouting:Enabled=false）の no-op 実装。
/// 常にフォールバック倉庫のみを返し、従来の単一倉庫引当を維持する。
/// </summary>
public class NoOpOutboundRoutingService : IOutboundRoutingService
{
    public Task<List<string>> ResolveCandidateWarehousesAsync(
        string? customerCd, string productCd, int outboundType, string fallbackWarehouseCd, CancellationToken ct = default)
        => Task.FromResult(string.IsNullOrWhiteSpace(fallbackWarehouseCd)
            ? new List<string>()
            : new List<string> { fallbackWarehouseCd });

    public Task<List<OutboundRoutingRule>> ListRulesAsync(bool includeDisabled = true, CancellationToken ct = default)
        => Task.FromResult(new List<OutboundRoutingRule>());

    public Task<OutboundRoutingRule> CreateRuleAsync(OutboundRoutingRule rule, string? userName, CancellationToken ct = default)
        => throw new InvalidOperationException("多倉庫ルーティングは無効です（OutboundRouting:Enabled=false）");

    public Task UpdateRuleAsync(Guid id, OutboundRoutingRule rule, string? userName, CancellationToken ct = default)
        => throw new InvalidOperationException("多倉庫ルーティングは無効です（OutboundRouting:Enabled=false）");

    public Task DeleteRuleAsync(Guid id, string? userName, CancellationToken ct = default)
        => throw new InvalidOperationException("多倉庫ルーティングは無効です（OutboundRouting:Enabled=false）");
}
