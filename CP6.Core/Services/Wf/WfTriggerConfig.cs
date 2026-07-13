// CP6.Core/Services/Wf/WfTriggerConfig.cs
using System.Text.Json;

namespace CP6.Core.Services.Wf;

public class WfTimerTriggerConfig { public string Cron { get; set; } = ""; public string? VarsJson { get; set; } }
public class WfEventTriggerConfig { public Dictionary<string, string>? VarsMap { get; set; } }
public class WfMessageTriggerConfig { public List<string>? VarsSchema { get; set; } }

/// <summary>ConfigJson 分型解析（spec §2.3）。坏 JSON → 空配置（校验在 FlowTriggerValidator，解析不抛）。</summary>
public static class WfTriggerConfig
{
    private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };

    public static WfTimerTriggerConfig ParseTimer(string? json) => Parse<WfTimerTriggerConfig>(json) ?? new();
    public static WfEventTriggerConfig ParseEvent(string? json) => Parse<WfEventTriggerConfig>(json) ?? new();
    public static WfMessageTriggerConfig ParseMessage(string? json) => Parse<WfMessageTriggerConfig>(json) ?? new();

    private static T? Parse<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<T>(json, Opts); }
        catch (JsonException) { return null; }
    }
}
