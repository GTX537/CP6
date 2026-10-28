using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Space;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
using static CP6.Core.Services.Mes.QualitySpecificationValuesV1;
using static CP6.Core.Services.Mes.QualitySpecificationStoreV1;

namespace CP6.Core.Services.Mes;
public sealed partial class QualitySpecificationAuthorityService
{
    private async Task<string[]> Sources(OperationsAccessV1 root,QualitySpecificationBodyV1 b,bool requireQualified,CancellationToken ct)
    {
        Body(b);var missing=new List<string>();
        var productAccess=await root.SourceAsync(OperationsOwnerV1.EngineeringProduct,ct);
        await productAccess.RequireEngineeringProduct(b.Product.Id,ct);productAccess.AtCommit(t=>productAccess.RequireEngineeringProduct(b.Product.Id,t));
        var products=new EngineeringProductStoreV1(db);await products.Fence(false,ct);
        var product=await products.Read(b.Product.Id,Revision(b.Product.Version),ct);
        if(product.Reference!=b.Product)throw Error("QUALITY_SPEC_PRODUCT_REFERENCE_CHANGED",412);
        if(!product.IsCurrent || product.Product.Enabled!=true)missing.Add("PRODUCT_SOURCE_CHANGED");
        if(requireQualified)productAccess.AtCommit(async t=>{
            var current=await products.Read(b.Product.Id,null,t);
            if(current.Reference!=b.Product || current.Product.Enabled!=true)throw Error("QUALITY_SPEC_PRODUCT_REFERENCE_CHANGED",412);
        });
        var siteAccess=await root.SourceAsync(OperationsOwnerV1.PhysicalSite,ct);
        var site=await PhysicalSiteIdentityReaderV1.ReadOwnedAsync(db,siteAccess,b.Site.Id,ct);
        if(site.Reference!=b.Site || !site.Enabled)missing.Add("SITE_SOURCE_CHANGED");
        if(requireQualified)siteAccess.AtCommit(async t=>{
            var current=await PhysicalSiteIdentityReaderV1.ReadOwnedAsync(db,siteAccess,b.Site.Id,t);
            if(current.Reference!=b.Site || !current.Enabled)throw Error("QUALITY_SPEC_SITE_REFERENCE_CHANGED",412);
        });
        foreach(var unit in b.Requirements.Select(x=>x.Unit).OfType<UomProfileRefV1>().Distinct())
        {
            var a=await root.SourceAsync(OperationsOwnerV1.UomProfile,ct);await a.RequireUomProfile(unit.Id,ct);a.AtCommit(t=>a.RequireUomProfile(unit.Id,t));
            var units=new UomProfileStoreV1(db);await units.Fence(false,ct);var value=await units.Read(unit.Id,Revision(unit.Version),ct);
            if(value.Reference!=unit)throw Error("QUALITY_SPEC_UNIT_REFERENCE_CHANGED",412);
            if(!value.IsCurrent || value.Profile.Enabled!=true)missing.Add("UNIT_SOURCE_CHANGED:"+unit.Id);
            if(requireQualified)a.AtCommit(async t=>{
                var current=await units.Read(unit.Id,null,t);
                if(current.Reference!=unit || current.Profile.Enabled!=true)throw Error("QUALITY_SPEC_UNIT_REFERENCE_CHANGED",412);
            });
        }
        var docs=await root.SourceAsync(OperationsOwnerV1.ControlledDocument,ct);
        foreach(var reference in Documents(b))
        {
            var document=await documents.ReviewSource(docs,reference,requireQualified,ct);
            if(document.State!="APPROVED")missing.Add("DOCUMENT_APPROVAL_REQUIRED:"+reference.Id);
            missing.AddRange(document.Missing!.Select(x=>"DOCUMENT:"+reference.Id+":"+x));
        }
        foreach(var requirement in b.Requirements)
            if(requirement.Applicability=="UNKNOWN")missing.Add("QUALITY_REQUIREMENT_APPLICABILITY_UNKNOWN:"+requirement.Id);
        if(requireQualified && missing.Count>0)throw Error("QUALITY_SPEC_SOURCE_INCOMPLETE",422);
        return missing.Distinct(StringComparer.Ordinal).ToArray();
    }
    /// <summary>Same native connection and transaction. This is the exact source participant;
    /// it neither starts a nested transaction nor creates an HTTP final-use token.</summary>
    internal async Task<QualitySpecificationSourceProofV1> Source(OperationsAccessV1 root,EngineeringSourceRefV1 reference,
        EngineeringProductRefV1? product,bool hold,CancellationToken ct)
    {
        Ref(reference);var a=await root.SourceAsync(OperationsOwnerV1.QualitySpecification,ct);
        var s=await Store(a,false,ct);var v=await View(a,s,reference.Id,Revision(reference.Version),ct);
        if(v.Version.Reference!=reference)throw Error("QUALITY_SPEC_REFERENCE_CHANGED",412);
        var missing=v.Missing.ToList();
        if(product!=null && v.Version.Body.Product!=product)missing.Add("QUALITY_SPEC_PRODUCT_MISMATCH");
        if(v.CurrentApproval==null)missing.Add(v.Control.State=="WITHDRAWN"?"QUALITY_SPEC_WITHDRAWN":"QUALITY_SPEC_APPROVAL_REQUIRED");
        else if(v.CurrentApproval.Body.Specification!=reference)missing.Add("QUALITY_SPEC_CURRENT_VERSION_CHANGED");
        var digest=Hash(Bytes(v.Version.Body.Requirements));
        if(v.CurrentApproval?.Body.Specification==reference && v.CurrentApproval.Body.RequirementsDigest!=digest)throw Error("QUALITY_SPEC_APPROVAL_UNAVAILABLE",503);
        if(hold)
        {
            if(missing.Count>0)throw Error("QUALITY_SPEC_SOURCE_UNAVAILABLE",422);
            await Sources(a,v.Version.Body,true,ct);
            a.AtCommit(async t=>{
                // Do not re-enter View/Sources here: they register new final callbacks.
                var current=await s.Control(reference.Id,t);
                if(current.Reference!=v.ControlReference || current.Body.ApprovalId!=v.Control.ApprovalId)throw Error("QUALITY_SPEC_CONTROL_CHANGED",412);
            });
        }
        return new(reference,v.ControlReference,v.CurrentApproval?.Body.Specification==reference?v.CurrentApproval.Reference:null,digest,
            missing.Count==0?"AVAILABLE":"UNAVAILABLE",missing.Distinct(StringComparer.Ordinal).ToArray(),v.Version.Body.Requirements,
            hold?"HELD_IN_CURRENT_UOW":"READ_ONLY_OBSERVATION");
    }
    internal Task<QualitySpecificationSourceProofV1> HoldExact(OperationsAccessV1 root,EngineeringSourceRefV1 reference,
        EngineeringSourceRefV1 expectedControl,EngineeringProductRefV1 product,CancellationToken ct)
        =>Hold(root,reference,expectedControl,product,ct);
    private async Task<QualitySpecificationSourceProofV1> Hold(OperationsAccessV1 root,EngineeringSourceRefV1 reference,
        EngineeringSourceRefV1 expectedControl,EngineeringProductRefV1 product,CancellationToken ct)
    {
        Ref(expectedControl,ControlOwner);var proof=await Source(root,reference,product,true,ct);
        if(proof.Control!=expectedControl)throw Error("QUALITY_SPEC_CONTROL_CHANGED",412);return proof;
    }
    public Task<QualitySpecificationSourceProofV1> SourceAsync(EngineeringSourceRefV1 reference,CancellationToken ct=default)
        =>Authority.ExecuteAsync(OperationsOwnerV1.QualitySpecification,"query",false,(a,t)=>Source(a,reference,null,false,t),ct);
}
