using System.Globalization;
using System.Numerics;
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.StockKernel.StockDirectMoveValuesV1;
namespace CP6.Core.Services.Wms.StockKernel;

// Assembly-internal participant: the authenticated parent owns this same connection/transaction.
// Opening data is explicitly simulated. MOVE, capacity, original result and the parent's links are real writes.
internal sealed class StockDirectMoveV1(CP6Context db,Guid environment)
{
    private Guid Tenant=>db.CurrentTenantId;
    internal async Task Fence(bool write,CancellationToken ct)
    {
        if(db.Database.CurrentTransaction==null)throw Error("STOCK_QUANTITY_TRANSACTION_REQUIRED",503);
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE public.\"T_Stock\",cp6_stock_move_v1.openings,cp6_stock_move_v1.roots,cp6_stock_move_v1.capacities,cp6_stock_move_v1.decisions IN "+(write?"SHARE ROW EXCLUSIVE":"SHARE")+" MODE",ct);
    }
    private static void ValidateOpening(StockOpeningV1 o)
    {
        if(o==null||o.DataKind!="SIMULATED"||o.Locations==null||o.Roots==null||o.Claims==null||o.Capacities==null||o.Products==null)throw Error("STOCK_SIMULATED_OPENING_REQUIRED",400);
        ValidateRef(o.Ref);
        if(o.Roots.Length is <1 or >500||o.Locations.Length is <2 or >500||o.Roots.Sum(x=>x.Slices?.Length??0)>2000)throw Error("STOCK_OPENING_SIZE_INVALID",413);
        if(o.Locations.Select(x=>Key(x.Ref)).Distinct().Count()!=o.Locations.Length||o.Roots.Select(x=>Key(x.Ref)).Distinct().Count()!=o.Roots.Length||o.Capacities.Select(x=>Key(x.DomainRef)).Distinct().Count()!=o.Capacities.Length||o.Products.Select(x=>Key(x.Ref)).Distinct().Count()!=o.Products.Length)throw Error("STOCK_OPENING_DUPLICATE",400);
        foreach(var p in o.Products){ValidateRef(p.Ref);if(string.IsNullOrWhiteSpace(p.NativeProductCode))throw Error("STOCK_PRODUCT_MAPPING_REQUIRED",400);}
        foreach(var l in o.Locations)
        {
            ValidateRef(l.Ref);
            if(l.NativeLocationId==Guid.Empty||string.IsNullOrWhiteSpace(l.WarehouseCode)||string.IsNullOrWhiteSpace(l.LocationCode)||l.ProductCodes==null||l.ProductCodes.Length==0||l.CapacityDomainRefs==null||l.CapacityDomainRefs.Length==0)throw Error("STOCK_LOCATION_MAPPING_REQUIRED",400);
            if(l.ProductCodes.Distinct(StringComparer.Ordinal).Count()!=l.ProductCodes.Length||l.CapacityDomainRefs.Distinct().Count()!=l.CapacityDomainRefs.Length)throw Error("STOCK_OPENING_DUPLICATE",400);
            foreach(var code in l.ProductCodes)if(!o.Products.Any(p=>p.NativeProductCode==code))throw Error("STOCK_PRODUCT_MAPPING_REQUIRED",400);
            foreach(var domain in l.CapacityDomainRefs)if(!o.Capacities.Any(c=>c.DomainRef==domain))throw Error("STOCK_CAPACITY_DOMAIN_REQUIRED",400);
        }
        foreach(var c in o.Capacities)
        {
            ValidateRef(c.DomainRef);ValidateRef(c.MetricRef);ValidateRef(c.UomRef);Number(c.OtherOccupancy,total:true);
            if(c.Mode is not ("LIMITED" or "UNLIMITED" or "NOT_APPLICABLE" or "UNKNOWN")||c.Mode=="LIMITED"&&c.Limit==null||c.Mode!="LIMITED"&&c.Limit!=null)throw Error("STOCK_CAPACITY_MODE_INVALID",400);
            if(c.Limit!=null)Number(c.Limit,total:true);
        }
        foreach(var r in o.Roots)
        {
            ValidateRef(r.Ref);ValidateRef(r.SourceRef);ValidateRef(r.EligibilityRef);
            if(r.Dimension==null||r.Slices==null||r.Rates==null)throw Error("STOCK_ROOT_INPUT_REQUIRED",400);
            foreach(var reference in new[]{r.Dimension.ProductRef,r.Dimension.TechnicalRef,r.Dimension.OwnerRef,r.Dimension.BaseUomRef,r.Dimension.OriginPurposeRef,r.Dimension.OriginSiteRef})ValidateRef(reference);
            var amount=Number(r.Quantity,positive:true);if(r.Serial&&amount!=100000000)throw Error("STOCK_SERIAL_QUANTITY_INVALID",400);
            var product=o.Products.SingleOrDefault(p=>p.Ref==r.Dimension.ProductRef)??throw Error("STOCK_PRODUCT_MAPPING_REQUIRED",400);
            BigInteger end=0;
            foreach(var s in r.Slices.OrderBy(x=>Number(x.From)))
            {
                if(Number(s.From)!=end||Number(s.To)<=end)throw Error("STOCK_OPENING_PARTITION_INVALID",400);
                var l=o.Locations.SingleOrDefault(x=>x.Ref==s.LocationRef)??throw Error("STOCK_LOCATION_MAPPING_REQUIRED",400);
                if(!l.ProductCodes.Contains(product.NativeProductCode,StringComparer.Ordinal))throw Error("STOCK_PRODUCT_MAPPING_REQUIRED",400);end=Number(s.To);
            }
            if(end!=amount)throw Error("STOCK_OPENING_PARTITION_INVALID",400);
            foreach(var rate in r.Rates){ValidateRef(rate.MetricRef);ValidateRef(rate.UomRef);Number(rate.Numerator);Number(rate.Denominator,positive:true);}
            if(r.Rates.Select(x=>new{x.MetricRef,x.UomRef}).Distinct().Count()!=r.Rates.Length)throw Error("STOCK_CAPACITY_RATE_DUPLICATE",400);
        }
        if(o.Claims.Select(x=>Key(x.BindingRef)).Distinct().Count()!=o.Claims.Length)throw Error("STOCK_CLAIM_DUPLICATE",400);
        foreach(var c in o.Claims)
        {
            ValidateRef(c.ClaimRef);ValidateRef(c.BindingRef);ValidateRef(c.UseControlRef);
            var r=o.Roots.SingleOrDefault(x=>x.Ref==c.Span?.RootRef)??throw Error("STOCK_CLAIM_ROOT_REQUIRED",400);
            if(Amount(c.Span)<=0||Number(c.Span.To)>Number(r.Quantity)||c.State is not ("RESERVED" or "ALLOCATED" or "PICKED"))throw Error("STOCK_CLAIM_RANGE_INVALID",400);
            if(o.Claims.Any(other=>!ReferenceEquals(other,c)&&Overlap(other.Span,c.Span)))throw Error("STOCK_CLAIM_OVERLAP",400);
        }
    }
    internal async Task OpenSimulation(StockOpeningV1 o,CancellationToken ct)
    {
        ValidateOpening(o);await Fence(true,ct);var id=Key(o.Ref);var bytes=Bytes(o);
        var old=await db.Database.SqlQuery<byte[]>($"SELECT body AS \"Value\" FROM cp6_stock_move_v1.openings WHERE tenant_id={Tenant} AND environment_id={environment} AND id={id}").SingleOrDefaultAsync(ct);
        if(old!=null){if(!old.AsSpan().SequenceEqual(bytes))throw Error("STOCK_OPENING_CONFLICT");return;}
        foreach(var l in o.Locations)
        {
            if(!await db.Locations.AnyAsync(x=>x.Id==l.NativeLocationId&&x.WarehouseCd==l.WarehouseCode&&x.LocationCd==l.LocationCode&&!x.IsDeleted,ct))throw Error("STOCK_LOCATION_MAPPING_INVALID",422);
            // This is a new simulated opening, never an implicit migration or remapping of existing balances.
            if(await db.Stocks.AnyAsync(x=>x.WarehouseCd==l.WarehouseCode&&x.LocationCd==l.LocationCode&&l.ProductCodes.Contains(x.ProductCd),ct))throw Error("STOCK_LEGACY_MAPPING_REQUIRED",422);
        }
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_stock_move_v1.openings VALUES({Tenant},{environment},{id},{bytes})",ct);
        foreach(var l in o.Locations)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_stock_move_v1.locations VALUES({Tenant},{environment},{Key(l.Ref)},{l.NativeLocationId},{id},{Bytes(l)})",ct);
            foreach(var code in l.ProductCodes)await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_stock_move_v1.writer_bindings VALUES({Tenant},{environment},{Key(l.Ref)},{l.WarehouseCode},{l.LocationCode},{code})",ct);
        }
        foreach(var r in o.Roots)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_stock_move_v1.roots VALUES({Tenant},{environment},{Key(r.Ref)},{id},{r.Quantity}::numeric,{Bytes(r)})",ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_stock_move_v1.root_heads VALUES({Tenant},{environment},{Key(r.Ref)},1)",ct);
            foreach(var slice in r.Slices)await InsertSlice(r.Ref,slice,ct);
        }
        foreach(var c in o.Claims)await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_stock_move_v1.claim_bindings VALUES({Tenant},{environment},{Key(c.BindingRef)},{Key(c.Span.RootRef)},{c.Span.From}::numeric,{c.Span.To}::numeric,{Bytes(c)})",ct);
        foreach(var c in o.Capacities)
        {
            var occupied=Number(c.OtherOccupancy,total:true);
            foreach(var r in o.Roots)foreach(var slice in r.Slices)
                if(o.Locations.Single(x=>x.Ref==slice.LocationRef).CapacityDomainRefs.Contains(c.DomainRef))occupied+=Measure(r,c,Number(slice.To)-Number(slice.From));
            if(c.Mode=="LIMITED"&&occupied>Number(c.Limit,total:true))throw Error("STOCK_CAPACITY_INSUFFICIENT");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_stock_move_v1.capacities VALUES({Tenant},{environment},{Key(c.DomainRef)},{Bytes(c)},{Text(occupied)}::numeric,1)",ct);
        }
    }
    private Task<int> InsertSlice(StockRefV1 root,StockSliceV1 s,CancellationToken ct)=>db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_stock_move_v1.slices VALUES({Tenant},{environment},{Key(root)},{s.From}::numeric,{s.To}::numeric,{Key(s.LocationRef)})",ct);
    private sealed record SliceRow(string From,string To,byte[] Body);
    internal async Task<StockRootCurrentV1[]> Snapshot(CancellationToken ct)
    {
        var bodies=await db.Database.SqlQuery<byte[]>($"SELECT body AS \"Value\" FROM cp6_stock_move_v1.roots WHERE tenant_id={Tenant} AND environment_id={environment} ORDER BY id").ToArrayAsync(ct);
        var result=new List<StockRootCurrentV1>();foreach(var body in bodies)result.Add(await Root(Decode<StockOpeningRootV1>(body).Ref,ct));return result.ToArray();
    }
    internal async Task<(StockCapacityV1 Capacity,string Occupied)> CapacitySnapshot(StockRefV1 domain,CancellationToken ct)
    {var row=await Capacity(domain,ct);return(Decode<StockCapacityV1>(row.Body),row.Occupied);}
    internal async Task<StockRootCurrentV1> Root(StockRefV1 reference,CancellationToken ct)
    {
        var key=Key(reference);
        var body=await db.Database.SqlQuery<byte[]>($"SELECT body AS \"Value\" FROM cp6_stock_move_v1.roots WHERE tenant_id={Tenant} AND environment_id={environment} AND id={key}").SingleOrDefaultAsync(ct)??throw Error("STOCK_ROOT_UNAVAILABLE",422);
        var root=Decode<StockOpeningRootV1>(body);if(root.Ref!=reference)throw Error("STOCK_ROOT_REFERENCE_STALE",412);
        var slices=await db.Database.SqlQuery<SliceRow>($"""
            SELECT s.from_qty::text AS "From",s.to_qty::text AS "To",l.body AS "Body" FROM cp6_stock_move_v1.slices s
            JOIN cp6_stock_move_v1.locations l ON l.tenant_id=s.tenant_id AND l.environment_id=s.environment_id AND l.id=s.location_id
            WHERE s.tenant_id={Tenant} AND s.environment_id={environment} AND s.root_id={key} ORDER BY s.from_qty
            """).ToArrayAsync(ct);
        var revision=await db.Database.SqlQuery<long>($"SELECT revision AS \"Value\" FROM cp6_stock_move_v1.root_heads WHERE tenant_id={Tenant} AND environment_id={environment} AND root_id={key}").SingleAsync(ct);
        return new(root,revision,slices.Select(x=>new StockSliceV1(x.From,x.To,Decode<StockLocationV1>(x.Body).Ref)).ToArray());
    }
    internal async Task<StockLocationV1> Location(StockRefV1 reference,CancellationToken ct)
    {
        var body=await db.Database.SqlQuery<byte[]>($"SELECT body AS \"Value\" FROM cp6_stock_move_v1.locations WHERE tenant_id={Tenant} AND environment_id={environment} AND id={Key(reference)}").SingleOrDefaultAsync(ct)??throw Error("STOCK_LOCATION_UNAVAILABLE",422);
        var location=Decode<StockLocationV1>(body);if(location.Ref!=reference)throw Error("STOCK_LOCATION_REFERENCE_STALE",412);return location;
    }
    internal async Task<StockClaimBindingV1[]> Claims(StockSpanV1 span,CancellationToken ct)
    {
        var rows=await db.Database.SqlQuery<byte[]>($"SELECT body AS \"Value\" FROM cp6_stock_move_v1.claim_bindings WHERE tenant_id={Tenant} AND environment_id={environment} AND root_id={Key(span.RootRef)} AND from_qty<{span.To}::numeric AND to_qty>{span.From}::numeric").ToArrayAsync(ct);
        return rows.Select(Decode<StockClaimBindingV1>).ToArray();
    }
    private sealed record CapacityRow(byte[] Body,string Occupied);
    private static BigInteger Measure(StockOpeningRootV1 root,StockCapacityV1 capacity,BigInteger amount)
    {
        if(capacity.Mode=="NOT_APPLICABLE")return 0;
        if(capacity.Mode=="UNKNOWN")throw Error("STOCK_CAPACITY_UNKNOWN");
        var rate=root.Rates.SingleOrDefault(x=>x.MetricRef==capacity.MetricRef&&x.UomRef==capacity.UomRef)??throw Error("STOCK_CAPACITY_CONVERSION_UNKNOWN");
        var value=BigInteger.DivRem(amount*Number(rate.Numerator),Number(rate.Denominator,positive:true),out var remainder);
        if(remainder!=0)throw Error("STOCK_CAPACITY_PRECISION_INVALID",422);return value;
    }
    private sealed record CheckedMove(StockRootCurrentV1[] Roots,StockClaimBindingV1[] Claims,Dictionary<string,BigInteger> CapacityDeltas);
    internal async Task Check(StockMoveInputV1 input,CancellationToken ct)=>_ = await CheckMove(input,ct);
    private async Task<CheckedMove> CheckMove(StockMoveInputV1 input,CancellationToken ct)
    {
        if(input.SourceLocationRef==input.TargetLocationRef||input.Spans.Length is <1 or >2000)throw Error("STOCK_MOVE_RANGE_INVALID",400);
        var source=await Location(input.SourceLocationRef,ct);var target=await Location(input.TargetLocationRef,ct);
        var locationIds=new[]{source.NativeLocationId,target.NativeLocationId};
        if(await db.Locations.CountAsync(x=>locationIds.Contains(x.Id)&&!x.IsDeleted&&!x.IsBlocked&&!x.IsInitializationQuarantined,ct)!=2)throw Error("STOCK_LOCATION_USE_FORBIDDEN",422);
        var roots=new List<StockRootCurrentV1>();var claims=new List<StockClaimBindingV1>();var deltas=new Dictionary<string,BigInteger>(StringComparer.Ordinal);
        foreach(var group in input.Spans.GroupBy(x=>Key(x.RootRef)))
        {
            var current=await Root(group.First().RootRef,ct);var root=current.Root;roots.Add(current);
            if(!root.Movable)throw Error("STOCK_ROOT_USE_FORBIDDEN",422);
            var openingBody=await db.Database.SqlQuery<byte[]>($"""
                SELECT o.body AS "Value" FROM cp6_stock_move_v1.openings o JOIN cp6_stock_move_v1.roots r
                ON o.tenant_id=r.tenant_id AND o.environment_id=r.environment_id AND o.id=r.opening_id
                WHERE r.tenant_id={Tenant} AND r.environment_id={environment} AND r.id={Key(root.Ref)}
                """).SingleAsync(ct);
            var code=Decode<StockOpeningV1>(openingBody).Products.Single(x=>x.Ref==root.Dimension.ProductRef).NativeProductCode;
            if(!target.ProductCodes.Contains(code,StringComparer.Ordinal))throw Error("STOCK_TARGET_WRITER_NOT_BOUND",422);
            BigInteger previous=-1;
            foreach(var span in group.OrderBy(x=>Number(x.From)))
            {
                var start=Number(span.From);var end=Number(span.To);var quantity=end-start;
                if(span.RootRef!=root.Ref||start<previous||quantity<=0||end>Number(root.Quantity))throw Error("STOCK_MOVE_RANGE_INVALID",400);previous=end;
                if(root.Serial&&(start!=0||quantity!=100000000))throw Error("STOCK_SERIAL_SPLIT_FORBIDDEN",422);
                var covered=current.Slices.Where(x=>Number(x.From)<end&&start<Number(x.To)).ToArray();
                if(covered.Any(x=>x.LocationRef!=source.Ref)||covered.Aggregate(BigInteger.Zero,(n,x)=>n+BigInteger.Min(end,Number(x.To))-BigInteger.Max(start,Number(x.From)))!=quantity)throw Error("STOCK_SOURCE_POSITION_CHANGED",412);
                var bindings=await Claims(span,ct);if(bindings.Any(x=>!x.MoveAllowed))throw Error("STOCK_CLAIM_USE_FORBIDDEN",422);claims.AddRange(bindings);
                foreach(var (location,sign) in new[]{(source,-1),(target,1)})foreach(var domain in location.CapacityDomainRefs)
                {
                    var row=await Capacity(domain,ct);var c=Decode<StockCapacityV1>(row.Body);
                    var key=Key(domain);deltas[key]=deltas.GetValueOrDefault(key)+sign*Measure(root,c,quantity);
                }
            }
        }
        if(roots.Select(x=>x.Root.Dimension).Distinct().Count()!=1)throw Error("STOCK_MOVE_DIMENSION_MISMATCH",422);
        foreach(var (key,delta) in deltas)
        {
            var row=await CapacityKey(key,ct);var c=Decode<StockCapacityV1>(row.Body);var next=Number(row.Occupied,total:true)+delta;
            if(next<0||c.Mode=="LIMITED"&&next>Number(c.Limit,total:true))throw Error("STOCK_CAPACITY_INSUFFICIENT");
        }
        return new(roots.ToArray(),claims.DistinctBy(x=>Key(x.BindingRef)).ToArray(),deltas);
    }
    private Task<CapacityRow> CapacityKey(string key,CancellationToken ct)=>db.Database.SqlQuery<CapacityRow>($"SELECT body AS \"Body\",occupied::text AS \"Occupied\" FROM cp6_stock_move_v1.capacities WHERE tenant_id={Tenant} AND environment_id={environment} AND id={key}").SingleAsync(ct);
    private async Task<CapacityRow> Capacity(StockRefV1 domain,CancellationToken ct)
    {var row=await CapacityKey(Key(domain),ct);if(Decode<StockCapacityV1>(row.Body).DomainRef!=domain)throw Error("STOCK_CAPACITY_REFERENCE_STALE",412);return row;}
    private sealed record Decision(string Digest,byte[] Body);
    internal async Task<StockMoveReceiptV1?> Original(StockMoveInputV1 input,CancellationToken ct)
    {
        var row=await db.Database.SqlQuery<Decision>($"SELECT input_digest AS \"Digest\",body AS \"Body\" FROM cp6_stock_move_v1.decisions WHERE tenant_id={Tenant} AND environment_id={environment} AND canonical={input.CanonicalKey}").SingleOrDefaultAsync(ct);
        if(row==null)return null;if(row.Digest!=Hash(input))throw Error("STOCK_CANONICAL_CONFLICT");return Decode<StockMoveReceiptV1>(row.Body);
    }
    internal async Task<StockMoveReceiptV1> Move(StockMoveInputV1 input,CancellationToken ct)
    {
        await Fence(true,ct);var prior=await Original(input,ct);if(prior!=null)return prior;
        var check=await CheckMove(input,ct);
        foreach(var current in check.Roots)
        {
            var slices=current.Slices.ToList();
            foreach(var span in input.Spans.Where(x=>x.RootRef==current.Root.Ref))
            {
                var start=Number(span.From);var end=Number(span.To);var replacement=new List<StockSliceV1>();
                foreach(var slice in slices)
                {
                    var lo=Number(slice.From);var hi=Number(slice.To);
                    if(hi<=start||lo>=end){replacement.Add(slice);continue;}
                    if(lo<start)replacement.Add(slice with{To=Text(start)});
                    replacement.Add(new(Text(BigInteger.Max(lo,start)),Text(BigInteger.Min(hi,end)),input.TargetLocationRef));
                    if(hi>end)replacement.Add(slice with{From=Text(end)});
                }
                slices=replacement;
            }
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cp6_stock_move_v1.slices WHERE tenant_id={Tenant} AND environment_id={environment} AND root_id={Key(current.Root.Ref)}",ct);
            foreach(var slice in slices)await InsertSlice(current.Root.Ref,slice,ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_stock_move_v1.root_heads SET revision=revision+1 WHERE tenant_id={Tenant} AND environment_id={environment} AND root_id={Key(current.Root.Ref)}",ct);
        }
        foreach(var (key,delta) in check.CapacityDeltas)
        {
            // Format signed values explicitly: Text is intentionally for nonnegative quantities only.
            var signed=delta<0?"-"+Text(-delta):Text(delta);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_stock_move_v1.capacities SET occupied=occupied+{signed}::numeric,revision=revision+1 WHERE tenant_id={Tenant} AND environment_id={environment} AND id={key}",ct);
        }
        var legs=input.Spans.SelectMany(x=>new[]{new StockMoveLegV1(x,input.SourceLocationRef,"OUT",Text(Amount(x))),new StockMoveLegV1(x,input.TargetLocationRef,"IN",Text(Amount(x)))}).ToArray();
        var receipt=new StockMoveReceiptV1(Reference("DIRECT-MOVE-RECEIPT",Guid.NewGuid(),new{input,legs}),input.CanonicalKey,input.OperationId,"POSTED",input.SourceDemandRef,input.StageRef,legs,check.Claims);
        await Save(input,receipt,ct);return receipt;
    }
    // Called only after the Transfer Owner closes this exact source execution under its same transaction.
    internal async Task<StockMoveReceiptV1> Seal(StockMoveInputV1 input,CancellationToken ct)
    {
        await Fence(true,ct);var prior=await Original(input,ct);if(prior!=null)return prior;
        var receipt=new StockMoveReceiptV1(Reference("NO-EFFECT",Guid.NewGuid(),input),input.CanonicalKey,input.OperationId,"SEALED_NO_EFFECT",input.SourceDemandRef,input.StageRef,[],[]);
        await Save(input,receipt,ct);return receipt;
    }
    private async Task Save(StockMoveInputV1 input,StockMoveReceiptV1 receipt,CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_stock_move_v1.decisions VALUES({Tenant},{environment},{input.CanonicalKey},{input.OperationId},{Hash(input)},{receipt.State},{Bytes(receipt)})",ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_stock_move_v1.events VALUES({Tenant},{environment},{input.CanonicalKey},{Bytes(receipt)})",ct);
    }
}
