using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

/// <summary>服务任务节点（第 6 个 INodeHandler，spec §3.4）。让 token 到达节点时跑自动化：
/// <list type="bullet">
///   <item><b>sync</b>（dataWriteback 默认）：内联乐观一击——成功合并 OutputVars 后 <see cref="FlowEngine.AdvanceToken"/>（与流程态同事务原子）；
///         失败则降级异步重试，入队 job（AttemptCount=1，P0-1）+ token 停泊不 advance。</item>
///   <item><b>async</b>（webApi 默认）：停泊 token + 入队 job（AttemptCount=0），由 worker 到期 lease 抢占执行→恢复。</item>
///   <item><b>timer</b>（恒 async）：DueAtUtc = <see cref="ComputeDueUtc"/>（duration/untilDate/untilExpr），入队停泊；纯等待或到点执行动作。</item>
/// </list>
/// 入队走 <c>EnqueueServiceJob</c> 防重（P0-3）：同 (TenantId,TokenId,NodeId) 活跃 job（Pending/Running）至多一条——
/// 检查同时覆盖 EF 变更追踪器（Local，权威态、含本回合未落盘）与 DB（排除已 Local 的 Id），仿
/// <see cref="FlowEngine"/> 的 HasActiveToken / CancelAllActiveTokens 写法（仓库已知坑：只查 DB 会漏未落盘 Add）。
/// handler 不 SaveChanges（沿用引擎在 Submit/ActOnce 边界统一保存）；TenantId 由 StampTenant 自动盖（同 SpawnToken）。</summary>
internal sealed class ServiceTaskNodeHandler : INodeHandler
{
    private const int DefaultMaxRetries = 3;     // spec §2.1：node.ServiceMaxRetries 默认 3
    private const int DefaultBackoffSec = 30;    // spec §2.1：退避基数默认 30s

    private readonly IReadOnlyDictionary<string, IServiceTaskExecutor> _executors;
    private readonly IWorkdayCalculator? _workdays;    // I-A workdays 第四延时模式（缺则降级立即）
    private readonly int _workdayFireHour;             // WfsInfraOptions.WorkdayFireHour，落点小时（默认 9）
    private readonly ITenantClock? _clock;             // I-E 租户时区源（缺则回落服务器本地=现状字节等价）

    // ctor 追加 workdays/opts/clock（均带默认值 → DI 自动注入；FlowEngine.DefaultHandlers fallback 与既有单测 new(...) 零破坏）
    public ServiceTaskNodeHandler(IEnumerable<IServiceTaskExecutor>? executors,
        IWorkdayCalculator? workdays = null, WfsInfraOptions? opts = null, ITenantClock? clock = null)
    {
        _executors = (executors ?? Array.Empty<IServiceTaskExecutor>())
            .ToDictionary(e => e.Key, StringComparer.OrdinalIgnoreCase);
        _workdays = workdays;
        _workdayFireHour = opts?.WorkdayFireHour ?? 9;
        _clock = clock;
    }

    public string Type => "serviceTask";

