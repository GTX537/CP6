using System.Globalization;
using System.Text;
using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.ErpIntegration;
using CP6.Platform.Messaging.NativeContracts;

namespace CP6.Core.Services.Plm;

internal static class EngineeringIntakeWireV1
{
    internal const string SourceSystem = "CRM_HO", Purpose = "ENGINEERING_INTAKE", SourceKind = "CRM_OPPORTUNITY_REQUIREMENT";
    internal const string RequestContract = "HO-ENG-REQUEST/1", IntakeContract = "HO-ENG-INTAKE/1", ProgressContract = "HO-ENG-PROGRESS/1";
    internal const string SourceOwner = "Main.Plm.EngineeringIntake.v1";
    internal const int MaxBody = 1_048_576;

    internal static readonly JsonSerializerOptions WireJson = new(CommercialCanonicalV1.Json)
    {
        PropertyNameCaseInsensitive = false,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true
    };

    internal static OrderPriceCorrectionException Failure(string code, int status) => new(code, status);

    internal static T Read<T>(byte[] bytes, Action<T>? validate = null) where T : class
    {
        if (bytes.Length is < 2 or > MaxBody) throw Failure("ENGINEERING_INTAKE_CONTRACT_INVALID", 400);
        try
        {
            using var json = Cp6NativeJson.Parse(bytes);
            var input = json.RootElement.Deserialize<T>(WireJson) ?? throw new JsonException();
            if (!CommercialCanonicalV1.Encode(json.RootElement).AsSpan().SequenceEqual(CommercialCanonicalV1.Data(input)))
                throw new JsonException();
            validate?.Invoke(input);
            return input;
        }
        catch (Exception error) when (error is JsonException or CommercialProtocolException or KeyNotFoundException
            or DecoderFallbackException)
        {
            throw Failure("ENGINEERING_INTAKE_CONTRACT_INVALID", 400);
        }
    }

    internal static EngineeringIntakeScopeV1 Scope(EngineeringCaseNaturalKeyV1 key) =>
        new(key.TenantId, key.TargetAuthority, key.LegalEntityKey, key.OpportunityId);
    internal static string Version(long version) => version.ToString(CultureInfo.InvariantCulture);
    internal static string Hash(byte[] bytes) => CommercialOriginalV1.Hash(bytes);
    internal static byte[] SourceBytes(byte[] request)
    {
        using var json = Cp6NativeJson.Parse(request);
        return Encoding.UTF8.GetBytes(json.RootElement.GetProperty("source").GetRawText());
    }

    internal static string SourceDigest(EngineeringIntakeProofKeyV1 key, EngineeringIntakeSourceV1 source) =>
        key.Sign("HO-ENG-SOURCE-1", CanonicalSource(source));

    internal static EngineeringIntakeSourceV1 CanonicalSource(EngineeringIntakeSourceV1 source) =>
        source with
        {
            ActivitySources = source.ActivitySources.OrderBy(item => item.SourceId.ToString("D"), StringComparer.Ordinal)
                .ThenBy(item => item.SourceVersion, StringComparer.Ordinal).ThenBy(item => item.ReferenceSetVersion, StringComparer.Ordinal).ToArray(),
            EvidenceRefs = source.EvidenceRefs.OrderBy(item => item.Owner, StringComparer.Ordinal)
                .ThenBy(item => item.Key, StringComparer.Ordinal).ThenBy(item => item.Version, StringComparer.Ordinal).ToArray()
        };

    internal static void NextQuery(EngineeringNextQueryV1? input)
    {
        Require(input is not null);
        NaturalKey(input.CaseNaturalKey);
        if (input.CurrentSubmissionVersion == "0")
        {
            Require(input.CaseId is null && input.CurrentRequestKey is null && input.CurrentReceiptId is null && input.CurrentSource is null);
            return;
        }
        _ = CommercialWireV1.Revision(input.CurrentSubmissionVersion);
        Require(input.CaseId is { } id && id != Guid.Empty && input.CurrentRequestKey is { } request && request != Guid.Empty);
        if (input.CurrentReceiptId is not null) Key(input.CurrentReceiptId);
        if (input.CurrentSource is not null) Binding(input.CurrentSource, input.CaseNaturalKey.OpportunityId);
    }

