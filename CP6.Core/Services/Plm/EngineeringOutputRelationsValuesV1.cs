using System.Text;
using CP6.Core.Services.Oa;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Plm;

internal static class EngineeringOutputRelationsValuesV1
{
    internal const string Owner="Main.Plm.EngineeringOutputRelations.v1", ControlOwner="Main.Plm.EngineeringOutputRelationsControl.v1",
        Journal="Main.Plm.EngineeringOutputRelations.operations.v1", Contract="Main.Plm.EngineeringOutputRelations.commands.v1",
        Schema="ENG-OUTPUT-RELATIONS/1";
    internal static byte[] Bytes(object value)=>IterationReviewCanonicalV1.Bytes(value);
    internal static string Hash(byte[] value)=>ApprovalCanonicalV2.Hash(value);
    internal static void Ref(EngineeringSourceRefV1? value,string owner)
        =>IterationCandidateValuesV1.Reference(value!,owner);
    internal static void Body(EngineeringOutputRelationsBodyV1? value)
    {
        if(value==null)throw Error("OUTPUT_RELATIONS_BODY_REQUIRED",400);
        EngineeringRevisionValuesV1.Text(value.Name); Ref(value.Manufacturing,EngineeringManufacturingValuesV1.Owner);
        if(value.Inputs==null || value.Outputs==null || value.Inputs.Length>4096 || value.Outputs.Length is <1 or >4096
            || value.Inputs.Any(x=>x==null || x.OccurrenceId==Guid.Empty) || value.Outputs.Any(x=>x==null || x.OccurrenceId==Guid.Empty)
            || value.Inputs.Select(x=>x.OccurrenceId).Distinct().Count()!=value.Inputs.Length
            || value.Outputs.Select(x=>x.OccurrenceId).Distinct().Count()!=value.Outputs.Length)throw Error("OUTPUT_RELATIONS_OCCURRENCES_INVALID",400);
        foreach(var row in value.Inputs)Ref(row.Specification,"Main.Quality.Specification.v1");
        foreach(var row in value.Outputs)
        {
            Ref(row.Specification,"Main.Quality.Specification.v1"); Ref(row.RelationDocument,ControlledDocumentValuesV1.Owner);
            if(row.NoInputDocument!=null)Ref(row.NoInputDocument,ControlledDocumentValuesV1.Owner);
        }
    }
    internal static void Policy(OutputRelationsPolicyInputV1? p)
    {
        if(p==null || p.RoleId<=0 || p.EffectiveTo<=p.EffectiveFrom)throw Error("OUTPUT_RELATIONS_POLICY_INVALID",400);
        EngineeringRevisionValuesV1.Text(p.Basis);
    }
    private const string NativeSchema="""{"type":"object","additionalProperties":false,"required":["owner","id","version","digest"],"properties":{"owner":{"type":"string"},"id":{"type":"string","format":"uuid"},"version":{"type":"string"},"digest":{"type":"string","pattern":"^[0-9a-f]{64}$"}}}""";
    private static readonly string OccurrenceSchema="{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"source\",\"occurrenceId\"],\"properties\":{\"source\":"+NativeSchema+",\"occurrenceId\":{\"type\":\"string\",\"format\":\"uuid\"}}}";
    private static readonly string EvidenceSchema="{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"manufacturing\",\"occurrenceId\",\"document\"],\"properties\":{\"manufacturing\":"+NativeSchema+",\"occurrenceId\":{\"type\":\"string\",\"format\":\"uuid\"},\"document\":"+NativeSchema+"}}";
    // Explicit local mappings of actual native references, not foreign-provider signatures.
    private static IterationReviewNativeRefV1 Native(string name,string schema,object value)
    {var raw=Bytes(value);return new(new(name,"1",Hash(Encoding.UTF8.GetBytes(schema))),Convert.ToBase64String(raw),Hash(raw),raw.Length);}
    internal static IterationReviewNativeRefV1 NativeRef(EngineeringSourceRefV1 r)=>Native("Main.Plm.OutputRelations.native-reference/1",NativeSchema,r);
    internal static IterationReviewNativeRefV1 Occurrence(EngineeringSourceRefV1 source,Guid occurrenceId)
        =>Native("Main.Plm.OutputRelations.routing-occurrence/1",OccurrenceSchema,new{source,occurrenceId});
    internal static IterationReviewNativeRefV1 Evidence(EngineeringSourceRefV1 manufacturing,Guid occurrenceId,EngineeringSourceRefV1 document)
        =>Native("Main.Plm.OutputRelations.evidence-mapping/1",EvidenceSchema,new{manufacturing,occurrenceId,document});
    internal static string InputId(Guid id)=>"mbom/"+id.ToString("D");
    internal static string OutputId(Guid id)=>"output/"+id.ToString("D");
    internal static OutputRelationsWireV1 Wire(Guid environment,Guid tenant,Guid id,string version,EngineeringManufacturingSnapshotV1 manufacturing,
        EngineeringBomSnapshotV1 mbom,EngineeringRoutingSnapshotV1 routing,EngineeringOutputRelationsBodyV1 body)
    {
        var b=manufacturing.Body;var config=b.ConfigurationRef??throw Error("OUTPUT_RELATIONS_CONFIGURATION_REQUIRED",422);
        var configurationRef=NativeRef(new(config.Owner,config.Id,config.Version,config.Digest));
        var inputs=body.Inputs.Select(p=>new OutputRelationsInputV1(InputId(p.OccurrenceId),
            NativeRef(mbom.Body.Occurrences.Single(x=>x.OccurrenceId==p.OccurrenceId).ItemRef!),NativeRef(p.Specification))).ToList();
        foreach(var output in b.Outputs.Where(x=>b.Outputs.Any(y=>y.FromOutputOccurrenceIds.Contains(x.OutputOccurrenceId))))
            inputs.Add(new(OutputId(output.OutputOccurrenceId),NativeRef(new(output.ProductRef!.Owner,output.ProductRef.Id,output.ProductRef.Version,output.ProductRef.Digest)),
                NativeRef(body.Outputs.Single(x=>x.OccurrenceId==output.OutputOccurrenceId).Specification)));
        var operations=routing.Body.Operations.Select(x=>new OutputRelationsOperationV1(x.OccurrenceId.ToString("D"),Occurrence(routing.Reference,x.OccurrenceId),configurationRef)).OrderBy(x=>x.OperationOccurrenceId,StringComparer.Ordinal).ToArray();
        var outputs=b.Outputs.Select(x=>{
            var p=body.Outputs.Single(y=>y.OccurrenceId==x.OutputOccurrenceId);var product=x.ProductRef!;
            return new OutputRelationsOutputV1(OutputId(x.OutputOccurrenceId),NativeRef(new(product.Owner,product.Id,product.Version,product.Digest)),
                NativeRef(new(product.Owner,product.Id,product.Version,product.Digest)),NativeRef(p.Specification),x.GeneratingOperationOccurrenceId!.Value.ToString("D"),configurationRef,
                x.InputOccurrenceIds.Select(InputId).Concat(x.FromOutputOccurrenceIds.Select(OutputId)).Order(StringComparer.Ordinal).ToArray(),
                Evidence(manufacturing.Reference,x.OutputOccurrenceId,p.RelationDocument),p.NoInputDocument==null?null:Evidence(manufacturing.Reference,x.OutputOccurrenceId,p.NoInputDocument));
        }).OrderBy(x=>x.OutputOccurrenceId,StringComparer.Ordinal).ToArray();
        var orderedInputs=inputs.OrderBy(x=>x.InputOccurrenceId,StringComparer.Ordinal).ToArray();var scope=new OutputRelationsScopeV1(environment.ToString("D"),tenant);
        var digest=IterationReviewCanonicalV1.Domain(Schema,new{schema=Schema,scope,relationId=id,version,configurationRef,inputs=orderedInputs,operations,outputs});
        return new(Schema,scope,id,version,configurationRef,orderedInputs,operations,outputs,digest);
    }
}
