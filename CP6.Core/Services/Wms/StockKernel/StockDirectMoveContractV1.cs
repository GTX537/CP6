using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CP6.Core.Services.Erp;
namespace CP6.Core.Services.Wms.StockKernel;

// The Stock-owned direct-MOVE subset. Full references stay opaque; a native stock ID is not a Root.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StockRefV1(string Owner,string Id,string Version,string DigestDomain,string CodecVersion,string Digest);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StockSpanV1(StockRefV1 RootRef,string From,string To);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StockDimensionV1(StockRefV1 ProductRef,StockRefV1 TechnicalRef,StockRefV1 OwnerRef,StockRefV1 BaseUomRef,StockRefV1 OriginPurposeRef,StockRefV1 OriginSiteRef);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StockSliceV1(string From,string To,StockRefV1 LocationRef);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StockMetricRateV1(StockRefV1 MetricRef,StockRefV1 UomRef,string Numerator,string Denominator);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StockOpeningRootV1(StockRefV1 Ref,StockRefV1 SourceRef,StockDimensionV1 Dimension,string Quantity,
    [property:JsonRequired]bool Serial,StockSliceV1[] Slices,StockMetricRateV1[] Rates,StockRefV1 EligibilityRef,[property:JsonRequired]bool Movable);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StockClaimBindingV1(StockRefV1 ClaimRef,StockRefV1 BindingRef,StockSpanV1 Span,string State,
    StockRefV1 UseControlRef,[property:JsonRequired]bool MoveAllowed);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StockCapacityV1(StockRefV1 DomainRef,StockRefV1 MetricRef,StockRefV1 UomRef,string Mode,
    [property:JsonRequired]string? Limit,string OtherOccupancy);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StockLocationV1(StockRefV1 Ref,Guid NativeLocationId,string WarehouseCode,string LocationCode,string[] ProductCodes,StockRefV1[] CapacityDomainRefs);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StockProductMapV1(StockRefV1 Ref,string NativeProductCode);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StockOpeningV1(StockRefV1 Ref,string DataKind,StockLocationV1[] Locations,StockOpeningRootV1[] Roots,
    StockClaimBindingV1[] Claims,StockCapacityV1[] Capacities,StockProductMapV1[] Products);
internal sealed record StockMoveInputV1(string CanonicalKey,Guid OperationId,StockRefV1 SourceDemandRef,StockRefV1 StageRef,
    StockRefV1 SourceLocationRef,StockRefV1 TargetLocationRef,StockSpanV1[] Spans);
public sealed record StockMoveLegV1(StockSpanV1 Span,StockRefV1 LocationRef,string Direction,string Quantity);
public sealed record StockMoveReceiptV1(StockRefV1 Ref,string CanonicalKey,Guid OperationId,string State,
    StockRefV1 SourceDemandRef,StockRefV1 StageRef,StockMoveLegV1[] Movements,StockClaimBindingV1[] Claims);
internal sealed record StockRootCurrentV1(StockOpeningRootV1 Root,long Revision,StockSliceV1[] Slices);

internal static partial class StockDirectMoveValuesV1
{
    internal static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    internal static byte[] Bytes<T>(T value)=>JsonSerializer.SerializeToUtf8Bytes(value,Json);
    internal static T Decode<T>(byte[] value)=>JsonSerializer.Deserialize<T>(value,Json)!;
    internal static string Hash<T>(T value)=>Convert.ToHexString(SHA256.HashData(Bytes(value))).ToLowerInvariant();
    internal static string Key(StockRefV1 r)=>Hash(new{r.Owner,r.Id});
    internal static OrderPriceCorrectionException Error(string code,int status=409)=>new(code,status);
    [GeneratedRegex("\\A(0|[1-9][0-9]{0,29})(\\.[0-9]{1,8})?\\z",RegexOptions.CultureInvariant)]private static partial Regex QuantityPattern();
    internal static BigInteger Number(string? value,bool total=false,bool positive=false)
    {
        if(value==null||!QuantityPattern().IsMatch(value)||!total&&value.Split('.')[0].Length>13)throw Error("STOCK_QUANTITY_INVALID",400);
        var parts=value.Split('.');var n=BigInteger.Parse(parts[0],CultureInfo.InvariantCulture)*100000000+BigInteger.Parse(parts.Length==1?"0":parts[1].PadRight(8,'0'),CultureInfo.InvariantCulture);
        if(positive&&n==0)throw Error("STOCK_QUANTITY_INVALID",400);return n;
    }
    internal static string Text(BigInteger n)
    {if(n<0||n>=BigInteger.Pow(10,38))throw Error("STOCK_TOTAL_OVERFLOW",422);return string.Create(CultureInfo.InvariantCulture,$"{n/100000000}.{n%100000000:D8}");}
    internal static BigInteger Amount(StockSpanV1 s)=>Number(s.To)-Number(s.From);
    internal static StockRefV1 Reference(string kind,Guid id,object body)=>new("STOCK",id.ToString("D"),"1","STOCK-"+kind,"1",Hash(body));
    internal static void ValidateRef(StockRefV1? r)
    {if(r==null||new[]{r.Owner,r.Id,r.Version,r.DigestDomain,r.CodecVersion}.Any(x=>string.IsNullOrWhiteSpace(x)||x.Length>512)||r.Digest==null||r.Digest.Length!=64||r.Digest.Any(x=>!Uri.IsHexDigit(x)))throw Error("STOCK_REFERENCE_INVALID",400);}
    internal static bool Overlap(StockSpanV1 a,StockSpanV1 b)=>Key(a.RootRef)==Key(b.RootRef)&&Number(a.From)<Number(b.To)&&Number(b.From)<Number(a.To);
}
