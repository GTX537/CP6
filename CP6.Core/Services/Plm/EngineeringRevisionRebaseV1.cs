using System.Text.Json;
using System.Text.Json.Serialization;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
using static CP6.Core.Services.Plm.EngineeringRevisionValuesV1;

namespace CP6.Core.Services.Plm;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EngineeringRebaseSourcesV1(EngineeringRevisionRefV1 Base, EngineeringRevisionRefV1 Incoming);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EngineeringRebaseResolutionV1(string Path, string Decision, string Reason,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] JsonElement Manual = default);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EngineeringRebaseInputV1(EngineeringRebaseSourcesV1 Sources, EngineeringRebaseResolutionV1[] Resolutions);
public sealed record EngineeringRebaseChangeV1(string Path, JsonElement BaseValue, JsonElement CurrentValue, JsonElement IncomingValue, bool Conflict);
public sealed record EngineeringRebasePreviewV1(EngineeringRevisionRefV1 Revision, EngineeringRevisionRefV1 Candidate, Guid Etag,
    EngineeringRebaseSourcesV1 Sources, EngineeringRebaseChangeV1[] Changes, string[] BaseMissing, string[] IncomingMissing);
public sealed record EngineeringRebaseRecordV1(Guid OperationId, EngineeringRevisionRefV1 PreviousCandidate, EngineeringRebaseInputV1 Input,
    EngineeringSourceRefV1? PreviousChange, EngineeringSourceRefV1? IncomingChange, string Reason);

internal static class EngineeringRevisionRebaseV1
{
    private static readonly JsonSerializerOptions ManualJson = new(EngineeringProductStoreV1.Json)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict
    };

    internal static void References(EngineeringRebaseSourcesV1? sources)
    {
        if (sources == null || sources.Base == sources.Incoming) throw Error("ENGINEERING_REBASE_SOURCES_INVALID", 400);
        Validate(sources.Base);
        Validate(sources.Incoming);

        static void Validate(EngineeringRevisionRefV1? value)
        {
            if (value == null || value.Owner is not (CandidateOwner or EngineeringBaselineValuesV1.Owner)
                || value.Id == Guid.Empty || !ValidDigest(value.Digest)) throw Error("ENGINEERING_REBASE_SOURCE_INVALID", 400);
            Revision(value.Version);
            if (value.Owner == EngineeringBaselineValuesV1.Owner && value.Version != "1") throw Error("ENGINEERING_REBASE_SOURCE_INVALID", 400);
        }
    }

    internal static (EngineeringWorkingDraftV1 Draft, EngineeringRebaseChangeV1[] Changes) Compare(
        EngineeringWorkingDraftV1 basis, EngineeringWorkingDraftV1 current, EngineeringWorkingDraftV1 incoming,
        EngineeringRebaseResolutionV1[]? resolutions = null)
    {
        var decisions = new Dictionary<string, EngineeringRebaseResolutionV1>(StringComparer.Ordinal);
        if (resolutions != null)
        {
            if (resolutions.Length > 8003) throw Error("ENGINEERING_REBASE_RESOLUTION_LIMIT", 400);
            foreach (var decision in resolutions)
            {
                if (decision == null || !ValidText(decision.Path, 128)
                    || decision.Decision is not ("KEEP_BASE" or "ACCEPT_SOURCE" or "MANUAL")
                    || (decision.Decision == "MANUAL") != (decision.Manual.ValueKind != JsonValueKind.Undefined)
                    || !decisions.TryAdd(decision.Path, decision)) throw Error("ENGINEERING_REBASE_RESOLUTION_INVALID", 400);
                Text(decision.Reason);
            }
        }

        var changes = new List<EngineeringRebaseChangeV1>();
        var basisMembers = basis.Components.ToDictionary(x => x.OccurrenceId);
        var currentMembers = current.Components.ToDictionary(x => x.OccurrenceId);
        var incomingMembers = incoming.Components.ToDictionary(x => x.OccurrenceId);
        var merged = new List<EngineeringComponentDraftV1>();
        foreach (var id in currentMembers.Keys.Union(basisMembers.Keys).Union(incomingMembers.Keys))
        {
            var a = basisMembers.GetValueOrDefault(id);
            var b = currentMembers.GetValueOrDefault(id);
            var c = incomingMembers.GetValueOrDefault(id);
            var path = "components/" + id;
            // 发生位置新增或删除时按整项决定，避免生成一条只有部分字段的成员。
            var component = a == null || b == null || c == null
                ? Select(path, a, b, c)
                : new(id, Select(path + "/kind", a.Kind, b.Kind, c.Kind)!,
                    Select(path + "/path", a.Path, b.Path, c.Path)!,
                    Select(path + "/semanticOrder", a.SemanticOrder, b.SemanticOrder, c.SemanticOrder),
                    Select(path + "/applicability", a.Applicability, b.Applicability, c.Applicability)!,
                    Select(path + "/reason", a.Reason, b.Reason, c.Reason)!,
                    Select(path + "/note", a.Note, b.Note, c.Note)!,
                    Select(path + "/sourceRef", a.SourceRef, b.SourceRef, c.SourceRef));
            if (component == null) continue;
            if (component.OccurrenceId != id) throw Error("ENGINEERING_REBASE_OCCURRENCE_CHANGED", 422);
            merged.Add(component);
        }

        var draft = new EngineeringWorkingDraftV1(
            Select("productRef", basis.ProductRef, current.ProductRef, incoming.ProductRef),
            Select("goal", basis.Goal, current.Goal, incoming.Goal)!,
            Select("configuration", basis.Configuration, current.Configuration, incoming.Configuration,
                value => value.ValueKind == JsonValueKind.Null ? null : Configuration(value.Deserialize<EngineeringConfigurationInputV1>(ManualJson)!)),
            merged.ToArray());
        if (decisions.Count != 0) throw Error("ENGINEERING_REBASE_RESOLUTION_UNKNOWN", 422);
        return (draft, changes.ToArray());

        T? Select<T>(string path, T? a, T? b, T? c, Func<JsonElement, T?>? manual = null)
        {
            var @base = JsonSerializer.SerializeToElement(a, EngineeringProductStoreV1.Json);
            var currentValue = JsonSerializer.SerializeToElement(b, EngineeringProductStoreV1.Json);
            var incomingValue = JsonSerializer.SerializeToElement(c, EngineeringProductStoreV1.Json);
            if (currentValue.GetRawText() == incomingValue.GetRawText()) return b;
            var conflict = @base.GetRawText() != currentValue.GetRawText() && @base.GetRawText() != incomingValue.GetRawText();
            changes.Add(new(path, @base, currentValue, incomingValue, conflict));
            if (resolutions == null) return b;
            if (!decisions.Remove(path, out var decision)) throw Error("ENGINEERING_REBASE_RESOLUTION_REQUIRED", 409);
            if (decision.Decision == "KEEP_BASE") return a;
            if (decision.Decision == "ACCEPT_SOURCE") return c;
            try { return manual == null ? decision.Manual.Deserialize<T>(ManualJson) : manual(decision.Manual); }
            catch (JsonException) { throw Error("ENGINEERING_REBASE_MANUAL_INVALID", 422); }
        }
    }
}
