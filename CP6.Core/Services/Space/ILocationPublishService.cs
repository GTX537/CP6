namespace CP6.Core.Services.Space;

/// <summary>
/// 库位发布、停用、采纳服务契约（ch04）。
/// </summary>
public interface ILocationPublishService
{
    /// <summary>整层/库区发布：过闸门 → 草稿 Status0→1 → 发 UPSERT 事件。返回发布条数。</summary>
    Task<int> PublishFloorAsync(Guid floorId, Guid? zoneId, string? user);

    /// <summary>停用已发布库位（D6 双重校验：Space 前置查库存 + WMS hook）。</summary>
    Task DeactivateAsync(Guid locationId, string? user);

    /// <summary>
    /// 路径元数据 re-publish（ch04 §7.2 路径B）：层级归属（Zone/Aisle/Rack 挂载）变更后，
    /// 对已发布(Status=1)库位 Version+1 并发 UPSERT 事件刷新 WMS 侧 path——码冻结不变、状态不变。
    /// 非发布态/无码的 id 自动忽略。返回实际 re-publish 条数。
    /// </summary>
    Task<int> RepublishAsync(IReadOnlyCollection<Guid> locationIds, string? user);

    /// <summary>存量采纳导入（CodeOrigin=2，不发事件）。返回 (imported, skipped codes)。</summary>
    Task<(int imported, List<string> skipped)> AdoptAsync(
        IEnumerable<(string code, Dictionary<string, object?>? attrs)> items, string? user);
}
