// CP6.Core/Services/Wf/FlowTriggerValidator.cs（F-T1 全量版；spec §5 E-WF-022/023 保存侧）
using System.Text.Json;
using System.Text.RegularExpressions;
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

/// <summary>触发器保存时校验（spec §5 E-WF-022/023 的保存侧；运行时侧在 FireAsync，双检——
/// 发起人/流程可能在保存后被停用）。失败抛 InvalidOperationException("E-WF-0xx: ...")（对齐引擎错误码风格）。</summary>
public static class FlowTriggerValidator
{
    private static readonly Regex EventKeyPattern = new(@"^[A-Za-z0-9_.-]+\|[A-Za-z0-9_.-]+$", RegexOptions.Compiled);

    public static async Task ValidateAsync(CP6Context db, FlowTriggerSaveReq req, CancellationToken ct)
    {
        // ── 通用 ──
        if (string.IsNullOrWhiteSpace(req.FlowKey))
            throw new InvalidOperationException("E-WF-023: FlowKey 必填");
        if (req.TriggerType is < WfTriggerType.Timer or > WfTriggerType.Message)
            throw new InvalidOperationException("E-WF-022: 触发器类型非法");
        if (req.StarterUserId == Guid.Empty)
            throw new InvalidOperationException("E-WF-022: StarterUserId 必填");

        // ── 分型（spec §2.3）──
        switch (req.TriggerType)
        {
            case WfTriggerType.Timer:
            {
                var cfg = WfTriggerConfig.ParseTimer(req.ConfigJson);
                if (!WfCronHelper.IsValid(cfg.Cron))
                    throw new InvalidOperationException("E-WF-022: cron 解析失败（NCrontab 标准 5 段）");
                // 波③终审 I-1：语法合法但永不触发（如 "0 0 30 2 *"，2/30 不存在）——NextUtc 返回 null，
                // 若放行则 enabled 触发器带 NextDueUtc=null 入库静默死掉（无流水、无报错、扫描永不拾取）。
                if (WfCronHelper.NextUtc(cfg.Cron, DateTime.UtcNow) == null)
                    throw new InvalidOperationException("E-WF-022: cron 表达式永不触发");
                if (!string.IsNullOrWhiteSpace(cfg.VarsJson) && !IsJsonObject(cfg.VarsJson))
                    throw new InvalidOperationException("E-WF-022: varsJson 须为 JSON 对象");
                break;
            }
            case WfTriggerType.Event:
            {
                if (string.IsNullOrWhiteSpace(req.EventKey) || !EventKeyPattern.IsMatch(req.EventKey))
                    throw new InvalidOperationException("E-WF-022: eventKey 格式错（应为 \"{SourceModule}|{HookName}\"）");
                var cfg = WfTriggerConfig.ParseEvent(req.ConfigJson);
                foreach (var (k, v) in cfg.VarsMap ?? new())
                {
                    if (string.IsNullOrWhiteSpace(k))
                        throw new InvalidOperationException("E-WF-022: varsMap 变量名不能为空");
                    if (string.IsNullOrWhiteSpace(v))
                        throw new InvalidOperationException($"E-WF-022: varsMap[{k}] 点路径/模板不能为空");
                }
                break;
            }
            case WfTriggerType.Message:
            {
                var cfg = WfTriggerConfig.ParseMessage(req.ConfigJson);
                if (cfg.VarsSchema != null && cfg.VarsSchema.Any(string.IsNullOrWhiteSpace))
                    throw new InvalidOperationException("E-WF-022: varsSchema 含空字段名");
                break;
            }
        }

        // ── 引用存在性（保存侧）──
        var starterOk = await db.Sys_Users.AnyAsync(u => u.Id == req.StarterUserId && u.Enable, ct);
        if (!starterOk) throw new InvalidOperationException("E-WF-022: StarterUserId 不存在或已停用");
        var flowOk = await db.Wf_FlowDefs.AnyAsync(d => d.FlowKey == req.FlowKey && d.Enable, ct);
        if (!flowOk) throw new InvalidOperationException("E-WF-023: 目标流程不存在或未启用");
    }

    private static bool IsJsonObject(string s)
    {
        try { using var d = JsonDocument.Parse(s); return d.RootElement.ValueKind == JsonValueKind.Object; }
        catch (JsonException) { return false; }
    }
}
