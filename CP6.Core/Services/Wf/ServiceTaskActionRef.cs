using System.Text.Json;
using System.Text.Json.Serialization;

namespace CP6.Core.Services.Wf;

/// <summary>
/// 固化动作绑定快照(ActionRefJson §3.5)。
/// 防流程定义漂移:node 入队时快照一次,worker 解析时只看 actionKind,不再查 FlowNode。
/// </summary>
public record ServiceTaskActionRef
{
    [JsonPropertyName("serviceKind")]
    public string ServiceKind { get; init; } = string.Empty;

    /// <summary>
    /// 执行语义:webApi / dataWriteback / none(纯等待 timer)。
    /// worker 只看这个字段决定 executor,不靠猜 Kind。
    /// </summary>
    [JsonPropertyName("actionKind")]
    public string ActionKind { get; init; } = string.Empty;

    /// <summary>dataWriteback executor 键(timer 带回写动作时同用)。</summary>
    [JsonPropertyName("actionName")]
    public string? ActionName { get; init; }

    /// <summary>webApi 注册连接器键(timer 带 webApi 动作时同用)。</summary>
    [JsonPropertyName("connectorName")]
    public string? ConnectorName { get; init; }

    /// <summary>webApi 相对路径模板(可含 {var} 占位)。</summary>
    [JsonPropertyName("path")]
    public string? Path { get; init; }

    /// <summary>参数模板(JSON,键→表达式,§3.6 语法)。</summary>
    [JsonPropertyName("paramsJson")]
    public string? ParamsJson { get; init; }

    /// <summary>timer 专属延时配置;非 timer 节点时为 null。</summary>
    [JsonPropertyName("timer")]
    public TimerConfig? Timer { get; init; }

    // ─────────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions _opts = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// 从 FlowNode 快照 ActionRefJson(序列化 JSON 字符串)。
    /// ActionKind 推导规则:
    ///   • timer + 无 ConnectorName/ActionName → "none"
    ///   • timer + ConnectorName              → "webApi"
    ///   • timer + ActionName                 → "dataWriteback"
    ///   • 非 timer                           → ActionKind = ServiceKind
    /// </summary>
    public static string Snapshot(FlowNode node)
    {
        var sk = node.ServiceKind ?? string.Empty;
        string actionKind;
        TimerConfig? timer = null;

        if (sk == "timer")
        {
            // timer 节点:先确定 actionKind,再填 timer config
            if (!string.IsNullOrEmpty(node.ServiceConnectorName))
                actionKind = "webApi";
            else if (!string.IsNullOrEmpty(node.ServiceActionName))
                actionKind = "dataWriteback";
            else
                actionKind = "none";

            timer = new TimerConfig
            {
                DelayMode  = node.ServiceDelayMode,
                DelayValue = node.ServiceDelayValue,
            };
        }
        else
        {
            // dataWriteback / webApi:actionKind = serviceKind
            actionKind = sk;
        }

        var r = new ServiceTaskActionRef
        {
            ServiceKind   = sk,
            ActionKind    = actionKind,
            ActionName    = node.ServiceActionName,
            ConnectorName = node.ServiceConnectorName,
            Path          = node.ServicePath,
            ParamsJson    = node.ServiceParamsJson,
            Timer         = timer,
        };
        return JsonSerializer.Serialize(r, _opts);
    }

    /// <summary>从 ActionRefJson 字符串反序列化(round-trip lossless)。</summary>
    public static ServiceTaskActionRef Parse(string json)
        => JsonSerializer.Deserialize<ServiceTaskActionRef>(json, _opts)
           ?? throw new InvalidOperationException("ActionRefJson 反序列化失败");

    /// <summary>
    /// 据 actionKind 解析 executor key(§3.2):
    ///   "webApi"        → "webApi"
    ///   "dataWriteback" → r.ActionName
    ///   "none"          → null
    /// </summary>
    public static string? ResolveExecutorKey(ServiceTaskActionRef r)
        => r.ActionKind switch
        {
            "webApi"        => "webApi",
            "dataWriteback" => r.ActionName,
            "none"          => null,
            _               => null,
        };

    // ─────────────────────────────────────────────────────────────

    /// <summary>timer 延时配置嵌套对象(§3.5)。</summary>
    public record TimerConfig
    {
        [JsonPropertyName("delayMode")]
        public string? DelayMode { get; init; }

        [JsonPropertyName("delayValue")]
        public string? DelayValue { get; init; }
    }
}
