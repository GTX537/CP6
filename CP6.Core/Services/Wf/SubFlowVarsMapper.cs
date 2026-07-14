using System.Text.Json;
using System.Text.Json.Nodes;

namespace CP6.Core.Services.Wf;

/// <summary>子流程双向变量映射（spec §2.1/§3.2，纯函数零 I/O）。点路径与 <see cref="ServiceVarsHelper.ResolveValue"/>
/// 同口径（"$.a.b" 顶层/嵌套对象键，不支持数组下标——含下标由校验层 E-WF-025 拦），但**保 JSON 类型**
/// （ResolveValue 返回字符串，回注数组/数字会失真，故独立实现 ResolveNode）。</summary>
internal static class SubFlowVarsMapper
{
    private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };

    /// <summary>解析映射 JSON（{"目标var":"$.源路径"}）。null/空白 → true+空表；非对象/值非字符串/非法 JSON → false。</summary>
    public static bool TryParseMap(string? mapJson, out Dictionary<string, string> map)
    {
        map = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(mapJson)) return true;
        try
        {
            if (JsonNode.Parse(mapJson) is not JsonObject o) return false;
            foreach (var (k, v) in o)
            {
                if (v is not JsonValue jv || !jv.TryGetValue<string>(out var path)) return false;
                map[k] = path;
            }
            return true;
        }
        catch (JsonException) { return false; }
    }

    /// <summary>"$.a.b" 点路径取值（保类型）。缺失/非法 → null。无 "$." 前缀视为顶层键。</summary>
    public static JsonNode? ResolveNode(string path, string? varsJson)
    {
        if (string.IsNullOrWhiteSpace(varsJson) || string.IsNullOrWhiteSpace(path)) return null;
        var p = path.StartsWith("$.", StringComparison.Ordinal) ? path[2..] : path;
        try
        {
            JsonNode? cur = JsonNode.Parse(varsJson);
            foreach (var part in p.Split('.'))
            {
                if (cur is not JsonObject o) return null;
                cur = o[part];
                if (cur is null) return null;
            }
            return cur;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>构造子实例 varsJson（spec §3.1 第 3 步）：SubVarsInJson 映射自父 vars ∪ {"item","itemIndex"}
    /// （单实例 item==null 时不注入两键）。映射源缺失 → 该键写 null（子流程可空感知）。</summary>
    public static string BuildChildVars(string? subVarsInJson, string parentVarsJson, JsonNode? item, int? itemIndex)
    {
        var child = new JsonObject();
        if (TryParseMap(subVarsInJson, out var map))
            foreach (var (childVar, path) in map)
                child[childVar] = ResolveNode(path, parentVarsJson)?.DeepClone();
        if (item is not null)
        {
            child["item"] = item.DeepClone();
            child["itemIndex"] = itemIndex;
        }
        return child.ToJsonString();
    }

    /// <summary>子终态→父回注（spec §3.2 恢复路径）：{"父var":"$.子var路径"}。
    /// aggregateAsArray=true（多实例 all）→ 按 SubIndex 升序聚合数组（缺失=null）；false（单实例/any 首个）→ 标量。
    /// 返回 dict 供 <see cref="ServiceVarsHelper.MergeOutputVars"/> 合并（保留前缀 wf./sys./_internal. 同款拦截）。</summary>
    public static Dictionary<string, object?> BuildOutMerge(string? subVarsOutJson,
        IReadOnlyList<(int SubIndex, string VarsJson)> approvedChildren, bool aggregateAsArray)
    {
        var result = new Dictionary<string, object?>();
        if (!TryParseMap(subVarsOutJson, out var map) || map.Count == 0) return result;
        var ordered = approvedChildren.OrderBy(c => c.SubIndex).ToList();
        foreach (var (parentVar, path) in map)
        {
            if (aggregateAsArray)
            {
                var arr = new JsonArray();
                foreach (var (_, vars) in ordered) arr.Add(ResolveNode(path, vars)?.DeepClone());
                result[parentVar] = arr;
            }
            else
            {
                result[parentVar] = ordered.Count == 0 ? null : ResolveNode(path, ordered[0].VarsJson)?.DeepClone();
            }
        }
        return result;
    }
}