    public async Task OnEnterAsync(NodeContext ctx)
    {
        var node = ctx.Node; var inst = ctx.Inst; var token = ctx.Token;
        var kind = node.ServiceKind ?? string.Empty;

        // mode：timer 恒 async；否则取节点显式 mode，缺省按 kind（webApi→async / 其余→sync），spec §3.4
        var mode = (kind == ServiceKind.Timer)
            ? ServiceMode.Async
            : (node.ServiceMode ?? (kind == ServiceKind.WebApi ? ServiceMode.Async : ServiceMode.Sync));

        int maxAttempts = (node.ServiceMaxRetries ?? DefaultMaxRetries) + 1;   // P0-1：消 off-by-one
        int backoffSec = node.ServiceRetryBackoffSec ?? DefaultBackoffSec;

        var actionRefJson = ServiceTaskActionRef.Snapshot(node);              // 固化动作绑定快照，防漂移（§3.5）
        var actionRef = ServiceTaskActionRef.Parse(actionRefJson);
        var key = ServiceTaskActionRef.ResolveExecutorKey(actionRef);
        var nowUtc = DateTime.UtcNow;

        if (mode == ServiceMode.Sync)
        {
            // ── sync：内联乐观一击（快/原子），失败降级异步重试 ──
            ServiceTaskResult result;
            if (key is null || !_executors.TryGetValue(key, out var exec))
            {
                result = ServiceTaskResult.Fail($"E-WF-018 动作/连接器未注册:{key ?? "(none)"}");
            }
            else
            {
                var sctx = new ServiceTaskContext
                {
                    InstanceId = inst.Id,
                    TokenId = token.Id,
                    NodeId = node.Id,
                    StarterId = inst.StarterId,
                    JobId = Guid.Empty,            // sync 内联无 job（幂等键空）
                    AttemptNo = 1,
                    ActorId = inst.StarterId,      // sync = 发起人上下文
                    NowUtc = nowUtc,
                    VarsJson = inst.VarsJson,
                    ActionRefJson = actionRefJson,
                };
                try { result = await exec.ExecuteAsync(sctx); }
                catch (Exception ex) { result = ServiceTaskResult.Fail(ex.Message); }   // 不让 executor 异常炸引擎
            }

            if (result.Success)
            {
                if (result.OutputVars is { Count: > 0 })
                {
                    var merged = ServiceVarsHelper.MergeOutputVars(inst.VarsJson, result.OutputVars);   // §3.6
                    inst.VarsJson = merged.VarsJson;
                    ctx.Engine.AddHistory(inst.Id, node.Id, inst.StarterId, "serviceVars",
                        $"merged: [{string.Join(",", merged.MergedKeys)}]; skipped: [{string.Join(",", merged.SkippedKeys)}]");
                }
                await ctx.Engine.AdvanceToken(inst, ctx.Schema, token);   // 沿成功边推进（跳 IsError），原子
            }
            else
            {
                // sync 那一击算 attempt 1（P0-1）；token 停泊，worker 退避后重试
                EnqueueServiceJob(ctx, node, token, kind, actionRefJson,
                    dueAtUtc: nowUtc, attemptCount: 1, maxAttempts: maxAttempts,
                    nextAttemptAtUtc: nowUtc.AddSeconds(backoffSec));
            }
            return;
        }

        // ── async / timer：停泊 token + 入队（不 advance，像 ApprovalNodeHandler 等 ActAsync）──
        var dueAtUtc = (kind == ServiceKind.Timer)
            ? await ComputeTimerDueUtcAsync(node, inst.VarsJson, ct: default)
            : nowUtc;
        EnqueueServiceJob(ctx, node, token, kind, actionRefJson,
            dueAtUtc: dueAtUtc, attemptCount: 0, maxAttempts: maxAttempts,
            nextAttemptAtUtc: dueAtUtc);
    }

    /// <summary>入队停泊 job，<b>先防重</b>（P0-3）：同 token 同 node 已有活跃 job（Pending/Running）则不重复创建。
    /// 防重检查覆盖 EF 变更追踪器（Local，含本回合 Add 未落盘）+ DB（排除已 Local 的 Id），与 filtered unique index 双保险。
    /// TenantId 不显式设——由 <c>StampTenant</c> 在 SaveChanges 自动盖（同 <see cref="FlowEngine.SpawnToken"/>）。</summary>
    private static void EnqueueServiceJob(NodeContext ctx, FlowNode node, Wf_FlowToken token,
        string kind, string actionRefJson, DateTime dueAtUtc, int attemptCount, int maxAttempts,
        DateTime nextAttemptAtUtc)
    {
        var db = ctx.Engine.Db;

        // ① Local 权威态（变更追踪器对已加载/新增 job 是最新态，含未落盘 Add）
        if (db.Wf_ServiceJobs.Local.Any(j => j.TokenId == token.Id && j.NodeId == node.Id
                && (j.Status == ServiceJobStatus.Pending || j.Status == ServiceJobStatus.Running)))
            return;
        // ② DB 补查未被本地追踪的行（排除已在 Local 的 Id，避免读到落盘旧值）
        var localIds = db.Wf_ServiceJobs.Local
            .Where(j => j.TokenId == token.Id && j.NodeId == node.Id).Select(j => j.Id).ToHashSet();
        if (db.Wf_ServiceJobs.Any(j => j.TokenId == token.Id && j.NodeId == node.Id
                && (j.Status == ServiceJobStatus.Pending || j.Status == ServiceJobStatus.Running)
                && !localIds.Contains(j.Id)))
            return;

        db.Wf_ServiceJobs.Add(new Wf_ServiceJob
        {
            Id = Guid.NewGuid(),
            InstanceId = ctx.Inst.Id,
            TokenId = token.Id,
            NodeId = node.Id,
            Kind = kind,
            ActionRefJson = actionRefJson,
            DueAtUtc = dueAtUtc,
            Status = ServiceJobStatus.Pending,
            AttemptCount = attemptCount,
            MaxAttempts = maxAttempts,
            NextAttemptAtUtc = nextAttemptAtUtc,
            CreateDate = DateTime.UtcNow,
        });   // TenantId 由 StampTenant 自动盖
    }

