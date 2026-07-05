using System.Collections.Generic;
using System.Globalization;

namespace CP6.Core.Services.Wf.Executors;

/// <summary>
/// 样例 dataWriteback 执行器（§3.1 首个对设计器可见的回写动作，C-T2）。
/// Key = "sampleWriteback"；Kind = "dataWriteback"；VisibleInDesigner = true（进服务目录，P1-6）。
/// <para>
/// 演示动作：读表单 <c>$.amount</c> × 1，写回流程变量 <c>writebackEcho</c>，
/// 并发出幂等键 <c>writebackIdempotencyKey = wf-writeback-job-{JobId}</c>。
/// 纯计算、无 I/O——真实业务回写（PO 确认 / 凭证过账等）由后续按需各加一个 executor，照本模板复制。
/// </para>
///
/// <para><b>黄金模板三铁律（后续 dataWriteback executor 照此模板复制——架构审查 2026-07-05 裁定）</b></para>
/// <para>
/// 背景：sync 路径下 executor 与引擎共享同一 DbContext/事务；executor 半途抛异常时，
/// 已改的追踪实体会被引擎外层 SaveChanges 一并提交（ServiceTaskNodeHandler.cs 入队降级路径不回滚脏改）。
/// 因此：
/// <list type="number">
///   <item><b>先校验全部前置条件，再执行任何写操作</b>——校验失败直接
///     <see cref="ServiceTaskResult.Fail(string)"/>，不留半截脏改。本例先解析 + 校验 amount，
///     全部通过后才构造 OutputVars。</item>
///   <item><b>幂等</b>——用 ctx 语义键（<c>JobId</c>/<c>InstanceId</c>）判重，重复执行结果一致。
///     本例输出是输入的纯函数（amount × 1）且带 <c>wf-writeback-job-{JobId}</c> 幂等键，
///     at-least-once 重投结果字节等价。</item>
///   <item><b>绝不自行 <c>SaveChanges()</c>、不开独立事务、不发外部 HTTP</b>——落库交给引擎的原子接缝
///     （引擎把 OutputVars 经 <see cref="ServiceVarsHelper.MergeOutputVars"/> 合并回 inst.VarsJson 后统一 SaveChanges）；
///     外呼只属于 webApi kind 经 IWfConnector。</item>
/// </list>
/// 如需读 DB，注入 <c>CP6Context</c>（同 scoped，sync 路径原子前提，spec §4.5），但仍不得自行 SaveChanges。
/// </para>
/// </summary>
public sealed class SampleDataWritebackExecutor : IServiceTaskExecutor
{
    public string Key               => "sampleWriteback";
    public string Kind              => ServiceKind.DataWriteback;   // "dataWriteback"
    public bool   VisibleInDesigner => true;                       // dataWriteback 动作进服务目录（P1-6）
    public string DisplayName       => "样例数据回写";

    /// <summary>
    /// 读 <c>$.amount</c> × 1 → 写回 <c>writebackEcho</c>。纯计算，无 I/O。
    /// 律1：先校验 amount 存在且为数值，任一不满足直接 Fail（不构造任何 OutputVars）。
    /// </summary>
    public System.Threading.Tasks.Task<ServiceTaskResult> ExecuteAsync(ServiceTaskContext ctx)
    {
        // ── 律1：先校验全部前置条件，再执行任何写操作 ──────────────────────
        var tplCtx = new ServiceTemplateCtx(
            varsJson:   ctx.VarsJson,
            actorId:    ctx.ActorId.ToString(),
            jobId:      ctx.JobId.ToString(),
            instanceId: ctx.InstanceId.ToString(),
            nowUtcIso:  ctx.NowUtc.ToString("O"));

        var amountRaw = ServiceVarsHelper.ResolveValue("$.amount", tplCtx);
        if (string.IsNullOrWhiteSpace(amountRaw))
            return Done(ServiceTaskResult.Fail("E-WF-019 缺少必填表单字段 amount，无法回写"));

        if (!decimal.TryParse(amountRaw, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
            return Done(ServiceTaskResult.Fail($"E-WF-019 表单字段 amount 非数值:{amountRaw}"));

        // ── 律2：幂等键（at-least-once 重投判重依据）─────────────────────────
        var idempotencyKey = $"wf-writeback-job-{ctx.JobId}";

        // ── 律3：只返回 OutputVars，绝不自行 SaveChanges / 开事务 / 发 HTTP ──
        // 演示回写：amount × 1（换成真实业务计算即为下一个 executor）。
        var writeback = amount * 1m;
        var outputVars = new Dictionary<string, object?>
        {
            ["writebackEcho"]           = writeback,
            ["writebackIdempotencyKey"] = idempotencyKey,
        };

        return Done(ServiceTaskResult.Ok(outputVars));
    }

    private static System.Threading.Tasks.Task<ServiceTaskResult> Done(ServiceTaskResult r)
        => System.Threading.Tasks.Task.FromResult(r);
}
