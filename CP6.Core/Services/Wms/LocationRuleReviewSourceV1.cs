using System.Globalization;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Plm;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
namespace CP6.Core.Services.Wms;

// This projection reads the existing proposal originals. It is not a second rule Owner.
public sealed record LocationRuleReviewSourceViewV1(EngineeringSourceRefV1 Reference, LocationRuleOriginalV1 Body,
    string OriginalJson, string? ParentVersion, bool IsCurrent, Guid AuthorId, Guid ActorId,
    string State, string Etag, Guid? ActiveSubmissionId, string[] Missing);
internal sealed class LocationRuleReviewOriginalsV1(CP6Context db)
{
    internal Task Fence(bool write,CancellationToken ct)=>new LocationRuleStoreV1(db).Fence(write,ct);
    internal async Task<LocationRuleReviewSourceViewV1> Read(Guid id,long? version,CancellationToken ct)
    {
        var s=new LocationRuleStoreV1(db);
        var h=await s.Heads().SingleOrDefaultAsync(x=>x.Id==id,ct)??throw Error("RULE_PROPOSAL_NOT_FOUND",404);
        var value=await s.Read(h,version,ct);var first=await s.Read(h,1,ct);
        var n=version??h.Version;
        return new(value.Reference,value.Original,value.Json,n>1?(n-1).ToString(CultureInfo.InvariantCulture):null,
            n==h.Version,first.Original.ActorId,value.Original.ActorId,h.State,h.Etag,h.ActiveSubmissionId,
            value.Original.ReferenceBinding==null?["RULE_REFERENCES_UNRESOLVED"]:[]);
    }
}
public sealed class LocationRuleReviewSourceV1(CP6Context db,string evidenceMode)
{
    internal string EvidenceMode=>evidenceMode;
    internal async Task<LocationRuleReviewSourceViewV1> ReviewSource(OperationsAccessV1 a,EngineeringSourceRefV1 reference,bool current,CancellationToken ct)
    {
        if(evidenceMode is not("REAL" or "DEMO"))throw Error("LOCATION_RULE_REVIEW_ENVIRONMENT_UNAVAILABLE",503);
        if(reference.Owner!=LocationRuleValuesV1.Owner)throw Error("RULE_REFERENCE_INVALID",422);
        var s=new LocationRuleReviewOriginalsV1(db);await s.Fence(false,ct);
        await a.RequireLocationRule(reference.Id,ct);a.RequireLocationRuleFields(false);
        var value=await s.Read(reference.Id,Revision(reference.Version),ct);
        if(value.Reference!=reference)throw Error("RULE_REFERENCE_CHANGED",412);
        if(current)
        {
            if(!value.IsCurrent)throw Error("RULE_VERSION_CHANGED",412);
            var binding=value.Body.ReferenceBinding??throw Error("RULE_REFERENCES_UNRESOLVED",422);
            var actual=await LocationRuleReferenceResolverV1.Resolve(db,a,value.Body.LocationId,value.Body.Draft,binding.Policy,ct);
            if(!ApprovalCanonicalV2.Bytes(actual).AsSpan().SequenceEqual(ApprovalCanonicalV2.Bytes(binding)))throw Error("RULE_REFERENCE_BINDING_CHANGED",412);
            a.AtCommit(async t=>{if((await s.Read(reference.Id,null,t)).Reference!=reference)throw Error("RULE_VERSION_CHANGED",412);});
        }
        a.AtCommit(async t=>{await a.RequireLocationRule(reference.Id,t);a.RequireLocationRuleFields(false);});
        return value;
    }
}
internal static class LocationRuleReviewValuesV1
{
    internal static void Text(string? value,bool required=false)
    {
        if(value==null||value.Length>4000||value.Any(char.IsControl)||required&&string.IsNullOrWhiteSpace(value))throw Error("RULE_REVIEW_TEXT_INVALID",400);
    }
    internal static void Ref(EngineeringSourceRefV1? value)
    {
        if(value==null||value.Owner!=LocationRuleValuesV1.Owner||value.Id==Guid.Empty||string.IsNullOrEmpty(value.Digest)||value.Digest.Length!=64||string.IsNullOrEmpty(value.Version)
            ||value.Digest.Any(c=>!char.IsAsciiHexDigit(c)||char.IsUpper(c)))throw Error("RULE_REFERENCE_INVALID",400);
        _=Revision(value.Version);
    }
}
