using System.Collections.Generic;

namespace CP6.Core.Services.Wf.Executors;

/// <summary>
/// 通用 WebAPI 执行器（§3.2/§3.3）。
/// Key/Kind = "webApi"；不混入 dataWriteback 服务目录（VisibleInDesigner=false，P1-6）。
/// 本执行器自身不做 HTTP——委托给注册式 <see cref="IWfConnector"/>（D4 安全边界：
/// baseURL/认证/密钥/method/response-map 由连接器持有，不进 SchemaJson）。
/// </summary>
public sealed class WebApiExecutor : IServiceTaskExecutor
{
    public string Key              => "webApi";
    public string Kind             => "webApi";
    public bool   VisibleInDesigner => false;
    public string DisplayName      => "WebAPI";

    // D5 解析合并：先租户表（Wf_Connector Enabled 行 → DbWfConnector）→ 未命中回落 app 字典。
    private readonly TenantConnectorResolver _resolver;

    /// <summary>生产构造（DI）：注入租户感知解析器（内部持 CP6Context + app 字典 + DataProtection + HttpClientFactory）。</summary>
    public WebApiExecutor(TenantConnectorResolver resolver)
    {
        _resolver = resolver;
    }

    /// <summary>兼容构造（app-only）：仅 app 级连接器、无租户表，供既有单测零改写复用。
    /// 内部包成 db=null 的 app-only 解析器（解析永不触碰租户表）。</summary>
    public WebApiExecutor(IEnumerable<IWfConnector> connectors)
        : this(new TenantConnectorResolver(null, connectors)) { }

    /// <summary>
    /// 解析 ctx.ActionRefJson → 经解析器找连接器（租户优先 app 兜底）→ 委托 CallAsync。
    /// 本执行器不做 HTTP，不持有 HttpClient——HTTP 由各连接器实现决定（D4/P1-5）。
    /// </summary>
    public async System.Threading.Tasks.Task<ServiceTaskResult> ExecuteAsync(ServiceTaskContext ctx)
    {
        // E-WF-018 结构化格式：`E-WF-018|<机读明细>`。管道前=可翻译码（前端按码 i18n），
        // 管道后=无本地化散文的机读 token（连接器名 / 空标记 / 异常类型名），供诊断解析。
        if (string.IsNullOrEmpty(ctx.ActionRefJson))
            return ServiceTaskResult.Fail("E-WF-018|actionRefEmpty");

        ServiceTaskActionRef r;
        try { r = ServiceTaskActionRef.Parse(ctx.ActionRefJson); }
        catch (System.Exception ex)
        { return ServiceTaskResult.Fail($"E-WF-018|parseError:{ex.GetType().Name}"); }

        var connectorName = r.ConnectorName;
        if (string.IsNullOrEmpty(connectorName))
            return ServiceTaskResult.Fail($"E-WF-018|{connectorName}");

        var connector = await _resolver.ResolveAsync(connectorName);
        if (connector == null)
            return ServiceTaskResult.Fail($"E-WF-018|{connectorName}");

        // 把 path/paramsJson 原样传给连接器；连接器自己决定模板求值/method/headers/response-map（P1-5）
        return await connector.CallAsync(r.Path ?? "", r.ParamsJson, ctx);
    }
}
