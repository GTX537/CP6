using System.Text;
using System.Text.Json;
using CP6.Core.Services.Oa;
using static CP6.Core.Services.Plm.IterationReviewCanonicalV1;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
namespace CP6.Core.Services.Plm;

// These are versioned local mapping envelopes. They wrap accurate provider bytes and
// explicitly identify themselves; they never pretend to be new references signed by ENG.
internal static class IterationReviewMappingV1
{
    private const string RefSchema="""{"type":"object","additionalProperties":false,"required":["owner","id","version","digest"],"properties":{"owner":{"type":"string"},"id":{"type":"string","format":"uuid"},"version":{"type":"string"},"digest":{"type":"string","pattern":"^[0-9a-f]{64}$"}}}""";
    private const string DigestSchema="""{"type":"object","additionalProperties":false,"required":["algorithm","canonicalizationVersion","value"],"properties":{"algorithm":{"const":"sha256"},"canonicalizationVersion":{"type":"string"},"value":{"type":"string","pattern":"^[0-9a-f]{64}$"}}}""";
    private static readonly string TechnicalSchema="{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"sourceCheck\",\"originalJson\"],\"properties\":{\"sourceCheck\":"+RefSchema+",\"originalJson\":{\"type\":\"string\"}}}";
    // Registration evidence is an exact immutable snapshot of the native registration,
    // with its separately versioned native UUID revision; no credential material is present.
    private const string RegistrationSchema="""{"type":"object","additionalProperties":false,"required":["environmentId","tenantId","owner","bizType","subjectKind","revision","snapshotSchema","principal","audience","route","evidenceMode","enabled"],"properties":{"environmentId":{"type":"string"},"tenantId":{"type":"string"},"owner":{"type":"string"},"bizType":{"type":"string"},"subjectKind":{"type":"string"},"revision":{"type":"string"},"snapshotSchema":{"type":"string"},"principal":{"type":"string"},"audience":{"type":"string"},"route":{"type":"string"},"evidenceMode":{"type":"string"},"enabled":{"type":"boolean"}}}""";
    private static IterationReviewNativeRefV1 Native(string id,string schema,object value)
    {
        var bytes=Bytes(value);return new(new(id,"1",Hash(Encoding.UTF8.GetBytes(schema))),Convert.ToBase64String(bytes),Hash(bytes),bytes.Length);
    }
    internal static IterationReviewNativeRefV1 Reference(EngineeringSourceRefV1 reference)=>Native("Main.Plm.IterationReview.native-reference/1",RefSchema,reference);
    internal static IterationReviewNativeRefV1 Registration(IterationReviewRegistrationV1 registration)=>Native("Main.Plm.IterationReview.registration/1",RegistrationSchema,registration);
    internal static IterationReviewMemberV1 Member(IterationCandidateSnapshotV1 value)=>new(value.Reference.Id.ToString("D"),value.Reference.Version,
        Native("Main.Plm.IterationReview.technical-reference/1",TechnicalSchema,new{sourceCheck=value.Original.Definition.SourceCheck,originalJson=value.Original.Definition.TechnicalSourceOriginal}),
        Native("Main.Plm.IterationReview.original-digest/1",DigestSchema,new ApprovalDigestV2("sha256","Main.Plm.IterationCandidate.raw-json/1",value.Reference.Digest)));
    internal static IterationReviewAggregateV1 Aggregate(IterationReviewPackageV1 package,Guid aggregateId)
    {
        var members=package.Candidates.OrderBy(c=>c.Reference.Id.ToString("D"),StringComparer.Ordinal).Select(Member).ToArray();
        if(members.Length==0 || members.Select(m=>m.CandidateId).Distinct(StringComparer.Ordinal).Count()!=members.Length)throw Error("ITERATION_REVIEW_CANDIDATE_DUPLICATE",400);
        var body=new{schema=AggregateSchema,scope=package.Scope,iterationId=package.Iteration.Id.ToString("D"),roundId=package.RoundId.ToString("D"),aggregateId,aggregateVersion="1",members};
        return new(AggregateSchema,package.Scope,body.iterationId,body.roundId,aggregateId,"1",members,Domain(AggregateSchema,body));
    }
    internal static IterationReviewSnapshotV1 Snapshot(IterationReviewPackageV1 package,byte[] packageBytes,IterationReviewPolicyViewV1 policy,
        IterationReviewRegistrationV1 registration,Guid aggregate,Guid binding,Guid submission)
    {
        var members=Aggregate(package,aggregate);var packageDigest=Digest(PackageSchema,packageBytes);
        var packageRef=new EngineeringSourceRefV1(IterationReviewStoreV1.PackageOwner,submission,"1",packageDigest.Value);
        // A multi-candidate review has no single original technical manifest. Keep the
        // legitimate nullable value, and retain every candidate manifest in the full package.
        var manifest=package.Candidates.Length==1?package.Candidates[0].Original.Definition.TechnicalSource.Manifest:null;
        var technical=manifest==null?null:new ApprovalDigestV2("sha256",manifest.Canonicalization,manifest.Digest);
        var map=new IterationReviewBindingV1(MappingSchema,"1.0.0",package.Scope,Registration(registration),binding,members.RoundId,members.IterationId,
            members.AggregateId,members.AggregateVersion,members.AggregateDigest,Reference(packageRef),packageDigest,Reference(policy.Reference),policy.Reference.Version,
            new("sha256",ApprovalCanonicalV2.Codec,policy.Reference.Digest),technical,policy.Fact.Body.RejectDisposition,submission,"PLM_ITER_REVIEW_DECISION",members.RoundId);
        return new(SnapshotSchema,members,map);
    }
    internal static JsonElement Engineering(IterationReviewSnapshotV1 snapshot)=>JsonSerializer.SerializeToElement(new{
        kind="ITER",roundId=snapshot.Binding.OaRoundKey,candidateId=snapshot.Aggregate.AggregateId.ToString("D"),candidateVersion=snapshot.Aggregate.AggregateVersion,
        digest=snapshot.Binding.ReviewPackageDigest,policyVersion=snapshot.Binding.PolicyVersion,
        reviewPackageRef=new ApprovalRefV2(IterationReviewStoreV1.PackageOwner,snapshot.Binding.SubmissionId.ToString("D"),"1"),
        reviewPackageDigest=snapshot.Binding.ReviewPackageDigest,technicalManifestDigest=snapshot.Binding.TechnicalManifestDigest,purpose="DESIGN_REVIEW"},ApprovalBindingOwnerV2.Json);
}
public sealed record IterationReviewRegistrationV1(Guid EnvironmentId,Guid TenantId,string Owner,string BizType,string SubjectKind,
    Guid Revision,string SnapshotSchema,Guid Principal,string Audience,string Route,string EvidenceMode,bool Enabled);