    /// <summary>timer 到期时刻计算（spec §4.7，P0-6），<b>一律返回 UTC</b> 存 <c>DueAtUtc</c>：
    /// <list type="bullet">
    ///   <item><c>duration</c>：now + <see cref="ParseDuration"/>（"3d"/"2h" 简写 或 ISO-8601 "PT2H"/"P3D"）。</item>
    ///   <item><c>untilDate</c>：把用户输入（如 "2026-07-01"）按 <b>app 默认时区</b> 解释 → 转 UTC。
    ///         首切无 per-tenant 时区基础设施 → app 默认 = 服务器本地时区（<see cref="TimeZoneInfo.Local"/>）；
    ///         字段名带 <c>Utc</c>，未来接入 per-tenant tz 设置时零 schema/字段返工。</item>
    ///   <item><c>untilExpr</c>：对 varsJson 求值（<see cref="ServiceVarsHelper.ResolveValue"/>，支持 $.var）出日期串 → 同 untilDate 转 UTC（最小实现）。</item>
    /// </list>
    /// 输入非法/缺失一律降级为「立即」（now），不让坏配置炸引擎。</summary>
    internal static DateTime ComputeDueUtc(FlowNode node, string? varsJson)
        => ComputeDueUtc(node, varsJson, TimeZoneInfo.Local);   // 无 tz 上下文（既有静态调用点）→ 服务器本地（字节等价）

    /// <summary>三延时模式（duration/untilDate/untilExpr）算 UTC 到期。<paramref name="tz"/> 为「本地时刻」的解释时区
    /// （I-E：untilDate/untilExpr 从服务器本地换租户时区；duration 与时区无关）。</summary>
    internal static DateTime ComputeDueUtc(FlowNode node, string? varsJson, TimeZoneInfo tz)
    {
        var nowUtc = DateTime.UtcNow;
        var value = node.ServiceDelayValue;
        if (string.IsNullOrWhiteSpace(value)) return nowUtc;

        switch (node.ServiceDelayMode)
        {
            case "duration":
                return nowUtc + ParseDuration(value);

            case "untilDate":
                return ParseLocalDateToUtc(value, nowUtc, tz);

            case "untilExpr":
                // 最小实现：表达式求值出日期串（$.var / 字面量）→ 同 untilDate 转 UTC
                var resolved = ServiceVarsHelper.ResolveValue(value,
                    new ServiceTemplateCtx(varsJson, "", "", "", nowUtc.ToString("O")));
                return string.IsNullOrWhiteSpace(resolved) ? nowUtc : ParseLocalDateToUtc(resolved, nowUtc, tz);

            default:
                return nowUtc;
        }
    }

    /// <summary>timer 到期计算（含 <c>workdays</c> 第四模式，spec §2.3）。<c>workdays</c> 走 <see cref="IWorkdayCalculator"/>
    /// 顺延 N 工作日后落到当日 <see cref="_workdayFireHour"/> 时整点；其余三模式委托 <see cref="ComputeDueUtc(FlowNode,string,TimeZoneInfo)"/>。
    /// I-E：tz 源＝<c>ITenantClock.GetTenantTimeZone()</c>（缺 clock → 服务器本地＝现状字节等价）；<c>nowLocal</c> 按同一 tz
    /// 从 UTC 换算（跨零点当日边界随租户时区）。缺服务/值非正整数 → 降级立即（坏配置不炸引擎，值校验并入 E-WF-016 家族）。</summary>
    private Task<DateTime> ComputeTimerDueUtcAsync(FlowNode node, string? varsJson, CancellationToken ct)
    {
        var tz = _clock?.GetTenantTimeZone() ?? TimeZoneInfo.Local;
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        return ComputeTimerDueUtcCoreAsync(node, varsJson, nowLocal, tz, ct);
    }