    internal static void Request(EngineeringIntakeRequestV1 input)
    {
        NaturalKey(input.CaseNaturalKey);
        Require(input.SchemaVersion == RequestContract && input.Producer == SourceSystem && input.TenantId == input.CaseNaturalKey.TenantId
            && input.EventId != Guid.Empty && input.CaseId != Guid.Empty && input.RequestKey != Guid.Empty);
        Key(input.Epoch);
        Text(input.RequestReason, 2000);
        Require(input.Mode is "CREATE" or "REVISE");
        if (input.Mode == "CREATE") Require(input.Target is null);
        else Reference(input.Target);
        var next = input.NextAuthorization;
        Require(next is not null);
        NextQuery(next!.Query);
        Require(next.Query.CaseNaturalKey == input.CaseNaturalKey);
        if (next.Target is not null) Reference(next.Target);
        Evidence(next.PriorExecutionFence);
        Evidence(next.Decision);
        Require(next.BlockingCodes is not null);
        Key(next.RouteVersion);
        Stream(next.CanonicalIntakeStream, IntakeContract);
        Stream(next.CanonicalProgressStream, ProgressContract);
        if (next.MainCaseRevision != "0") _ = CommercialWireV1.Revision(next.MainCaseRevision);
        var sequence = CommercialWireV1.Revision(input.SubmissionVersion);
        var previous = input.Previous;
        if (previous is null)
            Require(sequence == 1 && next.Query.CaseId is null);
        else
        {
            Key(previous.ReceiptId);
            Binding(previous.Source, input.CaseNaturalKey.OpportunityId);
            Require(next.Query.CaseId == input.CaseId && next.Query.CurrentRequestKey == previous.RequestKey
                && next.Query.CurrentReceiptId == previous.ReceiptId && next.Query.CurrentSource == previous.Source
                && next.MainCaseRevision == previous.MainCaseRevision && next.Query.CurrentSubmissionVersion == previous.SubmissionVersion
                && sequence - 1 == CommercialWireV1.Revision(previous.SubmissionVersion));
        }

        var source = input.Source;
        Require(source is not null && source.SchemaVersion == "HO-ENG-SOURCE/1" && source.TenantId == input.TenantId
            && source.SourceKind == SourceKind && source.SourceId == input.CaseNaturalKey.OpportunityId && source.AccountId != Guid.Empty);
        Binding(input.SourceBinding, input.CaseNaturalKey.OpportunityId);
        Require(source!.SourceVersion == input.SourceBinding.SourceVersion);
        CommercialWireV1.Requirement(source.Requirement);
        var lines = source.Requirement.GetProperty("lines").EnumerateArray().ToArray();
        Require(lines.All(line => line.GetProperty("lineId").ValueKind == JsonValueKind.String
                && line.GetProperty("clientRef").ValueKind == JsonValueKind.Null)
            && lines.Select(line => line.GetProperty("lineId").GetString()).Distinct(StringComparer.Ordinal).Count() == lines.Length);
        Texts(source.KnownConstraints);
        Texts(source.Unknowns);
        if (source.CandidateProduct is { } product)
        {
            Key(product.Owner);
            Key(product.Key);
            Key(product.Version);
        }
        Require(source.ActivitySources is { Length: <= 20 } && source.EvidenceRefs is { Length: <= 20 }
            && input.ActivityAttestations is not null && input.ActivityAttestations.Length == source.ActivitySources.Length);
        foreach (var activity in source.ActivitySources)
        {
            Require(activity is not null && activity.SourceOwner == "CRM_ACTIVITY" && activity.SourceKind == "CRM_ACTIVITY"
                && activity.SourceId != Guid.Empty && CommercialWireV1.IsDigest(activity.SourceDigest));
            _ = CommercialWireV1.Revision(activity!.SourceVersion);
            _ = CommercialWireV1.Revision(activity.ReferenceSetVersion);
            Key(activity.DigestKeyVersion);
        }
        Require(source.ActivitySources.Select(x => (x.SourceId, x.SourceVersion, x.ReferenceSetVersion)).Distinct().Count()
            == source.ActivitySources.Length);
        var attestations = new HashSet<EngineeringIntakeActivityV1>();
        foreach (var attestation in input.ActivityAttestations)
        {
            Require(attestation is not null && source.ActivitySources.Contains(attestation.Identity) && attestations.Add(attestation.Identity));
            Key(attestation!.Issuer);
            Key(attestation.AttestationVersion);
            Key(attestation.ProofRef);
        }
        var refs = new HashSet<(string, string, string)>();
        foreach (var evidence in source.EvidenceRefs)
        {
            Require(evidence is not null && evidence.UsePurpose == Purpose && refs.Add((evidence.Owner, evidence.Key, evidence.Version)));
            Key(evidence!.Owner);
            Key(evidence.Key);
            Key(evidence.Version);
        }
        Evidence(input.SourceUseEvidence);
    }

