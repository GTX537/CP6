using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Plm;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.ToolInstanceStoreV1;
namespace CP6.Core.Services.Wms;

// Separate wrapper: the v1/v2 command's serialized member set must never change.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ToolLinkedRegistrationCommandV1(ToolRegistrationCommandV1 Registration,ToolRegistrationLinksV1 Links);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ToolRegistrationLinksV1(BusinessPartnerIdentityRefV1? Customer=null,EngineeringSourceRefV1? Product=null,ToolLocationLinkV1? Location=null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ToolLocationLinkV1(EngineeringSourceRefV1 Location,EngineeringSourceRefV1 Warehouse);
public sealed record ToolPartyChoiceV1(EngineeringSourceRefV1 Reference,string Code,string? Name);
public sealed record ToolLocationChoiceV1(EngineeringSourceRefV1 Reference,EngineeringSourceRefV1 Warehouse,string WarehouseCd,string LocationCd,string? AreaCd);
public sealed record ToolRegistrationLinkViewV1(string CustomerState,string ProductState,string LocationState,
    BusinessPartnerIdentityRefV1? Customer,EngineeringSourceRefV1? Product,ToolLocationLinkV1? Location);
internal sealed record ToolLinkedSourceObservationV1(EngineeringSourceRefV1 Reference,string Code,string? DisplayName);
internal sealed record ToolLinkedLocationObservationV1(ToolLocationChoiceV1 Source,EngineeringSourceRefV1 Site,string? ExplicitWarehouseCd,string SiteCode);
internal sealed record ToolRegistrationLinksOriginalV1(Guid TenantId,Guid ObjectId,ToolLinkedSourceObservationV1? Customer,
    ToolLinkedSourceObservationV1? Product,ToolLinkedLocationObservationV1? Location);

// Typed SQL projection shared with the pre-pagination current-authority filter.
internal sealed class ToolRegistrationLinkRowV1
{
    public Guid ObjectId{get;set;} public Guid? CustomerId{get;set;} public Guid? ProductId{get;set;} public Guid? LocationId{get;set;}
    public Guid? WarehouseId{get;set;} public string? CustomerCd{get;set;} public string? ProductCd{get;set;}
    public string? WarehouseCd{get;set;} public string? LocationCd{get;set;}
    public string Json{get;set;}="";public string Digest{get;set;}="";
    internal static IQueryable<ToolRegistrationLinkRowV1> Query(CP6.Core.EFDbContext.CP6Context db)=>db.Database.SqlQuery<ToolRegistrationLinkRowV1>($"""
        SELECT object_id AS "ObjectId",customer_id AS "CustomerId",product_id AS "ProductId",location_id AS "LocationId",warehouse_id AS "WarehouseId",
          customer_cd AS "CustomerCd",product_cd AS "ProductCd",warehouse_cd AS "WarehouseCd",location_cd AS "LocationCd",original_json AS "Json",original_digest AS "Digest"
        FROM cp6_tool_instance_v1.registration_links WHERE tenant_id={db.CurrentTenantId}
        """);
}

