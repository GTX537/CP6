namespace CP6.Core.Services.Wf;

/// <summary>注册式连接器契约（D4/D9/P1-5：连接器自己决定 baseURL/认证/HTTP method/headers/timeout/response→OutputVars 映射；
/// 密钥服务端配置，绝不进 SchemaJson）。
/// 实现应把 ctx.JobId 作幂等键发出：Idempotency-Key: wf-service-job-{JobId}。</summary>
public interface IWfConnector
{
    string Name { get; }          // 设计器下拉用（唯一键，按 Name 索引）
    string DisplayName { get; }

    /// <summary>本连接器单次调用的上界耗时（含内部重试）。用于启动期校验其 &lt; 租约时长
    /// （<see cref="WfServiceJobService.LeaseDuration"/>），防长调用被 reaper 误判崩溃而重投→重复外呼。
    /// 默认 null = 未声明（假定安全，如 demo EchoConnector）；真实 HTTP 连接器应据 HttpClient.Timeout 如实声明。</summary>
    TimeSpan? MaxCallDuration => null;

    /// <summary>按路径+参数模板调用。连接器自己决定 baseURL/认证/HTTP method/headers/timeout/response→OutputVars 映射（D4/P1-5）。
    /// 实现应把 ctx.JobId 作幂等键发出：Idempotency-Key: wf-service-job-{JobId}。</summary>
    Task<ServiceTaskResult> CallAsync(string pathTemplate, string? paramsJson, ServiceTaskContext ctx);
}
