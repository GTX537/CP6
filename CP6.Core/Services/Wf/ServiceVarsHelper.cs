using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CP6.Core.Services.Wf;

/// <summary>
/// 参数模板求值上下文（§3.6 P2-2）。
/// varsJson = inst.VarsJson（表单数据）；其余为引擎上下文字段。
/// </summary>
public record ServiceTemplateCtx(
    string? varsJson,
    string actorId,
    string jobId,
    string instanceId,
    string nowUtcIso);

/// <summary>
/// OutputVars 合并结果（§3.6 P1-3）。
/// </summary>
public sealed class MergeResult
{
    public required string VarsJson { get; init; }
    public required IReadOnlyList<string> MergedKeys { get; init; }
    public required IReadOnlyList<string> SkippedKeys { get; init; }
}

/// <summary>
/// 参数模板求值 + OutputVars 合并规则（纯静态逻辑，无 I/O，§3.6）。
/// </summary>
public static class ServiceVarsHelper
{
    // ── 保留前缀（大小写敏感）────────────────────────────────────────
    private static readonly string[] ReservedExact = { "wf", "sys", "_internal" };
    private static readonly string[] ReservedPrefixes = { "wf.", "sys.", "_internal." };

    /// <summary>
    /// 求值单个模板表达式。
    /// <list type="bullet">
    ///   <item><c>$.path</c>  — 取 ctx.varsJson 的顶层/点路径值（JSONPath-lite）；缺失 → ""</item>
    ///   <item><c>$wf.actorId|jobId|instanceId|nowUtc</c>  — 引擎上下文字段</item>
    ///   <item>其余          — 原样返回（字面量）</item>
    /// </list>
    /// </summary>
    public static string ResolveValue(string template, ServiceTemplateCtx ctx)
    {
        if (template.StartsWith("$wf.", StringComparison.Ordinal))
        {
            var field = template.Substring(4);
            return field switch
            {
                "actorId"    => ctx.actorId    ?? "",
                "jobId"      => ctx.jobId      ?? "",
                "instanceId" => ctx.instanceId ?? "",
                "nowUtc"     => ctx.nowUtcIso  ?? "",
                _            => ""
            };
        }

        if (template.StartsWith("$.", StringComparison.Ordinal))
        {
            return ResolveDotPath(template.Substring(2), ctx.varsJson);
        }

        // {x} 占位等价 $.x（便于 URL 路径写法，§3.6）
        if (template.StartsWith("{", StringComparison.Ordinal) && template.EndsWith("}", StringComparison.Ordinal))
        {
            var key = template.Substring(1, template.Length - 2);
            return ResolveDotPath(key, ctx.varsJson);
        }

        // 字面量
        return template;
    }

    /// <summary>
    /// 把 executor 的 OutputVars 合并回 varsJson（§3.6 P1-3）。
    /// 规则：仅 top-level；保留前缀跳过+记 SkippedKeys；大小写敏感；
    /// value 用 System.Text.Json 序列化；覆盖已有非保留键。
    /// </summary>
    public static MergeResult MergeOutputVars(string? varsJson, Dictionary<string, object?>? output)
    {
        // 解析现有 JSON（容错空/无效 → 空对象）
        JsonObject root;
        if (!string.IsNullOrWhiteSpace(varsJson))
        {
            try   { root = JsonNode.Parse(varsJson)?.AsObject() ?? new JsonObject(); }
            catch { root = new JsonObject(); }
        }
        else
        {
            root = new JsonObject();
        }

        var merged  = new List<string>();
        var skipped = new List<string>();

        if (output != null)
        {
            foreach (var kvp in output)
            {
                if (IsReserved(kvp.Key))
                {
                    skipped.Add(kvp.Key);
                    continue;
                }

                JsonNode? node;
                try   { node = kvp.Value is null ? null : JsonSerializer.SerializeToNode(kvp.Value); }
                catch { skipped.Add(kvp.Key); continue; }   // 非 JSON 可序列化值 → 拒绝（§3.6）

                root[kvp.Key] = node;
                merged.Add(kvp.Key);
            }
        }

        return new MergeResult
        {
            VarsJson    = root.ToJsonString(),
            MergedKeys  = merged,
            SkippedKeys = skipped
        };
    }

    // ── 内部辅助 ─────────────────────────────────────────────────────

    private static string ResolveDotPath(string path, string? varsJson)
    {
        if (string.IsNullOrWhiteSpace(varsJson) || string.IsNullOrWhiteSpace(path))
            return "";

        try
        {
            JsonNode? current = JsonNode.Parse(varsJson);
            var parts = path.Split('.');
            foreach (var part in parts)
            {
                if (current is null) return "";
                current = current[part];
            }

            if (current is null) return "";

            // 字符串值去掉引号；数字/bool 返回原始文本；对象/数组返回 JSON
            if (current is JsonValue jv)
            {
                if (jv.TryGetValue<string>(out var s)) return s;
                return jv.ToJsonString();
            }

            return current.ToJsonString();
        }
        catch
        {
            return "";
        }
    }

    private static bool IsReserved(string key)
    {
        foreach (var ex in ReservedExact)
            if (string.Equals(key, ex, StringComparison.Ordinal)) return true;
        foreach (var pfx in ReservedPrefixes)
            if (key.StartsWith(pfx, StringComparison.Ordinal)) return true;
        return false;
    }
}
