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

    // 按 Name（OrdinalIgnoreCase）索引已注册连接器
    private readonly Dictionary<string, IWfConnector> _connectors;

    public WebApiExecutor(IEnumerable<IWfConnector> connectors)
    {
        _connectors = new Dictionary<string, IWfConnector>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var c in connectors)
            _connectors[c.Name] = c;
    }

    /// <summary>
    /// 解析 ctx.ActionRefJson → 找连接器 → 委托 CallAsync。
    /// 本执行器不做 HTTP，不持有 HttpClient——HTTP 由各连接器实现决定（D4/P1-5）。
    /// </summary>
    public async System.Threading.Tasks.Task<ServiceTaskResult> ExecuteAsync(ServiceTaskContext ctx)
    {
        if (string.IsNullOrEmpty(ctx.ActionRefJson))
            return ServiceTaskResult.Fail("E-WF-018 ActionRefJson 为空，无法解析连接器");

        ServiceTaskActionRef r;
        try { r = ServiceTaskActionRef.Parse(ctx.ActionRefJson); }
        catch (System.Exception ex)
        { return ServiceTaskResult.Fail($"E-WF-018 ActionRefJson 解析失败: {ex.Message}"); }

        var connectorName = r.ConnectorName;
        if (string.IsNullOrEmpty(connectorName) || !_connectors.TryGetValue(connectorName, out var connector))
            return ServiceTaskResult.Fail($"E-WF-018 连接器未注册:{connectorName}");

        // 把 path/paramsJson 原样传给连接器；连接器自己决定模板求值/method/headers/response-map（P1-5）
        return await connector.CallAsync(r.Path ?? "", r.ParamsJson, ctx);
    }
}