public sealed partial class ToolRegistrationOwnerV1
{
    private const string LinkedContract="tool.registration.v3",WarehouseIdentity="Main.Wms.WarehouseIdentity.v1",LocationIdentity="Main.Wms.LocationIdentity.v1";
    private static EngineeringSourceRefV1 ProductRef(CommercialProductIdentityV1 value)=>new(EngineeringProductValuesV1.BusinessOwner,value.ProductId,value.Version,value.Digest);
    public Task<ToolPartyChoiceV1[]> CustomersAsync(string? search,CancellationToken ct=default)
    {
        if(search?.Length>20)throw Error("TOOL_LINK_QUERY_INVALID",400);
        return Authority.ExecuteAsync(OperationsOwnerV1.ToolInstance,"query",false,async(a,t)=>
        {
            a.RequireToolFieldRead("CustomerCd");var source=await a.SourceAsync(OperationsOwnerV1.BusinessPartner,t);
            var rows=source.BusinessPartners().Where(p=>p.CustomerFlg&&p.Status!=9);
            if(search!=null)rows=rows.Where(p=>p.BpCd.Contains(search));
            var ids=await rows.OrderBy(p=>p.BpCd).ThenBy(p=>p.Id).Take(50).Select(p=>p.Id).ToArrayAsync(t);
            var result=new List<ToolPartyChoiceV1>();foreach(var id in ids)
            {var value=await BusinessPartnerIdentityOwnerV1.ReadOwnedAsync(db,source,id,null,t);
                result.Add(new(new(value.Reference.Owner,id,value.Reference.Version,value.Reference.Digest),value.Source.GetProperty("bpCd").GetString()!,
                    a.ToolReadableFields.Contains("CustomerName")?value.Source.GetProperty("bpName").GetString():null));}
            return result.ToArray();
        },ct);
    }
    public Task<ToolPartyChoiceV1[]> ProductsAsync(string? search,CancellationToken ct=default)
    {
        if(search?.Length>20)throw Error("TOOL_LINK_QUERY_INVALID",400);
        return Authority.ExecuteAsync(OperationsOwnerV1.ToolInstance,"query",false,async(a,t)=>
        {
            a.RequireToolFieldRead("ProductCd");var source=await a.SourceAsync(OperationsOwnerV1.Product,t);
            var rows=source.Products().Where(p=>p.Status!=9);if(search!=null)rows=rows.Where(p=>p.ProductCd.Contains(search));
            var native=await rows.OrderBy(p=>p.ProductCd).ThenBy(p=>p.Id).Take(50).ToArrayAsync(t);var result=new List<ToolPartyChoiceV1>();
            foreach(var row in native){var value=await CommercialProductIdentityReader.ReadOwnedAsync(db,source,row.Id,null,t);
                result.Add(new(ProductRef(value),row.ProductCd,a.ToolReadableFields.Contains("ProductName")?row.SetProductName:null));}
            return result.ToArray();
        },ct);
    }
    private static string NativeVersion(byte[] token)=>token is {Length:8}?Convert.ToBase64String(token):throw Error("MASTER_VERSION_UNAVAILABLE",503);
    private async Task<ToolLocationChoiceV1> LocationSource(OperationsAccessV1 access,Guid id,CancellationToken ct)
    {
        var source=await access.SourceAsync(OperationsOwnerV1.Location,ct);var warehouses=await access.SourceAsync(OperationsOwnerV1.Warehouse,ct);
        var location=await (await source.MasterLocations(ct)).SingleOrDefaultAsync(x=>x.Id==id,ct)??throw Error("MASTER_NOT_FOUND",404);
        var candidates=await db.Warehouses.AsNoTracking().Where(x=>!x.IsDeleted&&x.WarehouseCd==location.WarehouseCd).Select(x=>x.Id).Take(2).ToArrayAsync(ct);
        if(candidates.Length!=1||await db.Locations.CountAsync(x=>!x.IsDeleted&&x.WarehouseCd==location.WarehouseCd&&x.LocationCd==location.LocationCd,ct)!=1)
            throw Error("MASTER_SOURCE_UNAVAILABLE",503);
        var warehouse=await warehouses.MasterWarehouses().SingleOrDefaultAsync(x=>x.Id==candidates[0],ct)??throw Error("MASTER_NOT_FOUND",404);
        // Native single-use PostgreSQL tokens advance for every UPDATE, including an ABA reversal.
        // This is an observation stored by registration, not an invented independent master-history API.
        if(!await db.Database.SqlQueryRaw<bool>("""
            SELECT count(*)=2 AS "Value" FROM pg_catalog.pg_trigger
            WHERE tgrelid IN ('public."T_Warehouse"'::regclass,'public."T_Location"'::regclass)
              AND tgname='cp6_rowversion_v1' AND tgenabled IN ('O','A') AND NOT tgisinternal
            """).SingleAsync(ct))throw Error("MASTER_VERSION_UNAVAILABLE",503);
        var wr=JsonSerializer.Serialize(new{tenantId=db.CurrentTenantId,id=warehouse.Id,warehouse.WarehouseCd,warehouse.IsDeleted});
        var lr=JsonSerializer.Serialize(new{tenantId=db.CurrentTenantId,id=location.Id,location.WarehouseCd,location.LocationCd,location.AreaCd,location.IsBlocked,location.IsDeleted});
        if(location.IsBlocked)throw Error("TOOL_LOCATION_BLOCKED",409);
        return new(new(LocationIdentity,id,NativeVersion(location.RowVersion),Hash(lr)),new(WarehouseIdentity,warehouse.Id,NativeVersion(warehouse.RowVersion),Hash(wr)),
            location.WarehouseCd,location.LocationCd,location.AreaCd);
    }
    private async Task<(string? Explicit,string Code)> SiteWarehouse(Guid site,CancellationToken ct)
    {
        var row=await db.Space_Sites.AsNoTracking().SingleAsync(x=>x.Id==site,ct);
        return(row.WarehouseCd,row.SiteCode);
    }
    public Task<ToolLocationChoiceV1[]> LocationsAsync(Guid siteId,string? search,CancellationToken ct=default)
    {
        if(siteId==Guid.Empty||search?.Length>30)throw Error("TOOL_LINK_QUERY_INVALID",400);
        return Authority.ExecuteAsync(OperationsOwnerV1.ToolInstance,"query",false,async(a,t)=>
        {
            var site=await a.SourceAsync(OperationsOwnerV1.PhysicalSite,t);var current=await CP6.Core.Services.Space.PhysicalSiteIdentityReaderV1.ReadOwnedAsync(db,site,siteId,t);
            if(!current.Enabled)throw Error("TOOL_SITE_DISABLED",409);
            var mapping=await SiteWarehouse(siteId,t);var code=string.IsNullOrEmpty(mapping.Explicit)?mapping.Code:mapping.Explicit;
            var source=await a.SourceAsync(OperationsOwnerV1.Location,t);var rows=(await source.MasterLocations(t)).Where(x=>x.WarehouseCd==code&&!x.IsBlocked);
            if(search!=null)rows=rows.Where(x=>x.LocationCd.Contains(search));
            var ids=await rows.OrderBy(x=>x.LocationCd).ThenBy(x=>x.Id).Take(50).Select(x=>x.Id).ToArrayAsync(t);
            var result=new List<ToolLocationChoiceV1>();foreach(var id in ids)result.Add(await LocationSource(a,id,t));return result.ToArray();
        },ct);
    }
    private async Task<ToolRegistrationLinksOriginalV1> ResolveLinks(OperationsAccessV1 a,ToolRegistrationCommandV1 command,ToolRegistrationLinksV1 links,CancellationToken ct)
    {
        ToolLinkedSourceObservationV1? customer=null,product=null;ToolLinkedLocationObservationV1? location=null;
        if(links.Customer is {} expected)
        {
            a.RequireToolFieldWrite("CustomerCd");var source=await a.SourceAsync(OperationsOwnerV1.BusinessPartner,ct);
            var value=await BusinessPartnerIdentityOwnerV1.HoldExactAsync(db,source,expected,ct);
            if(!value.Source.GetProperty("customerFlg").GetBoolean()||value.Source.GetProperty("isDeleted").GetBoolean()||value.Source.GetProperty("status").GetInt32()==9)
                throw Error("TOOL_CUSTOMER_UNAVAILABLE",409);
            var name=value.Source.GetProperty("bpName").GetString();if(name!=null&&command.Kind=="CREATE")a.RequireToolFieldWrite("CustomerName");
            customer=new(new(expected.Owner,expected.Id,expected.Version,expected.Digest),value.Source.GetProperty("bpCd").GetString()!,name);
        }
        if(links.Product is {} p)
        {
            a.RequireToolFieldWrite("ProductCd");if(p.Owner!=EngineeringProductValuesV1.BusinessOwner||p.Id==Guid.Empty)throw Error("TOOL_PRODUCT_REF_INVALID",400);
            var source=await a.SourceAsync(OperationsOwnerV1.Product,ct);var value=await CommercialProductIdentityReader.ReadOwnedAsync(db,source,p.Id,null,ct);
            if(ProductRef(value)!=p)throw Error("TOOL_PRODUCT_CHANGED",412);
            if(value.Source.GetProperty("isDeleted").GetBoolean()||value.Source.GetProperty("status").GetInt32()==9)throw Error("TOOL_PRODUCT_UNAVAILABLE",409);
            var row=await db.ProductMasters.AsNoTracking().SingleAsync(x=>x.Id==p.Id,ct);
            if(row.SetProductName!=null&&command.Kind=="CREATE")a.RequireToolFieldWrite("ProductName");
            // DisplayName is an explicit native display observation at this version, not a field in the older identity payload.
            product=new(p,row.ProductCd,row.SetProductName);
            a.AtCommit(async t=>{if(ProductRef(await CommercialProductIdentityReader.ReadOwnedAsync(db,source,p.Id,null,t))!=p)throw Error("TOOL_PRODUCT_CHANGED",412);});
        }
        if(links.Location is {} l)
        {
            if(l.Location==null||l.Warehouse==null)throw Error("TOOL_LOCATION_REF_INVALID",400);
            var current=await LocationSource(a,l.Location.Id,ct);
            if(current.Reference!=l.Location||current.Warehouse!=l.Warehouse)throw Error("TOOL_LOCATION_CHANGED",412);
            var mapping=await SiteWarehouse(command.Site.Id,ct);var code=string.IsNullOrEmpty(mapping.Explicit)?mapping.Code:mapping.Explicit;
            if(code!=current.WarehouseCd)throw Error("TOOL_SITE_WAREHOUSE_MISMATCH",409);
            location=new(current,command.Site,mapping.Explicit,mapping.Code);
            a.AtCommit(async t=>{if(await LocationSource(a,l.Location.Id,t)!=current||await SiteWarehouse(command.Site.Id,t)!=mapping)throw Error("TOOL_LOCATION_CHANGED",412);});
        }
        return new(db.CurrentTenantId,command.ObjectId,customer,product,location);
    }
    private static void ApplyLinks(PlateMoldStock row,ToolRegistrationLinksOriginalV1 links)
    {row.CustomerCd=links.Customer?.Code;row.CustomerName=links.Customer?.DisplayName;row.ProductCd=links.Product?.Code;row.ProductName=links.Product?.DisplayName;
        row.WarehouseCd=links.Location?.Source.WarehouseCd;row.LocationCd=links.Location?.Source.LocationCd;}
    private async Task MatchLegacyLinks(Guid id,ToolRegistrationLinksOriginalV1 links,CancellationToken ct)
    {
        var row=await db.PlateMoldStocks.AsNoTracking().SingleAsync(x=>x.Id==id,ct);
        if(links.Customer!=null&&links.Customer.Code!=row.CustomerCd||links.Product!=null&&links.Product.Code!=row.ProductCd
            ||links.Location!=null&&(links.Location.Source.WarehouseCd!=row.WarehouseCd||links.Location.Source.LocationCd!=row.LocationCd))
            throw Error("TOOL_LEGACY_LINK_MISMATCH",409);
    }
    private async Task StoreLinks(ToolRegistrationLinksOriginalV1 links,string json,string digest,CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_tool_instance_v1.registration_links(tenant_id,object_id,customer_id,product_id,location_id,warehouse_id,
              customer_cd,product_cd,warehouse_cd,location_cd,original_json,original_digest)
            VALUES({links.TenantId},{links.ObjectId},{links.Customer?.Reference.Id},{links.Product?.Reference.Id},
              {links.Location?.Source.Reference.Id},{links.Location?.Source.Warehouse.Id},
              {links.Customer?.Code},{links.Product?.Code},
              {links.Location?.Source.WarehouseCd},{links.Location?.Source.LocationCd},{json},{digest})
            """,ct);
    }
    private async Task<ToolRegistrationLinkViewV1> ProjectLinks(OperationsAccessV1 a,Guid id,JsonElement native,CancellationToken ct)
    {
        var row=await ToolRegistrationLinkRowV1.Query(db).SingleOrDefaultAsync(x=>x.ObjectId==id,ct);
        var value=row==null?null:JsonSerializer.Deserialize<ToolRegistrationLinksOriginalV1>(row.Json);
        string State(object? link,string field)=>link!=null?"LINKED":native.GetProperty(field).ValueKind==JsonValueKind.String?"LEGACY_UNMAPPED":"UNLINKED";
        var c=value?.Customer?.Reference;var p=value?.Product?.Reference;
        return new(a.ToolReadableFields.Contains("CustomerCd")?State(c,"customerCd"):"HIDDEN",a.ToolReadableFields.Contains("ProductCd")?State(p,"productCd"):"HIDDEN",
            State(value?.Location,"locationCd"),c!=null&&a.ToolReadableFields.Contains("CustomerCd")?new(c.Owner,c.Id,c.Version,c.Digest):null,
            a.ToolReadableFields.Contains("ProductCd")?p:null,value?.Location is {} l?new(l.Source.Reference,l.Source.Warehouse):null);
    }
}
