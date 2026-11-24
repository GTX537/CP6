using System.Reflection;
using System.Text;
using CP6.Core.Services.Sys;
using CP6.Entity.Contracts.Wire;
using System.Text.Json;
using CP6.Entity.DomainModels.Wf;

namespace CP6.Core.Services.Oa;

/// <summary>通知矩阵一行（类型轴 = WfNotificationType 反射；Supported 标志驱动 UI 格子禁用）。</summary>
public record NotifyMatrixRow(string TypeKey, int TypeValue, bool InAppSupported, bool EmailSupported);

/// <summary>
/// 通知偏好矩阵纯函数（wfs-inbox-ux §2）。
/// PrefsJson.notify 新形态：{"notify":{"todoCreated":{"inApp":bool,"email":bool},...}}。
/// 三态坍缩：无行/无 notify 键/无类型键/无通道键/解析失败 → true（默认全开，D2 零迁移）。
/// 遗留扁平形态（{"notify":{"todo":bool,...,"email":bool}}）兼容：类型键非对象时回落——
/// 事件键=false → 双通道关；全局 email=false → 仅邮件关（与既有 ParseNotifyPrefs 语义逐位等价）。
/// </summary>
public static class NotifyMatrix
{
    public const int MaxPreferenceBytes = 32768;
    public const string ChannelInApp = "inApp";
    public const string ChannelEmail = "email";

    /// <summary>新类型键 → 遗留扁平键 映射（仅既有四类型有遗留形态）。</summary>
    private static readonly Dictionary<string, string> LegacyKeyMap = new()
    {
        ["todoCreated"] = "todo",
        ["flowApproved"] = "approved",
        ["flowRejected"] = "rejected",
        ["timeout"] = "timeout",
    };

    /// <summary>
    /// 通道支持清单（2026-07-05 实读核定，R1）：
    /// todoCreated/flowApproved/flowRejected = PersistentWfNotifier 三方法，站内+邮件双动作；
    /// timeout = 全库无生产者（超时提醒以 TodoCreated 发出）→ 双禁用；
    /// branchPruned = hardening spec §4.2 预留（合入 IWfNotifier.BranchPrunedAsync 即双通道生效）。
    /// 未登记的新类型默认 (inApp:true, email:false)——站内可开关、邮件保守禁用。
    /// </summary>
    private static readonly Dictionary<string, (bool InApp, bool Email)> Support = new()
    {
        ["todoCreated"]  = (true, true),
        ["flowApproved"] = (true, true),
        ["flowRejected"] = (true, true),
        ["timeout"]      = (false, false),
        ["branchPruned"] = (true, true),
    };

    public static bool IsEnabled(string prefsJson, string type, string channel)
    {
        if (string.IsNullOrWhiteSpace(prefsJson)) return true;
        try
        {
            using var doc = JsonDocument.Parse(prefsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("notify", out var notify) || notify.ValueKind != JsonValueKind.Object)
                return true;                                                      // 无 notify 键 → 默认开

            if (notify.TryGetProperty(type, out var typeEl) && typeEl.ValueKind == JsonValueKind.Object)
            {
                // 新矩阵形态：仅字面 false 为关；缺通道键/true/非布尔 → 开
                return !(typeEl.TryGetProperty(channel, out var ch) && ch.ValueKind == JsonValueKind.False);
            }

            // 遗留扁平形态回落（C2）
            if (!LegacyKeyMap.TryGetValue(type, out var legacyKey)) return true;  // 新类型无遗留形态 → 开
            var eventOn = !(notify.TryGetProperty(legacyKey, out var ev) && ev.ValueKind == JsonValueKind.False);
            if (channel == ChannelInApp) return eventOn;
            var emailOn = !(notify.TryGetProperty("email", out var em) && em.ValueKind == JsonValueKind.False);
            return eventOn && emailOn;                                            // 既有语义：事件关→整跳；email 关→仅邮件跳
        }
        catch (JsonException)
        {
            return true;                                                          // 畸形 JSON → 默认开（与 ParseNotifyPrefs 一致）
        }
    }

    public static void ValidatePreferenceWrite(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > MaxPreferenceBytes)
            throw new ScopeAuthorizationExceptionV1("NOTIFICATION_PREFERENCE_JSON_INVALID", 422);
        try
        {
            using var document = WorkflowCommandJsonV1.Parse(Encoding.UTF8.GetBytes(json), MaxPreferenceBytes);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new JsonException();
            if (document.RootElement.TryGetProperty("notify", out var notify))
                ValidateNotify(notify);
        }
        catch (JsonException)
        {
            throw new ScopeAuthorizationExceptionV1("NOTIFICATION_PREFERENCE_JSON_INVALID", 422);
        }
    }

    internal static void ValidateNotify(JsonElement notify)
    {
        if (!ValidNotify(notify))
            throw new ScopeAuthorizationExceptionV1("NOTIFICATION_PREFERENCE_MATRIX_INVALID", 422);
    }

    internal static string ReadState(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                (!document.RootElement.TryGetProperty("notify", out var notify) || ValidNotify(notify))
                ? "Valid" : "LegacyInvalidDefaulted";
        }
        catch (JsonException) { return "LegacyInvalidDefaulted"; }
    }

    public static string Summary(int type) => type switch
    {
        WfNotificationType.TodoCreated => "您有新的待办",
        WfNotificationType.FlowApproved => "您的申请已通过",
        WfNotificationType.FlowRejected => "您的申请被驳回",
        WfNotificationType.BranchPruned => "流程分支已终止",
        WfNotificationType.Timeout => "待办期限提醒",
        WfNotificationType.FormSubmitted => "表单已提交",
        WfNotificationType.TaskHandled => "任务已处理",
        _ => "流程通知"
    };

    private static bool ValidNotify(JsonElement notify)
    {
        if (notify.ValueKind == JsonValueKind.Null) return true;
        if (notify.ValueKind != JsonValueKind.Object) return false;
        var known = Rows().Select(x => x.TypeKey).ToHashSet(StringComparer.Ordinal);
        foreach (var property in notify.EnumerateObject())
        {
            if (property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                if (property.Name != ChannelEmail && !LegacyKeyMap.Values.Contains(property.Name)) return false;
                continue;
            }
            if (!known.Contains(property.Name) || property.Value.ValueKind != JsonValueKind.Object)
                return false;
            if (property.Value.EnumerateObject().Any(cell =>
                    (cell.Name is not (ChannelInApp or ChannelEmail)) ||
                    (cell.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))))
                return false;
        }
        return true;
    }

    /// <summary>类型轴 = 反射 WfNotificationType 全部 public const int（BranchPruned 合入即自动长出）。</summary>
    public static IReadOnlyList<NotifyMatrixRow> Rows() =>
        typeof(WfNotificationType)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(int))
            .Select(f =>
            {
                var key = char.ToLowerInvariant(f.Name[0]) + f.Name[1..];         // TodoCreated → todoCreated
                var (inApp, email) = Support.TryGetValue(key, out var s) ? s : (true, false);
                return new NotifyMatrixRow(key, (int)f.GetRawConstantValue()!, inApp, email);
            })
            .OrderBy(r => r.TypeValue)
            .ToList();
}
