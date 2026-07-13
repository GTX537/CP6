// CP6.Core/Services/Wf/WfTriggerVarsMapper.cs
using System.Text.Json;

namespace CP6.Core.Services.Wf;

/// <summary>event varsMap 映射（复用 ServiceVarsHelper 点路径口径，含其已记档限制：值统一为字符串）
/// + message varsSchema 白名单过滤（spec §2.3）。两者共同哲学：不透传原负载，防变量注入。</summary>
public static class WfTriggerVarsMapper
{
    public static string MapVars(Dictionary<string, string>? varsMap, string payloadJson)
    {
        if (varsMap == null || varsMap.Count == 0) return "{}";
        var ctx = new ServiceTemplateCtx(payloadJson, actorId: "", jobId: "", instanceId: "",
                                         nowUtcIso: DateTime.UtcNow.ToString("O"));
        var vars = new Dictionary<string, string>(varsMap.Count);
        foreach (var (key, template) in varsMap)
            vars[key] = ServiceVarsHelper.ResolveValue(template, ctx);
        return JsonSerializer.Serialize(vars);
    }

    /// <summary>白名单过滤：不在名单的负载键丢弃。body 非 JSON 对象抛 JsonException（端点回 400）。</summary>
    public static string FilterBySchema(string bodyJson, IReadOnlyList<string>? schema)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(bodyJson) ? "{}" : bodyJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("body must be a JSON object");
        var allow = new HashSet<string>(schema ?? Array.Empty<string>(), StringComparer.Ordinal);
        var kept = new Dictionary<string, JsonElement>();
        foreach (var p in doc.RootElement.EnumerateObject())
            if (allow.Contains(p.Name)) kept[p.Name] = p.Value.Clone();
        return JsonSerializer.Serialize(kept);
    }
}
