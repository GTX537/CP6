using System.Text.Json;
using CP6.Core.Services.Integration;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Plm;
public sealed partial class EngineeringSourceCheckService
{
    private async Task OutputRelations(OperationsAccessV1 root,Observation o,EngineeringSourceRefV1 reference,
        EngineeringRevisionSnapshotV1 revision,CancellationToken ct)
    {
        if(outputRelations==null || !outputRelations.Owns(db))throw Error("OUTPUT_RELATIONS_PARTICIPANT_REQUIRED",503);
        var view=await outputRelations.Source(root,reference,false,ct);var original=view.Version.Original;
        var m=JsonSerializer.Deserialize<EngineeringManufacturingStoredV1>(original.ManufacturingOriginal,EngineeringProductStoreV1.Json)
            ??throw Error("OUTPUT_RELATIONS_ORIGINAL_UNAVAILABLE",503);
        var missing=view.Missing.ToList();
        if(original.RevisionOrigin.Id!=revision.Reference.Id || original.Product!=revision.Draft.ProductRef
            || m.Body.ConfigurationRef!=revision.Draft.Configuration?.Reference)missing.Add("OUTPUT_RELATIONS_CANDIDATE_MISMATCH");
        var expected=new[]{("EBOM",m.Body.EbomRef),("MBOM",m.Body.MbomRef),("ROUTING",m.Body.RoutingRef)};
        if(expected.Any(pair=>!revision.Draft.Components.Any(x=>x.Kind==pair.Item1 && x.SourceRef==pair.Item2 && x.Applicability=="APPLIES")))
            missing.Add("OUTPUT_RELATIONS_COMPOSITION_MISMATCH");
        // This original includes the separate wire bytes/digest, manufacturing quantities,
        // exact native specifications/documents and current professional control identity.
        var sourceOriginal=System.Text.Encoding.UTF8.GetString(EngineeringOutputRelationsValuesV1.Bytes(new{
            schema="Main.Plm.EngineeringOutputRelationsSource/1",versionReference=view.Version.Reference,versionOriginalJson=view.Version.OriginalJson,
            view.ControlReference,view.Control,view.Policy,view.Qualification}));
        o.Pin("outputRelations",reference,"Main.Plm.EngineeringOutputRelationsSource/1",sourceOriginal,
            new{view.ControlReference,qualification=view.Qualification?.Reference,view.Missing,view.DownstreamMissing},missing);
        if(missing.Count!=0)return;
        await outputRelations.HoldExact(root,reference,view.ControlReference,ct);
        var refs=expected.Select(x=>x.Item2).ToHashSet();
        for(var i=0;i<o.Items.Count;i++)
        {
            var item=o.Items[i];var code=item.Code.Split(':')[0];
            if(item.Source!=null && refs.Contains(item.Source) && (code is "MANUFACTURING_MAPPING_REVIEW_REQUIRED" or "MANUFACTURING_VIEW_OWNER_REQUIRED" or "QUANTITY_APPLICATION_REVIEW_REQUIRED"
                || code=="OCCURRENCES_REQUIRED" && original.Body.Inputs.Length==0))
                o.Items[i]=new(item.Path,"PASS","EXACT_OUTPUT_RELATIONS_QUALIFICATION","Independent professional confirmation covers this exact manufacturing mapping and quantity original",reference);
        }
        foreach(var gap in view.DownstreamMissing)o.Item("outputRelations/downstream","UNKNOWN",gap,source:reference);
    }
}
