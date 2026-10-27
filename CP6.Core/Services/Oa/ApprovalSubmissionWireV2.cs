using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CP6.Core.Services.Sys;
using CP6.Entity.Contracts.Wire;

namespace CP6.Core.Services.Oa;

public sealed record ApprovalSubmissionAliasV2(Guid RequestedSubmissionId, Guid CanonicalSubmissionId, Guid AliasId);
public sealed record ApprovalSubmissionAckV2(Guid SubmissionId, Guid? InstanceId, string State,
    Guid? BindingRevisionId, Guid? BindingTimelineRevisionId, Guid? FlowDefVersionId,
    ApprovalSubjectV2 Subject, ApprovalPolicyV2 Policy, string RowVersion, string? ErrorCode, ApprovalSubmissionAliasV2? Alias);
public sealed record ApprovalIntakeResultV2(int Status, ApprovalSubmissionAckV2 Ack);
public sealed record ApprovalServiceIdentityV2(Guid Environment, Guid Tenant, Guid Principal, string Client, string Audience, string Version, bool CanSubmit, string EvidenceMode="REAL");
public interface IApprovalServiceAuthorityV2
{
    // Called inside the actual native authentication transaction, including immediately before commit.
    Task<ApprovalServiceIdentityV2> CurrentAsync(CancellationToken ct);
}

