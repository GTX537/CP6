using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace CP6.Core.Services.Wf.Executors;

/// <summary>
/// 样例演示连接器（QA / demo 用），不发出真实 HTTP 请求。
/// 把解析后的 path 和幂等键 echo 回 OutputVars，便于 gstack QA 断言流程变量写入。
/// <para>
/// 真实连接器实现示例：
///   1. 注入 <c>IHttpClientFactory</c>，取预置命名客户端（含 baseURL/认证/timeout，密钥来自 appsettings/secret，绝不进 SchemaJson，D9）。
///   2. 把 <c>pathTemplate</c> + <c>paramsJson</c> 中的 <c>{var}</c> / <c>$.var</c> 表达式用 <see cref="ServiceVarsHelper"/> 求值。
///   3. 发出 HTTP 请求并在 request header 追加 <c>Idempotency-Key: wf-service-job-{ctx.JobId}</c>（P1-2）。
///   4. 把 response body 按连接器配置的 response-map 转成 OutputVars（P1-5）。
/// </para>
/// </summary>
public sealed class EchoConnector : IWfConnector
{
    public string Name        => "erpEcho";
    public string DisplayName => "ERP Echo (demo)";

    /// <summary>
    /// 解析 pathTemplate 中的 {varName} 占位符 → 查 VarsJson（等价 $.varName），
    /// 返回 Ok(echoedPath, idempotencyKey)。不发出任何 HTTP 请求。
    /// </summary>
    public System.Threading.Tasks.Task<ServiceTaskResult> CallAsync(
        string pathTemplate, string? paramsJson, ServiceTaskContext ctx)
    {
        // 构造模板求值上下文
        var tplCtx = new ServiceTemplateCtx(
            varsJson:    ctx.VarsJson,
            actorId:     ctx.ActorId.ToString(),
            jobId:       ctx.JobId.ToString(),
            instanceId:  ctx.InstanceId.ToString(),
            nowUtcIso:   ctx.NowUtc.ToString("O"));

        // 把 path 中每个 {varName} 替换为 $.varName 的求值结果
        var resolvedPath = Regex.Replace(pathTemplate, @"\{(\w+)\}", m =>
            ServiceVarsHelper.ResolveValue("{" + m.Groups[1].Value + "}", tplCtx));

        var idempotencyKey = $"wf-service-job-{ctx.JobId}";

        var outputVars = new Dictionary<string, object?>
        {
            ["echoedPath"]      = resolvedPath,
            ["idempotencyKey"]  = idempotencyKey,
        };

        return System.Threading.Tasks.Task.FromResult(ServiceTaskResult.Ok(outputVars));
    }
}
