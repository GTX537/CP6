using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Mes;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
using static CP6.Core.Services.Plm.EngineeringOutputRelationsValuesV1;

namespace CP6.Core.Services.Plm;
public sealed partial class EngineeringOutputRelationsOwnerV1
{
    private sealed record SourcesV1(EngineeringManufacturingSnapshotV1 Manufacturing,EngineeringBomSnapshotV1 Mbom,
        EngineeringRoutingSnapshotV1 Routing,string Fingerprint,EngineeringSourcePinV1[] Pins,string[] Missing,string[] Downstream,Guid[] Authors);
    // Only these exact legacy placeholders have relation-level professional coverage here.
    // Physical qualification, applicability/substitution and document process gates remain explicit.
    private static bool RelationPlaceholder(string value)
    {
        var code=value.Split(':').First(x=>x is not ("EBOM" or "MBOM" or "ROUTING"));
        return code is "FORMAL_RELATION_SET_PROOFS_REQUIRED" or "MAPPING_PROFESSIONAL_EVIDENCE_REQUIRED"
            or "OUTPUT_SPECIFICATION_PROOF_REQUIRED" or "OUTPUT_RELATION_PROOF_REQUIRED" or "QUANTITY_APPLICATION_REVIEW_REQUIRED"
            or "NO_INPUT_BASIS_REQUIRED" or "RELATION_BASIS_REQUIRED"
            or "CONFIGURATION_APPLICABILITY_REVIEW_REQUIRED" or "MANUFACTURING_MAPPING_REVIEW_REQUIRED" or "MANUFACTURING_VIEW_OWNER_REQUIRED";
    }
    private static bool LaterQualification(string value)
    {
        var code=value.Split(':').First(x=>x is not ("EBOM" or "MBOM" or "ROUTING"));
        return code is "RESOURCE_REQUIRED" or "RESOURCE_QUALIFICATION_REQUIRED" or "TOOL_APPROVAL_AND_PHYSICAL_QUALIFICATION_REQUIRED"
            or "CONTROLLED_DOCUMENT_OWNER_REQUIRED" or "ROUTING_REVIEW_AND_FREEZE_REQUIRED" or "APPLICABILITY_REVIEW_REQUIRED"
            or "SUBSTITUTION_APPROVAL_REQUIRED" or "APPLICABILITY_OWNER_REQUIRED";
    }
    private async Task<SourcesV1> Sources(OperationsAccessV1 root,EngineeringOutputRelationsBodyV1 body,bool held,CancellationToken ct)
    {
        Body(body);var m=await new EngineeringManufacturingOwnerV1(db,owner).Source(root,body.Manufacturing,ct);var b=m.Body;
        var boms=new EngineeringBomOwnerV1(db,owner);
        var e=await boms.Source(root,b.EbomRef,b.RevisionOrigin.Id,b.ProductRef,"EBOM",false,ct);
        var mb=await boms.Source(root,b.MbomRef,b.RevisionOrigin.Id,b.ProductRef,"MBOM",false,ct);
        var r=await new EngineeringRoutingOwnerV1(db,owner).Source(root,b.RoutingRef,b.RevisionOrigin.Id,b.ProductRef,false,ct);
        if(!body.Inputs.Select(x=>x.OccurrenceId).Order().SequenceEqual(mb.Body.Occurrences.Select(x=>x.OccurrenceId).Order())
            || !body.Outputs.Select(x=>x.OccurrenceId).Order().SequenceEqual(b.Outputs.Select(x=>x.OutputOccurrenceId).Order()))throw Error("OUTPUT_RELATIONS_COMPLETE_OCCURRENCES_REQUIRED",422);
        // A revision origin is an immutable authoring parent; composition can legitimately advance
        // that revision. Consumers independently match the candidate's current configuration.
        var noMaterialInputs=mb.Body.Occurrences.Length==0 && b.Outputs.Length>0 && b.Outputs.All(x=>x.InputOccurrenceIds.Length==0)
            && b.Outputs.Any(x=>x.FromOutputOccurrenceIds.Length==0 && body.Outputs.Single(p=>p.OccurrenceId==x.OutputOccurrenceId).NoInputDocument!=null);
        var missing=m.Missing!.Where(x=>x!="REVISION_SOURCE_CHANGED" && !RelationPlaceholder(x) && !LaterQualification(x)
            && !(noMaterialInputs && x is "EBOM:OCCURRENCES_REQUIRED" or "MBOM:OCCURRENCES_REQUIRED")).ToList();
        var downstream=m.Missing!.Where(LaterQualification).ToArray();
        if(!m.IsCurrent)missing.Add("MANUFACTURING_SOURCE_CHANGED");
        var revisions=new EngineeringRevisionStoreV1(db);await revisions.Fence(false,ct);
        var candidate=await revisions.Read(b.RevisionOrigin.Id,null,ct);
        if(candidate.Draft.Configuration?.Reference!=b.ConfigurationRef || candidate.Draft.ProductRef!=b.ProductRef)
            missing.Add("OUTPUT_RELATIONS_CONFIGURATION_SOURCE_CHANGED");
        if(missing.Count!=0 && (b.ConfigurationRef==null || b.Outputs.Any(x=>x.ProductRef==null || x.GeneratingOperationOccurrenceId==null)))
            throw Error("OUTPUT_RELATIONS_GRAPH_INCOMPLETE",422);
        if(mb.Body.Occurrences.Any(x=>x.ItemRef?.Owner!=EngineeringProductValuesV1.Owner))throw Error("OUTPUT_RELATIONS_NATIVE_ITEM_REVISION_REQUIRED",422);
        // Closure must be real even for a saved draft. A missing proof may remain draft, but
        // unknown edges/configuration identities cannot produce a closed wire contract.
        if(missing.Any(x=>x.StartsWith("UNMAPPED_",StringComparison.Ordinal) || x.StartsWith("UNUSED_",StringComparison.Ordinal)
            || x.Contains("OCCURRENCE_MISSING",StringComparison.Ordinal) || x.Contains("OPERATION_MISSING",StringComparison.Ordinal)
            || x.StartsWith("OUTPUT_INPUT_MISSING",StringComparison.Ordinal) || x.StartsWith("INTERMEDIATE_",StringComparison.Ordinal)
            || x.StartsWith("OUTPUT_FLOW_",StringComparison.Ordinal) || x.StartsWith("OUTPUT_CONFIGURATION_",StringComparison.Ordinal)
            || x=="PRIMARY_PRODUCT_OUTPUT_REQUIRED"))throw Error("OUTPUT_RELATIONS_GRAPH_INCOMPLETE",422);
        var pins=new List<EngineeringSourcePinV1>();
        void Pin(string path,EngineeringSourceRefV1 reference,string schema,string original,object watermark,IEnumerable<string> findings)
        {
            var f=findings.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            pins.Add(new(path,reference,reference.Owner,schema,"NATIVE_SAME_UOW_RECHECK",Hash(Bytes(watermark)),original,f));
        }
        Pin("manufacturing",m.Reference,"cp6_engineering_manufacturing_v1",m.OriginalJson,new{m.Reference,m.IsCurrent},m.Missing!.Where(x=>x!="REVISION_SOURCE_CHANGED"));
        Pin("ebom",e.Reference,"cp6_engineering_bom_v1",e.OriginalJson,new{e.Reference,e.IsCurrent},e.Missing!);
        Pin("mbom",mb.Reference,"cp6_engineering_bom_v1",mb.OriginalJson,new{mb.Reference,mb.IsCurrent},mb.Missing!);
        Pin("routing",r.Reference,"cp6_engineering_routing_v1",r.OriginalJson,new{r.Reference,r.IsCurrent},r.Missing!);
        // Composition-only revision changes do not rewrite configuration identity. A real
        // configuration change does invalidate the relation even before a consumer checks it.
        if(b.ConfigurationRef is { } configuration)
            Pin("configuration",new(configuration.Owner,configuration.Id,configuration.Version,configuration.Digest),"cp6_engineering_configuration_v1",
                System.Text.Encoding.UTF8.GetString(Bytes(new{reference=configuration,configuration=candidate.Draft.Configuration})),new{candidate.Draft.Configuration?.Reference,candidate.Draft.ProductRef},
                missing.Where(x=>x=="OUTPUT_RELATIONS_CONFIGURATION_SOURCE_CHANGED"));
        async Task Spec(string path,EngineeringSourceRefV1 reference,EngineeringProductRefV1 product)
        {
            var q=await quality.Source(root,reference,product,held,ct);
            using var original=JsonDocument.Parse(q.OriginalSourceJson??throw Error("QUALITY_SPEC_ORIGINAL_UNAVAILABLE",503));
            var specification=JsonSerializer.Deserialize<QualitySpecificationBodyV1>(original.RootElement.GetProperty("body"),EngineeringProductStoreV1.Json)!;
            var f=q.Missing.ToList();if(specification.Site!=b.SiteRef)f.Add("QUALITY_SPEC_SITE_MISMATCH");
            missing.AddRange(f.Select(x=>path+":"+x));
            Pin(path,reference,"cp6_quality_specification_v1",q.OriginalSourceJson,new{q.Control,q.Approval,q.Availability,q.RequirementsDigest},f);
        }
        async Task Document(string path,EngineeringSourceRefV1 reference)
        {
            var v=await documents.ReviewSource(await root.SourceAsync(OperationsOwnerV1.ControlledDocument,ct),reference,held,ct);
            var f=v.Missing!.ToList();if(!v.IsCurrent)f.Add("DOCUMENT_SOURCE_CHANGED");if(v.State!="APPROVED")f.Add("DOCUMENT_APPROVAL_REQUIRED");
            if(v.Body.ProductRef!=b.ProductRef)f.Add("DOCUMENT_PRODUCT_MISMATCH");
            missing.AddRange(f.Select(x=>path+":"+x));Pin(path,reference,"cp6_controlled_document_v1",v.OriginalJson,new{v.IsCurrent,v.State,v.ContentStatus},f);
        }
        foreach(var p in body.Inputs.OrderBy(x=>x.OccurrenceId))
        {
            var item=mb.Body.Occurrences.Single(x=>x.OccurrenceId==p.OccurrenceId).ItemRef!;
            await Spec("inputs/"+p.OccurrenceId,p.Specification,new(item.Owner,item.Id,item.Version,item.Digest));
        }
        foreach(var p in body.Outputs.OrderBy(x=>x.OccurrenceId))
        {
            var output=b.Outputs.Single(x=>x.OutputOccurrenceId==p.OccurrenceId);
            await Spec("outputs/"+p.OccurrenceId,p.Specification,output.ProductRef!);
            await Document("relationEvidence/"+p.OccurrenceId,p.RelationDocument);
            var noInput=output.InputOccurrenceIds.Length==0 && output.FromOutputOccurrenceIds.Length==0;
            if(noInput!=(p.NoInputDocument!=null))throw Error("OUTPUT_RELATIONS_NO_INPUT_PROOF_INVALID",422);
            if(p.NoInputDocument!=null)await Document("noInputBasis/"+p.OccurrenceId,p.NoInputDocument);
        }
        var fingerprint=Hash(Bytes(pins));
        if(held && missing.Count>0)throw Error("OUTPUT_RELATIONS_SOURCES_INCOMPLETE",422);
        return new(m,mb,r,fingerprint,pins.ToArray(),missing.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),downstream,
            new[]{m.ActorId,e.ActorId,mb.ActorId,r.ActorId}.Distinct().ToArray());
    }
    private void FinalSources(OperationsAccessV1 root,EngineeringOutputRelationsBodyV1 body,SourcesV1 observed,bool held)
    {
        root.AtCommit(async t=>{
            var current=await root.EngineeringFinalObservation((a,token)=>Sources(a,body,held,token),t);
            if(current.Fingerprint!=observed.Fingerprint || !current.Missing.SequenceEqual(observed.Missing))throw Error("OUTPUT_RELATIONS_SOURCES_CHANGED_AT_COMMIT",412);
        });
    }
    internal async Task<OutputRelationsViewV1> Source(OperationsAccessV1 root,EngineeringSourceRefV1 reference,bool hold,CancellationToken ct)
    {
        Ref(reference,EngineeringOutputRelationsValuesV1.Owner);var a=await root.SourceAsync(OperationsOwnerV1.EngineeringOutputRelations,ct);
        var s=await Store(a,false,ct);var view=await View(a,s,reference.Id,Revision(reference.Version),ct);
        if(view.Version.Reference!=reference)throw Error("OUTPUT_RELATIONS_REFERENCE_CHANGED",412);
        if(hold)
        {
            if(view.Missing.Length!=0)throw Error("OUTPUT_RELATIONS_NOT_CURRENT_QUALIFIED",412);
            var current=await Sources(a,view.Version.Original.Body,true,ct);FinalSources(a,view.Version.Original.Body,current,true);
            a.AtCommit(async t=>{if((await s.Control(reference.Id,t)).Reference!=view.ControlReference)throw Error("OUTPUT_RELATIONS_CONTROL_CHANGED",412);});
        }
        return view;
    }
    internal async Task<OutputRelationsViewV1> HoldExact(OperationsAccessV1 root,EngineeringSourceRefV1 reference,EngineeringSourceRefV1 control,CancellationToken ct)
    {
        var view=await Source(root,reference,true,ct);if(view.ControlReference!=control)throw Error("OUTPUT_RELATIONS_CONTROL_CHANGED",412);return view;
    }
}