    internal static void OriginalQuery(EngineeringIntakeOriginalQueryV1 input)
    {
        Require(input.SchemaVersion == "HO-ORIGINAL-QUERY/1" && input.TenantId != Guid.Empty && input.MaxResults == 100
            && input.Request is { Kind: "ENGINEERING" } original && original.CaseId != Guid.Empty && original.RequestKey != Guid.Empty
            && CommercialWireV1.IsDigest(original.SourceDigest));
        Key(input.Authority, 64);
        Key(input.LegalEntityKey);
        _ = CommercialWireV1.Revision(input.Request.SubmissionVersion);
        if (input.PageToken is not null) throw Failure("CURSOR_STALE", 409);
    }

    internal static void Stream(EngineeringIntakeStreamV1? stream, string contract)
    {
        Require(stream is not null && stream.ContractVersion == contract);
        Key(stream!.Producer);
        Key(stream.Epoch);
    }

    private static void NaturalKey(EngineeringCaseNaturalKeyV1? key)
    {
        Require(key is not null && key.TenantId != Guid.Empty && key.OpportunityId != Guid.Empty
            && key.SourceSystem == SourceSystem && key.Purpose == Purpose);
        Key(key!.TargetAuthority, 64);
        Key(key.LegalEntityKey);
    }

    private static void Binding(EngineeringIntakeBindingV1? binding, Guid opportunityId)
    {
        Require(binding is not null && binding.SourceId == opportunityId && binding.SourceKind == SourceKind
            && CommercialWireV1.IsDigest(binding.SourceDigest));
        _ = CommercialWireV1.Revision(binding!.SourceVersion);
        Key(binding.DigestKeyVersion);
    }

    private static void Evidence(EngineeringIntakeEvidenceV1? evidence)
    {
        Require(evidence is not null);
        Key(evidence!.Authority);
        Key(evidence.Key);
        Key(evidence.Version);
        Key(evidence.ProofRef);
    }

    private static void Reference(EngineeringIntakeRefV1? value)
    {
        Require(value is not null);
        Key(value!.EngineeringRequestId);
        Key(value.EngineeringRevision);
    }

    private static void Texts(string[]? values)
    {
        Require(values is { Length: <= 30 } && values.Distinct(StringComparer.Ordinal).Count() == values.Length);
        foreach (var value in values!) Text(value, 1000);
    }

    private static void Text(string? value, int max) =>
        Require(!string.IsNullOrWhiteSpace(value) && value.EnumerateRunes().Count() <= max
            && !value.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')));
    private static void Key(string? value, int max = 128) => _ = CommercialWireV1.RequireKey(value, max);
    private static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool valid)
    {
        if (!valid) throw Failure("ENGINEERING_INTAKE_CONTRACT_INVALID", 400);
    }
}
