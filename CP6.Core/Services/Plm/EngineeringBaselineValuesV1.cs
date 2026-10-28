using System.Text;
using System.Text.Json;
using CP6.Core.Services.Oa;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
namespace CP6.Core.Services.Plm;

internal static class EngineeringBaselineValuesV1
{
    internal const string Owner="Main.Plm.EngineeringBaseline.v1",Journal="Main.Plm.EngineeringFreeze.operations.v1",
        Contract="Main.Plm.EngineeringFreeze.commands.v1",CancelJournal="Main.Plm.EngineeringFreeze.cancel.v1",
        SuccessorJournal="Main.Plm.EngineeringBaseline.successor.v1",Schema="Main.Plm.EngineeringBaseline/1";
    internal static byte[] Bytes(object value)=>IterationReviewCanonicalV1.Bytes(value);
    internal static string Hash(byte[] value)=>ApprovalCanonicalV2.Hash(value);
    internal static void Ref(EngineeringSourceRefV1? value,string owner)=>IterationCandidateValuesV1.Reference(value!,owner);
    internal static void Command(EngineeringFreezeCommandV1 c)
    {
        if(c==null || c.OperationId==Guid.Empty || c.IterationId==Guid.Empty || c.ReviewSubmissionId==Guid.Empty
            || c.ReviewRoundId==Guid.Empty || c.ApplicationReceiptId==Guid.Empty || c.ExpectedEtag==Guid.Empty)
            throw Error("ENGINEERING_FREEZE_COMMAND_INVALID",400);
        Ref(c.SourceCandidate,IterationCandidateValuesV1.Owner);Ref(c.SourceCheck,EngineeringSourceCheckValuesV1.Owner);
        foreach(var p in new[]{(c.Revision,EngineeringRevisionValuesV1.RevisionOwner),(c.EngineeringCandidate,EngineeringRevisionValuesV1.CandidateOwner),
            (c.Configuration,EngineeringRevisionValuesV1.ConfigurationOwner)})
        {if(p.Item1==null)throw Error("ENGINEERING_FREEZE_COMMAND_INVALID",400);Ref(new(p.Item1.Owner,p.Item1.Id,p.Item1.Version,p.Item1.Digest),p.Item2);}
        foreach(var digest in new[]{c.TechnicalManifestDigest,c.ReviewPackageDigest})
            if(digest==null || digest.Length!=64 || digest.Any(x=>!char.IsAsciiHexDigitLower(x)))throw Error("ENGINEERING_FREEZE_DIGEST_INVALID",400);
        EngineeringRevisionValuesV1.Text(c.Reason);
    }
    internal static EngineeringBaselineBindingV1 Binding(EngineeringSourceRefV1 baseline,EngineeringBaselineOriginalV1 original,OutputRelationsVersionV1 relation)
    {
        var wire=relation.Original.Wire;
        var baselineRef=EngineeringOutputRelationsValuesV1.NativeRef(baseline);
        var manifestRef=EngineeringOutputRelationsValuesV1.NativeRef(new("Main.Plm.EngineeringTechnicalManifest.v1",original.Command.EngineeringCandidate.Id,
            original.Command.EngineeringCandidate.Version,original.Manifest.Digest));
        const string digestSchema="{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"algorithm\",\"value\"],\"properties\":{\"algorithm\":{\"const\":\"SHA-256\"},\"value\":{\"type\":\"string\",\"pattern\":\"^[0-9a-f]{64}$\"}}}";
        var digestBytes=Bytes(new{algorithm="SHA-256",value=original.Manifest.Digest});
        var digest=new IterationReviewNativeRefV1(new("Main.Plm.EngineeringManifest.digest/1","1",Hash(Encoding.UTF8.GetBytes(digestSchema))),
            Convert.ToBase64String(digestBytes),Hash(digestBytes),digestBytes.Length);
        var configuration=original.Command.Configuration;
        var config=EngineeringOutputRelationsValuesV1.NativeRef(new(configuration.Owner,configuration.Id,configuration.Version,configuration.Digest));
        var product=original.Revision.Draft.ProductRef;
        if(original.OutputRelations!=relation.Reference || config!=wire.ConfigurationRef || product==null
            || !wire.Outputs.Any(x=>x.ProductIdentity==EngineeringOutputRelationsValuesV1.NativeRef(new(product.Owner,product.Id,product.Version,product.Digest))))
            throw Error("ENGINEERING_BASELINE_OUTPUT_BINDING_MISMATCH",422);
        var body=new{schema="ENG-BASELINE-OUTPUT-BINDING/1",scope=new OutputRelationsScopeV1(original.EnvironmentId.ToString(),original.TenantId),
            baselineRef,originalManifestRef=manifestRef,originalManifestDigest=digest,relationId=relation.Reference.Id,relationVersion=relation.Reference.Version,
            relationBodyDigest=wire.BodyDigest,freezeCommandId=original.Command.OperationId,configurationEvidence=new[]{config}};
        return new(body.schema,body.scope,baselineRef,manifestRef,digest,body.relationId,body.relationVersion,body.relationBodyDigest,body.freezeCommandId,
            body.configurationEvidence,Hash(Encoding.UTF8.GetBytes(body.schema+"\n").Concat(Bytes(body)).ToArray()));
    }
}
