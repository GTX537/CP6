using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CP6.Core.Services.Sys;
using CP6.Entity.Contracts.Wire;

namespace CP6.Core.Services.Wf;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DefinitionCloneBodyV2(Guid SourceVersionId, string NewKey, string NewName);

internal sealed record DefinitionCloneCommandV2(string SourceKey, string Id, DefinitionCloneBodyV2 Body,
    string Digest, byte[] Raw, string? Correlation)
{
    internal static DefinitionCloneCommandV2 Parse(string sourceKey, byte[] raw)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sourceKey) || sourceKey.Length > 100 || sourceKey.Contains('\0')) throw new JsonException();
            using var doc = WorkflowCommandJsonV1.Parse(raw); var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Any(p => p.Name is not ("commandId" or "expectedVersion" or "body" or "clientCorrelationId"))
                || root.GetProperty("expectedVersion").ValueKind != JsonValueKind.Null) throw new JsonException();
            var id = root.GetProperty("commandId").GetString();
            if (id == null || !Regex.IsMatch(id, "\\A[A-Za-z0-9_.-]{1,100}\\z")) throw new JsonException();
            var correlation = root.TryGetProperty("clientCorrelationId", out var c) ? c.GetString() : null;
            if (correlation?.Length > 100) throw new JsonException();
            var body = root.GetProperty("body").Deserialize<DefinitionCloneBodyV2>(new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = false }) ?? throw new JsonException();
            if (body.SourceVersionId == Guid.Empty || string.IsNullOrWhiteSpace(body.NewKey) || body.NewKey.Length > 100 || body.NewKey.Contains('\0')
                || string.IsNullOrWhiteSpace(body.NewName) || body.NewName.Length > 200 || body.NewName.Contains('\0')) throw new JsonException();
            // The command slot belongs to the new definition; bind the source route as well as its exact version.
            var intent = JsonSerializer.SerializeToElement(new { sourceKey, body });
            return new(sourceKey, id, body, WorkflowCommandJsonV1.Digest(intent, null), raw.ToArray(), correlation);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException)
        { throw new ScopeAuthorizationExceptionV1("COMMAND_SHAPE_INVALID", 400); }
    }
}
