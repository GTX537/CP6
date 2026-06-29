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

    /// <summary>存量采纳导入（CodeOrigin=2，不发事件）。返回 (imported, skipped codes)。</summary>
    Task<(int imported, List<string> skipped)> AdoptAsync(
        IEnumerable<(string code, Dictionary<string, object?>? attrs)> items, string? user);
}
