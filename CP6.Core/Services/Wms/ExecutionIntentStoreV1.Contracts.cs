using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CP6.Core.Services.Wms;

// Internal persistence only. The adopting Owner must authenticate every reference and
// ensure that every real effect writer participates before exporting closure evidence.
internal sealed partial class ExecutionIntentStoreV1
{
    internal sealed record Space(Guid EnvironmentId, string Mode, Guid? SandboxScopeId);
    internal sealed record Authority(string Kind, Guid Id);
    internal sealed record Evidence(string Owner, Guid Id, string Version, string Digest);
    internal sealed record Alias(string Owner, string Key);
    internal sealed record Target(string Owner, string ScopeKey, string ProtectionKind, string ScopeDigest, Evidence[] ExpectedPredecessors);
    internal sealed record Plan(string IntentKind, string CandidateDigest, string ScopeDigest, Target[] RequiredOwners);
    internal sealed record Intent(Guid Id, string State, long Revision, long ExecutionEpoch, string IntentDigest, Plan Plan, string? Decision);
    internal sealed record Prepare(Guid RequestKey, Guid BindingId, Guid ExecutionIntentId, Authority Authority,
        string IntentDigest, string CandidateDigest, string ScopeDigest, Target Target);
    internal sealed record Binding(Guid Id, Prepare Prepare, string PrepareDigest);
    internal sealed record OwnerOutcome(Guid ActionKey, string ActionDigest, string Owner, Guid PrepareRequestKey, string PrepareRequestDigest, Guid ExecutionIntentId,
        string ScopeDigest, string CandidateDigest, string State, string Coverage, string? PrepareId, string? GuardEpoch,
        string? ControlRevision, Evidence? Receipt, int? PendingApplications);
    internal sealed record ActionView(Guid Id, Guid BindingId, string Kind, string Payload, string State, long AttemptEpoch,
        long AttemptRevision, string ObservationState, OwnerOutcome? LastConfirmed, OwnerOutcome? LastObservation);
    internal sealed record EffectResult(Evidence WmsReceipt, string CommandResultJson);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static InvalidOperationException Error(string code) => new(code);
    private static byte[] Bytes<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Json);
    private static T Decode<T>(byte[] value) => JsonSerializer.Deserialize<T>(value, Json)!;
    private static T Freeze<T>(T value) => Decode<T>(Bytes(value));
    private static string Hash(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    private static string TextBytes(byte[] value) => Encoding.UTF8.GetString(value);
    private static byte[] Canonical(object value)
    {
        using var doc = JsonDocument.Parse(Bytes(value)); using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) Write(doc.RootElement, writer);
        return stream.ToArray();
        static void Write(JsonElement e, Utf8JsonWriter w)
        {
            if (e.ValueKind == JsonValueKind.Object)
            { w.WriteStartObject(); foreach (var p in e.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal)) { w.WritePropertyName(p.Name); Write(p.Value, w); } w.WriteEndObject(); }
            else if (e.ValueKind == JsonValueKind.Array) { w.WriteStartArray(); foreach (var x in e.EnumerateArray()) Write(x, w); w.WriteEndArray(); }
            else e.WriteTo(w);
        }
    }
    private static void Text(string? value)
    { if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || value.Length > 500 || value.Any(char.IsControl)) throw Error("EXECUTION_IDENTITY_INVALID"); }
    private static void Digest(string? value)
    { if (value == null || value.Length != 64 || value.Any(c => c is not (>= 'a' and <= 'f' or >= '0' and <= '9'))) throw Error("EXECUTION_DIGEST_INVALID"); }
    private static void Ref(Evidence value)
    { if (value == null || value.Id == Guid.Empty) throw Error("EXECUTION_REFERENCE_INVALID"); Text(value.Owner); Text(value.Version); Digest(value.Digest); }
    private static Plan Normalize(Plan input)
    {
        if (input == null || input.IntentKind is not ("MASTER_CHANGE" or "RULE_CHANGE" or "LIFECYCLE_CHANGE" or "STOP_NEW_USE")
            || input.RequiredOwners == null || input.RequiredOwners.Length == 0) throw Error("EXECUTION_PLAN_INVALID");
        var plan = Freeze(input); Digest(plan.CandidateDigest); Digest(plan.ScopeDigest);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var target in plan.RequiredOwners)
        {
            if (target == null || target.ExpectedPredecessors == null || target.ProtectionKind != plan.IntentKind) throw Error("EXECUTION_TARGET_INVALID");
            Text(target.Owner); Text(target.ScopeKey); Digest(target.ScopeDigest);
            foreach (var predecessor in target.ExpectedPredecessors) Ref(predecessor);
            if (!keys.Add(Hash(Canonical(new { target.Owner, target.ScopeKey, target.ProtectionKind })))) throw Error("EXECUTION_TARGET_DUPLICATE");
        }
        return plan with { RequiredOwners = plan.RequiredOwners.OrderBy(x => x.Owner, StringComparer.Ordinal).ThenBy(x => x.ScopeKey, StringComparer.Ordinal).ToArray() };
    }
    private static bool Terminal(Intent intent) => intent.State is "APPLIED" or "SEALED_NO_EFFECT";
    private static void Expected(Intent intent, long revision, long epoch)
    {
        if (Terminal(intent)) throw Error("EXECUTION_TERMINAL");
        if (intent.Revision != revision || intent.ExecutionEpoch != epoch) throw Error("EXECUTION_FENCE_CONFLICT");
    }
}
