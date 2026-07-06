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
        foreach (var item in batch.Items)
        {
            // 终审 #3：先看同批未保存的 Added 实体（Local），再落库查——顺手兜掉批内同 LocationId 双 Add。
            // 注意 FindAsync 会看见 Local 但绕过全局查询过滤器（不做租户核对），此处用 Local+过滤查询双查更稳；
            // 本表 PK=LocationId 为全局唯一 GUID，跨租户误命中现实无虞。
            var bin = _db.WmsBins.Local.FirstOrDefault(b => b.Id == item.LocationId)
                      ?? await _db.WmsBins.FirstOrDefaultAsync(b => b.Id == item.LocationId);

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
                    result.Items.Add(Item(item, "DEACTIVATED", "墓碑落库（bin 未曾消费，防乱序复活）"));
                    continue;
                }
                // TOCTOU 权威校验：库存真相在 WMS（§6），按 (WarehouseCd, LocationCode) 锚查
                var qty = await _db.Stocks
                    .Where(s => s.WarehouseCd == bin.WarehouseCd && s.LocationCd == bin.LocationCode)
                    .SumAsync(s => s.PhysicalQty);
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
                // 含 Local 同批检查，覆盖同批两条不同 LocationId 却撞同锚的场景。
                var anchorHolder = _db.WmsBins.Local
                        .FirstOrDefault(b => b.WarehouseCd == item.WarehouseCd && b.LocationCode == item.LocationCode)
                    ?? await _db.WmsBins.FirstOrDefaultAsync(
                        b => b.WarehouseCd == item.WarehouseCd && b.LocationCode == item.LocationCode);
                if (anchorHolder != null && anchorHolder.Id != item.LocationId)
                {
                    result.Items.Add(Item(item, "REJECTED",
                        "join 锚 (WarehouseCd, LocationCode) 已被其他 LocationId 占用（唯一索引冲突转业务拒绝）"));
                    result.Success = false;
                    continue;
                }
                bin = new WmsBin { Id = item.LocationId };
                _db.WmsBins.Add(bin);
            }
            bin.LocationCode = item.LocationCode;   // 理论不变（发布后码冻结）
            bin.WarehouseCd = item.WarehouseCd;
            bin.PathJson = JsonSerializer.Serialize(item.Path, Json);
            bin.AttrsJson = JsonSerializer.Serialize(item.Attrs, Json);
            bin.IsActive = true;
            Stamp(bin, item.Version, batch.PublishedBy);
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
