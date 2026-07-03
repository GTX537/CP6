namespace CP6.Core.Services.Wf;

/// <summary>注册式连接器契约（D4/D9/P1-5：连接器自己决定 baseURL/认证/HTTP method/headers/timeout/response→OutputVars 映射；
/// 密钥服务端配置，绝不进 SchemaJson）。
/// 实现应把 ctx.JobId 作幂等键发出：Idempotency-Key: wf-service-job-{JobId}。</summary>
public interface IWfConnector
{
    string Name { get; }          // 设计器下拉用（唯一键，按 Name 索引）
    string DisplayName { get; }

    /// <summary>按路径+参数模板调用。连接器自己决定 baseURL/认证/HTTP method/headers/timeout/response→OutputVars 映射（D4/P1-5）。
    /// 实现应把 ctx.JobId 作幂等键发出：Idempotency-Key: wf-service-job-{JobId}。</summary>
    Task<ServiceTaskResult> CallAsync(string pathTemplate, string? paramsJson, ServiceTaskContext ctx);
}
