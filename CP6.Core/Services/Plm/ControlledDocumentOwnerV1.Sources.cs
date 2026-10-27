using CP6.Core.Services.Integration;
using CP6.Core.Services.Pub;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Plm;
public sealed partial class ControlledDocumentOwnerV1
{
    private async Task<ControlledDocumentSnapshotV1> Observe(OperationsAccessV1 a,ControlledDocumentSnapshotV1 value,CancellationToken ct)
    {
        await a.RequireControlledDocument(value.Reference.Id,ct); a.AtCommit(t => a.RequireControlledDocument(value.Reference.Id,t));
        var findings = (await Sources(a,value.Body,false,ct)).ToList(); var observations = new List<DocumentContentObservationV1>();
        foreach (var content in value.Body.Contents)
        {
            var file = await files.BoundSource(a,content.File,value.Body.RevisionOrigin.Id,false,ct);
            observations.Add(new(content.OccurrenceId,file.Availability,file.FileName,file.StateVersion));
        }
        return value with { Missing=findings.Distinct().ToArray(),ContentStatus=observations.ToArray() };
    }
    private async Task<string[]> Sources(OperationsAccessV1 root,ControlledDocumentBodyV1 body,bool current,CancellationToken ct,bool requireCurrentParent=true)
    {
        var findings=ControlledDocumentValuesV1.Validate(body).ToList();
        var revisions=await root.ActionAsync(OperationsOwnerV1.EngineeringRevision,current && requireCurrentParent ? "edit" : "query",ct);
        await revisions.RequireEngineeringRevision(body.RevisionOrigin.Id,ct);revisions.AtCommit(t=>revisions.RequireEngineeringRevision(body.RevisionOrigin.Id,t));
        var parents=new EngineeringRevisionStoreV1(db);await parents.Fence(false,ct);
        // Direct exact parent read avoids recursive observation of this document through engineering composition.
        var parent=await parents.Read(body.RevisionOrigin.Id,Revision(body.RevisionOrigin.Version),ct);
        if(parent.Reference!=body.RevisionOrigin || parent.Draft.ProductRef!=body.ProductRef)throw Error("DOCUMENT_PARENT_CHANGED",412);
        if(current && (requireCurrentParent && !parent.IsCurrent || parent.State!="DRAFT"))throw Error("DOCUMENT_PARENT_CHANGED",412);
        var products=await root.SourceAsync(OperationsOwnerV1.EngineeringProduct,ct);
        await products.RequireEngineeringProduct(body.ProductRef.Id,ct);products.AtCommit(t=>products.RequireEngineeringProduct(body.ProductRef.Id,t));
        var productStore=new EngineeringProductStoreV1(db);await productStore.Fence(false,ct);
        var product=await productStore.Read(body.ProductRef.Id,Revision(body.ProductRef.Version),ct);
        if(product.Reference!=body.ProductRef)throw Error("DOCUMENT_PRODUCT_CHANGED",412);
        if(!product.IsCurrent || product.Product.Enabled!=true)
        {if(current)throw Error("DOCUMENT_PRODUCT_CHANGED",412);findings.Add("PRODUCT_SOURCE_CHANGED");}
        foreach(var content in body.Contents)
        {
            var file=await files.BoundSource(root,content.File,body.RevisionOrigin.Id,current,ct);
            if(file.Availability!="AVAILABLE")findings.Add("CONTENT_UNAVAILABLE:"+content.OccurrenceId+":"+file.Availability);
        }
        return findings.ToArray();
    }
    public async Task<FileContentDownloadV1> DownloadAsync(Guid id,string version,Guid occurrence,CancellationToken ct=default)
    {
        var selected=Revision(version);
        if(id==Guid.Empty || occurrence==Guid.Empty)throw Error("DOCUMENT_CONTENT_INVALID",400);
        var result=await Authority.ExecuteAsync<FileContentDownloadV1?>(OperationsOwnerV1.ControlledDocument,"query",true,async(a,t)=>
        {
            var store=new ControlledDocumentStoreV1(db);await store.Fence(false,t);
            var saved=await Observe(a,await store.Read(id,selected,t),t);
            var member=saved.Body.Contents.SingleOrDefault(x=>x.OccurrenceId==occurrence)??throw Error("DOCUMENT_CONTENT_NOT_FOUND",404);
            return await files.BoundDownload(a,member.File,saved.Body.RevisionOrigin.Id,t);
        },ct);
        return result??throw Error("FILE_CONTENT_INTEGRITY_UNAVAILABLE",503);
    }
}