internal static class ApprovalSubmissionWireV2
{
    internal sealed record Parsed(ApprovalSubmitCommandV2 Value, byte[] Canonical, string Full, string Core, string IntentCore, string Intent);
    internal static ScopeAuthorizationExceptionV1 Error(string code, int status = 422) => new(code, status);
    internal static void Shape(JsonElement e, params string[] fields)
    {
        if(e.ValueKind!=JsonValueKind.Object || !e.EnumerateObject().Select(p=>p.Name).Order(StringComparer.Ordinal).SequenceEqual(fields.Order(StringComparer.Ordinal))) throw new JsonException();
    }
    internal static void Key(JsonElement e, int max=100)
    {if(e.ValueKind!=JsonValueKind.String || string.IsNullOrWhiteSpace(e.GetString()) || e.GetString()!.EnumerateRunes().Count()>max)throw new JsonException();}
    internal static void Id(JsonElement e, bool nullable=false)
    {if(nullable&&e.ValueKind==JsonValueKind.Null)return;var s=e.GetString();if(!Guid.TryParseExact(s,"D",out var id)||id==Guid.Empty||s!=id.ToString("D"))throw new JsonException();}
    internal static void Ref(JsonElement e)
    {Shape(e,"owner","id","version");foreach(var p in e.EnumerateObject())Key(p.Value);}
    internal static void Digest(JsonElement e)
    {Shape(e,"algorithm","canonicalizationVersion","value");Key(e.GetProperty("canonicalizationVersion"));if(e.GetProperty("algorithm").GetString()!="sha256"||!Regex.IsMatch(e.GetProperty("value").GetString()??"",@"\A[0-9a-f]{64}\z"))throw new JsonException();}
    internal static void Engineering(JsonElement e)
    {
        if(e.ValueKind==JsonValueKind.Null)return;
        var kind=e.GetProperty("kind").GetString();
        switch(kind)
        {
            case "ITER":Shape(e,"kind","roundId","candidateId","candidateVersion","digest","policyVersion","reviewPackageRef","reviewPackageDigest","technicalManifestDigest","purpose");break;
            case "ENG":Shape(e,"kind","candidateId","candidateVersion","technicalManifestDigest","reviewRoundRef","reviewPackageRef","reviewPackageDigest","policyVersion","parentBaselineRef","purpose");break;
            case "REL":Shape(e,"kind","baselineRef","purpose","scopeRef","scopeDigest","permitPolicyRef");break;
            case "CHG":Shape(e,"kind","ecoRef","scopeRevision","assessmentRef","assessmentDigest","signedScopeRows","stage","targetBaselineRef","purpose");break;
            default:throw new JsonException();
        }
        foreach(var p in e.EnumerateObject())
        {
            if(p.Name=="signedScopeRows")
            {
                if(p.Value.GetArrayLength()>1000)throw new JsonException();
                foreach(var row in p.Value.EnumerateArray()){Shape(row,"rowId","scopeRef","scopeDigest");Key(row.GetProperty("rowId"));Ref(row.GetProperty("scopeRef"));Digest(row.GetProperty("scopeDigest"));}
            }
            else if(p.Name.EndsWith("Ref",StringComparison.Ordinal))
            {if(p.Value.ValueKind==JsonValueKind.Null && p.Name is ("parentBaselineRef" or "targetBaselineRef"))continue;Ref(p.Value);}
            else if(p.Name.EndsWith("Digest",StringComparison.Ordinal)||p.Name=="digest")
            {if(p.Value.ValueKind==JsonValueKind.Null && kind=="ITER" && p.Name=="technicalManifestDigest")continue;Digest(p.Value);}
            else Key(p.Value);
        }
        if(kind=="CHG" && e.GetProperty("stage").GetString() is not("G2" or "G4" or "G5"))throw new JsonException();
        if(kind=="CHG" && e.GetProperty("stage").GetString() is ("G4" or "G5") && e.GetProperty("targetBaselineRef").ValueKind==JsonValueKind.Null)
            throw Error("BASELINE_REQUIRED");
    }
    internal static Parsed Parse(byte[] raw)
    {
        if(raw.Length>1048576)throw Error("SUBMISSION_SHAPE_INVALID");
        StrictJsonDocument doc;
        try{_=new UTF8Encoding(false,true).GetString(raw);doc=WorkflowCommandJsonV1.Parse(raw);}
        catch(Exception e)when(e is JsonException or DecoderFallbackException){throw Error("MALFORMED_JSON",400);}
        using(doc)try
        {
            var root=doc.RootElement;
            Shape(root,"schema","scope","commandId","submissionId","subject","policy","roundId","replacesSubmissionId","businessSubmittedRowVersion","starterId","submitAuthorizationRef","separationSubjectIds","snapshot","engineering","reason","ownerOutboxId","ownerFrozenAt");
            if(root.GetProperty("schema").GetString()!="oa.app/2.0")throw Error("UNSUPPORTED_SCHEMA");
            var scope=root.GetProperty("scope");Shape(scope,"environmentId","tenantId");Key(scope.GetProperty("environmentId"));Id(scope.GetProperty("tenantId"));
            foreach(var f in new[]{"commandId","submissionId","starterId","ownerOutboxId"})Id(root.GetProperty(f));Id(root.GetProperty("replacesSubmissionId"),true);
            var subject=root.GetProperty("subject");Shape(subject,"businessOwner","bizType","subjectKind","subjectId","businessRevision","contentDigest");
            foreach(var p in subject.EnumerateObject())if(p.Name=="contentDigest")Digest(p.Value);else Key(p.Value,p.Name switch{"businessRevision"=>128,"bizType" or "subjectKind"=>50,_=>100});
            var policy=root.GetProperty("policy");Shape(policy,"ref","version","digest");Ref(policy.GetProperty("ref"));Key(policy.GetProperty("version"));Digest(policy.GetProperty("digest"));
            if(policy.GetProperty("version").GetString()!=policy.GetProperty("ref").GetProperty("version").GetString())throw Error("POLICY_REF_VERSION_MISMATCH");
            if(root.GetProperty("roundId").ValueKind!=JsonValueKind.Null)Key(root.GetProperty("roundId"));Ref(root.GetProperty("submitAuthorizationRef"));
            var ids=root.GetProperty("separationSubjectIds");if(ids.GetArrayLength()>1000)throw new JsonException();foreach(var id in ids.EnumerateArray())Id(id);
            var snap=root.GetProperty("snapshot");Shape(snap,"schema","canonicalBytesBase64","digest","immutableEvidenceRefs");Key(snap.GetProperty("schema"));Digest(snap.GetProperty("digest"));
            var bytes=Convert.FromBase64String(snap.GetProperty("canonicalBytesBase64").GetString()!);
            if(bytes.Length>262144||Convert.ToBase64String(bytes)!=snap.GetProperty("canonicalBytesBase64").GetString()||ApprovalCanonicalV2.Hash(bytes)!=snap.GetProperty("digest").GetProperty("value").GetString())throw Error("SNAPSHOT_SCHEMA_INVALID");
            var refs=snap.GetProperty("immutableEvidenceRefs");if(refs.GetArrayLength()>1000)throw new JsonException();foreach(var r in refs.EnumerateArray())Ref(r);
            var version=root.GetProperty("businessSubmittedRowVersion").GetString()!;var rv=Convert.FromBase64String(version);if(rv.Length!=8||Convert.ToBase64String(rv)!=version)throw new JsonException();
            var time=root.GetProperty("ownerFrozenAt").GetString();if(!DateTimeOffset.TryParseExact(time,"yyyy-MM-dd'T'HH:mm:ss.fff'Z'",CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out _))throw new JsonException();
            var reason=root.GetProperty("reason").GetString();if(reason==null||reason.EnumerateRunes().Count()>500)throw new JsonException();
            Engineering(root.GetProperty("engineering"));
            var value=root.Deserialize<ApprovalSubmitCommandV2>(ApprovalBindingOwnerV2.Json)!;
            if(value.Engineering is { } engineering && engineering.GetProperty("kind").GetString()=="ITER" && engineering.GetProperty("roundId").GetString()!=value.RoundId)throw Error("ENGINEERING_SHAPE_INVALID");
            var canonical=ApprovalCanonicalV2.Bytes(root);var node=JsonNode.Parse(canonical)!.AsObject();node.Remove("commandId");node.Remove("ownerOutboxId");
            var core=ApprovalCanonicalV2.Hash(ApprovalCanonicalV2.Bytes(node));node.Remove("submissionId");node.Remove("reason");
            var intent=ApprovalCanonicalV2.Hash(ApprovalCanonicalV2.Bytes(new{value.Scope,Owner=value.Subject.BusinessOwner,value.Subject.SubjectKind,value.Subject.SubjectId,
                value.Subject.BusinessRevision,value.Subject.ContentDigest,PolicyIdentity=value.Policy,value.RoundId}));
            return new(value,canonical,ApprovalCanonicalV2.Hash(canonical),core,ApprovalCanonicalV2.Hash(ApprovalCanonicalV2.Bytes(node)),intent);
        }
        catch(Exception e)when(e is JsonException or FormatException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        {if(e is ScopeAuthorizationExceptionV1)throw;throw Error("SUBMISSION_SHAPE_INVALID");}
    }
}
