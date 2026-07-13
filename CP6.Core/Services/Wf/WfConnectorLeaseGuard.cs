using System;
using System.Collections.Generic;
using System.Linq;

namespace CP6.Core.Services.Wf;

/// <summary>启动期护栏（票3）：任何连接器声明的单次调用上界 >= 租约时长即抛错快速失败，
/// 逼真实连接器把 HTTP 超时配在租约内，杜绝「长调用未完 → reaper 复位重投 → 重复外呼」。</summary>
public static class WfConnectorLeaseGuard
{
    public static void Validate(IEnumerable<IWfConnector> connectors)
    {
        var lease = WfServiceJobService.LeaseDuration;
        var offenders = (connectors ?? Enumerable.Empty<IWfConnector>())
            .Where(c => c.MaxCallDuration is TimeSpan d && d >= lease)
            .Select(c => $"{c.Name}(MaxCallDuration={c.MaxCallDuration})")
            .ToList();
        if (offenders.Count > 0)
            throw new InvalidOperationException(
                $"WfConnector 租约校验失败：以下连接器 MaxCallDuration >= 租约 {lease}，" +
                $"reaper 会误判崩溃并重投导致重复外呼——请把 HTTP 超时收进租约内：{string.Join(", ", offenders)}");
    }
}
