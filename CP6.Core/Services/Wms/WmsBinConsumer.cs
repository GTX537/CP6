using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs.Space;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

/// <summary>
/// WMS 库位消费端真实现（ch04 §5.1/§5.3 v1.1）：LocationPublished → T_WmsBin 幂等 upsert。
/// 幂等判据：按 Id(=Space LocationId) 取行，incoming.Version &lt;= stored.Version → SKIPPED。
/// 整批语义（§5.2）：任一 REJECTED → Success=false（整事件 Failed，Worker 重试/人工介入）；
/// 其余项照常落库（部分失败返回逐项结果）。TenantId 由 SaveChanges 自动盖章。
/// </summary>
public class WmsBinConsumer : IWmsLocationConsumer
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    private readonly CP6Context _db;

    public WmsBinConsumer(CP6Context db) => _db = db;

    /// <inheritdoc/>
    public async Task<WmsConsumeResult> ConsumeAsync(LocationPublishBatch batch)
    {
        var result = new WmsConsumeResult { Success = true };

        // 波5 批量化：循环前三次预载，把旧实现「每 item 三次逐条查询」收敛为整批常数次查询。
        // 逐行为等价的关键是保留旧实现的 Local-first 语义——旧代码先看同批未保存的 Added 实体（Local）、
        // 再落库查；同批前面 item 刚插入的 bin 会被后面 item 看到（批内自碰撞）。故预载后在循环内插入
        // 新 bin 时同步维护字典，命中/版本判定与旧的 Local+DB 双查完全一致。
        // 上游可达性结论（写入报告）：生产调用方 PublishFloorAsync 先跑 PrecheckAsync，DuplicateGroups>0
        // 直接 E-SPACE-307 抛死，且每条 item 源自不同 PK 的库位行——故批内「同 LocationId」「同锚不同
        // LocationId」两条自碰撞路径生产不可达；但消费端契约（及既有 SameBatch 测试）要求保留该语义，
        // 循环内维护字典即为等价保真。
        var binsById = new Dictionary<Guid, WmsBin>();
        var binsByAnchor = new Dictionary<(string WarehouseCd, string LocationCode), WmsBin>();

        // ① bins 预载：按批次 LocationId 集合 ∪ 锚 (WarehouseCd, LocationCode) 集合一次载入，
        //    覆盖旧代码「按 Id 查」与「按锚查」两处 DbSet 命中面；载入即进 Local，连同预先已跟踪的
        //    WmsBin 一并入字典（等价旧 Local-first：Local 已跟踪行优先于 DB）。
        var idSet = batch.Items.Select(i => i.LocationId).ToHashSet();
        var codeSet = batch.Items.Select(i => i.LocationCode).ToHashSet();
        var whSet = batch.Items.Where(i => !string.IsNullOrEmpty(i.WarehouseCd))
            .Select(i => i.WarehouseCd!).ToHashSet();
        await _db.WmsBins
            .Where(b => idSet.Contains(b.Id) || (whSet.Contains(b.WarehouseCd) && codeSet.Contains(b.LocationCode)))
            .LoadAsync();
        foreach (var b in _db.WmsBins.Local)
        {
            binsById[b.Id] = b;
            if (!string.IsNullOrEmpty(b.WarehouseCd) && b.LocationCode != null)
                binsByAnchor[(b.WarehouseCd, b.LocationCode)] = b;
        }

        // ② 库存合计预载：仅 DEACTIVATE 且已有 bin 的项需要按 (bin.WarehouseCd, bin.LocationCode) 锚查库存。
        //    收集这些锚，一次 GroupBy 聚合成字典（旧代码每项一次 SumAsync；SumAsync 空集=0 → GetValueOrDefault 0 等价）。
        //    循环内不写库存，故批开始快照与逐条查询结果一致。
        var deactAnchors = new HashSet<(string WarehouseCd, string LocationCode)>();
        foreach (var item in batch.Items.Where(i => i.Op == "DEACTIVATE"))
        {
            if (binsById.TryGetValue(item.LocationId, out var b)
                && !string.IsNullOrEmpty(b.WarehouseCd) && b.LocationCode != null)
                deactAnchors.Add((b.WarehouseCd, b.LocationCode));
        }
        var stockByAnchor = new Dictionary<(string WarehouseCd, string LocationCode), decimal>();
        if (deactAnchors.Count > 0)
        {
            var stockWh = deactAnchors.Select(a => a.WarehouseCd).ToHashSet();
            var stockCode = deactAnchors.Select(a => a.LocationCode).ToHashSet();
            var grouped = await _db.Stocks
                .Where(s => stockWh.Contains(s.WarehouseCd) && stockCode.Contains(s.LocationCd))
                .GroupBy(s => new { s.WarehouseCd, s.LocationCd })
                .Select(g => new { g.Key.WarehouseCd, g.Key.LocationCd, Qty = g.Sum(x => x.PhysicalQty) })
                .ToListAsync();
            foreach (var g in grouped)
                stockByAnchor[(g.WarehouseCd, g.LocationCd)] = g.Qty;
        }

        foreach (var item in batch.Items)
        {
            // 波5：旧「Local + DB 双查」→ 预载字典命中（新插入的 bin 在循环内已回填 binsById，等价 Local-first）。
            binsById.TryGetValue(item.LocationId, out var bin);

            // 陈旧/重复事件 → 幂等跳过（§5.1 关键行，至少一次投递安全）
            if (bin != null && item.Version <= bin.Version)
            {
                result.Items.Add(Item(item, "SKIPPED", "version<=lastVersion（幂等重复）"));
                continue;
            }

            if (item.Op == "DEACTIVATE")
            {
                if (bin == null)
                {
                    // H6 乱序防护（对契约 §5.1 的修正）：对应 UPSERT 事件可能仍在重试队列，
                    // 直接跳过会让迟到的旧版 UPSERT 复活已停用库位 → 落墓碑行（IsActive=false + Version 占位），
                    // 版本单调判据自动掐死后到的旧版。无仓维度（建不了 join 锚）才退回幂等跳过。
                    if (string.IsNullOrEmpty(item.WarehouseCd))
                    {
                        result.Items.Add(Item(item, "SKIPPED", "bin 不存在且缺 WarehouseCd，幂等无操作"));
                        continue;
                    }
                    var tomb = new WmsBin
                    {
                        Id = item.LocationId,
                        LocationCode = item.LocationCode,
                        WarehouseCd = item.WarehouseCd,
                        PathJson = JsonSerializer.Serialize(item.Path, Json),
                        AttrsJson = JsonSerializer.Serialize(item.Attrs, Json),
                        IsActive = false
                    };
                    Stamp(tomb, item.Version, batch.PublishedBy);
                    _db.WmsBins.Add(tomb);
                    // 波5：新插入行回填字典，保后续同批 item 的 Id/锚命中等价旧 Local-first。
                    binsById[tomb.Id] = tomb;
                    if (!string.IsNullOrEmpty(tomb.WarehouseCd) && tomb.LocationCode != null)
                        binsByAnchor[(tomb.WarehouseCd, tomb.LocationCode)] = tomb;
                    result.Items.Add(Item(item, "DEACTIVATED", "墓碑落库（bin 未曾消费，防乱序复活）"));
                    continue;
                }
                // TOCTOU 权威校验：库存真相在 WMS（§6），按 (WarehouseCd, LocationCode) 锚查（波5：预载合计命中，空=0 等价 SumAsync）
                var qty = (!string.IsNullOrEmpty(bin.WarehouseCd) && bin.LocationCode != null)
                    ? stockByAnchor.GetValueOrDefault((bin.WarehouseCd, bin.LocationCode), 0m)
                    : 0m;
                if (qty > 0)
                {
                    result.Items.Add(Item(item, "REJECTED", "W-SPACE-404 库存非0"));
                    result.Success = false;
                    continue;
                }
                bin.IsActive = false;
                Stamp(bin, item.Version, batch.PublishedBy);
                result.Items.Add(Item(item, "DEACTIVATED", null));
                continue;
            }

            // UPSERT
            if (string.IsNullOrEmpty(item.WarehouseCd))
            {
                // 无仓维度无法建 (WarehouseCd, LocationCode) join 锚（§3.4）→ 拒绝该条
                result.Items.Add(Item(item, "REJECTED", "缺 WarehouseCd（SiteCode↔WarehouseCd 映射未命中）"));
                result.Success = false;
                continue;
            }
            if (bin == null)
            {
                // 终审 #3：join 锚碰撞检查——同 (WarehouseCd, LocationCode) 被不同 LocationId（含墓碑）
                // 占用时，直接 Add 会撞唯一索引走异常毒化链；改为业务拒绝链（REJECTED + Success=false）。
                // 波5：预载锚字典命中（新插入行循环内已回填），覆盖同批两条不同 LocationId 却撞同锚的场景。
                if (binsByAnchor.TryGetValue((item.WarehouseCd!, item.LocationCode), out var anchorHolder)
                    && anchorHolder.Id != item.LocationId)
                {
                    result.Items.Add(Item(item, "REJECTED",
                        "join 锚 (WarehouseCd, LocationCode) 已被其他 LocationId 占用（唯一索引冲突转业务拒绝）"));
                    result.Success = false;
                    continue;
                }
                bin = new WmsBin { Id = item.LocationId };
                _db.WmsBins.Add(bin);
                // 波5：新插入行回填字典，保后续同批 item 的 Id/锚命中等价旧 Local-first。
                binsById[bin.Id] = bin;
            }
            bin.LocationCode = item.LocationCode;   // 理论不变（发布后码冻结）
            bin.WarehouseCd = item.WarehouseCd;
            bin.PathJson = JsonSerializer.Serialize(item.Path, Json);
            bin.AttrsJson = JsonSerializer.Serialize(item.Attrs, Json);
            bin.IsActive = true;
            Stamp(bin, item.Version, batch.PublishedBy);
            // 波5：更新后按当前锚回填（含新建；发布后码冻结，锚通常不变），保后续同批锚查等价。
            binsByAnchor[(bin.WarehouseCd, bin.LocationCode)] = bin;
            result.Items.Add(Item(item, "UPSERTED", null));
        }

        // 终审 #2：持久失败时，失败实体若留在共享 CP6Context tracker 里，会毒化 SpaceBridgeHook
        // 的事件落库与 Worker 批尾簿记（Attempts/NextRetryAt 丢失 → 热循环永不 DeadLetter）。
        // 失败时只 detach 本方法 Add/Modify 的 WmsBin 行后 rethrow——绝不用 ChangeTracker.Clear()
        // （那会连 Worker 正在跟踪的事件行一起断开）。
        try
        {
            await _db.SaveChangesAsync();
        }
        catch
        {
            DetachOwnWrites();
            throw;
        }
        result.AllSkipped = result.Items.Count > 0 && result.Items.All(i => i.Status == "SKIPPED");
        return result;
    }

    /// <summary>
    /// SaveChanges 失败时，将本消费方法写入（Added/Modified）的 WmsBin 行从共享 tracker 断开，
    /// 避免污染同 context 内其他跟踪实体（事件行等）。internal 供单测直接锁定 detach 语义。
    /// </summary>
    internal void DetachOwnWrites()
    {
        foreach (var entry in _db.ChangeTracker.Entries<WmsBin>()
                     .Where(e => e.State is EntityState.Added or EntityState.Modified)
                     .ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    private static void Stamp(WmsBin bin, long version, string? publishedBy)
    {
        bin.Version = version;
        bin.LastPublishedAt = DateTime.Now;
        bin.LastPublishedBy = publishedBy;
    }

    private static WmsItemResult Item(LocationPublishItem i, string status, string? reason) =>
        new() { LocationId = i.LocationId, Status = status, Reason = reason };
}