    /// <summary>测试专用重载（注入 <paramref name="nowLocal"/>，A-T3/E-T2 复用）。tz 仍取 clock（缺→本地），
    /// 令注入的 <paramref name="nowLocal"/> 与回转 tz 一致，DST 口径可定点验证。</summary>
    internal Task<DateTime> ComputeTimerDueUtcForTestAsync(FlowNode node, string? varsJson, DateTime nowLocal, CancellationToken ct)
    {
        var tz = _clock?.GetTenantTimeZone() ?? TimeZoneInfo.Local;
        return ComputeTimerDueUtcCoreAsync(node, varsJson, nowLocal, tz, ct);
    }

    private async Task<DateTime> ComputeTimerDueUtcCoreAsync(FlowNode node, string? varsJson, DateTime nowLocal, TimeZoneInfo tz, CancellationToken ct)
    {
        if (node.ServiceDelayMode == "workdays")
        {
            if (_workdays == null || !int.TryParse(node.ServiceDelayValue, out var n) || n <= 0)
                return DateTime.UtcNow;   // 降级立即（值非正并入 E-WF-016 家族，运行期不炸引擎）
            var dueDay = await _workdays.AddWorkdaysAsync(nowLocal.Date, n, ct);
            var fireLocal = DateTime.SpecifyKind(dueDay.Date.AddHours(_workdayFireHour), DateTimeKind.Unspecified);
            return ConvertLocalToUtcWithDstPolicy(fireLocal, tz);
        }
        return ComputeDueUtc(node, varsJson, tz);   // duration/untilDate/untilExpr
    }

    /// <summary>把「指定时区 <paramref name="tz"/> 下的本地时刻」字符串转 UTC。解析失败 → <paramref name="fallback"/>。</summary>
    private static DateTime ParseLocalDateToUtc(string value, DateTime fallback, TimeZoneInfo tz)
    {
        if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return fallback;
        return ConvertLocalToUtcWithDstPolicy(DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified), tz);
    }

    /// <summary>本地时刻 → UTC，含 <b>DST 口径（写死，spec §2.3）</b>：
    /// <list type="bullet">
    ///   <item><b>春跳缺口</b>（本地时刻不存在，<see cref="TimeZoneInfo.IsInvalidTime"/>）→ 逐时前移取下一有效本地瞬间
    ///         （即 +DST 偏移，通常 1h；上限 6 步防呆）。</item>
    ///   <item><b>秋拨歧义</b>（本地时刻重复出现）→ <see cref="TimeZoneInfo.ConvertTimeToUtc(DateTime,TimeZoneInfo)"/>
    ///         默认按<b>标准时</b>解释（取标准时那次），不特殊处理。</item>
    /// </list>
    /// 极端边界仍抛 → 按 UTC 兜底（不炸引擎，同既有 ParseLocalDateToUtc 姿态）。日本无 DST，此策略对其为恒等。</summary>
    private static DateTime ConvertLocalToUtcWithDstPolicy(DateTime unspecified, TimeZoneInfo tz)
    {
        var t = DateTime.SpecifyKind(unspecified, DateTimeKind.Unspecified);
        for (var i = 0; i < 6 && tz.IsInvalidTime(t); i++)
            t = t.AddHours(1);   // 缺口内：前移到下一有效本地瞬间
        try { return TimeZoneInfo.ConvertTimeToUtc(t, tz); }
        catch { return DateTime.SpecifyKind(t, DateTimeKind.Utc); }   // tz 边界异常兜底
    }

    /// <summary>解析延时时长：ISO-8601（"PT2H"/"P3D"，经 <see cref="XmlConvert.ToTimeSpan"/>）
    /// 或简写 "&lt;n&gt;d|h|m|s"（如 "3d"/"2h"/"30m"/"45s"）。无法解析 → <see cref="TimeSpan.Zero"/>（立即，不崩溃）。</summary>
    internal static TimeSpan ParseDuration(string value)
    {
        value = value.Trim();
        // ISO-8601 duration（P 打头）
        if (value.StartsWith("P", StringComparison.OrdinalIgnoreCase))
        {
            try { return XmlConvert.ToTimeSpan(value); } catch { /* 含年月或非法 → 落简写分支/零 */ }
        }
        // 简写 <number><unit>
        var m = Regex.Match(value, @"^(\d+)\s*([dhms])$", RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[1].Value, out var n))
        {
            return m.Groups[2].Value.ToLowerInvariant() switch
            {
                "d" => TimeSpan.FromDays(n),
                "h" => TimeSpan.FromHours(n),
                "m" => TimeSpan.FromMinutes(n),
                "s" => TimeSpan.FromSeconds(n),
                _   => TimeSpan.Zero,
            };
        }
        return TimeSpan.Zero;
    }
}
