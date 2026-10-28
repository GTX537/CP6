using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.Services.Oa;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
namespace CP6.Core.Services.Plm;

// New ITER mapping codec. Existing OA/ENG original bytes and codecs are not changed.
internal static class IterationReviewCanonicalV1
{
    internal const string AggregateSchema="ITER-CANDIDATE-AGGREGATE/1",SnapshotSchema="ITER-OA-SNAPSHOT/1",
        PackageSchema="Main.Plm.IterationReviewPackage/1",MappingSchema="ITER-OA-APPLICATION/1";
    private static readonly UTF8Encoding Utf8=new(false,true);
    private static readonly JsonSerializerOptions Json=CreateJson();
    private static JsonSerializerOptions CreateJson()
    {
        // ITER packages retain native source instants at their original precision. The
        // separate OA envelope still uses OA's millisecond codec and frozen timestamp.
        var options=new JsonSerializerOptions(JsonSerializerDefaults.Web){PropertyNameCaseInsensitive=false};
        options.Converters.Add(new StrictString());return options;
    }
    private sealed class StrictString:JsonConverter<string>
    {
        public override string? Read(ref Utf8JsonReader r,Type t,JsonSerializerOptions o)=>r.GetString();
        public override void Write(Utf8JsonWriter w,string value,JsonSerializerOptions o){Text(value);w.WriteStringValue(value);}
        public override void WriteAsPropertyName(Utf8JsonWriter w,string value,JsonSerializerOptions o){Text(value);w.WritePropertyName(value);}
    }
    internal static void Text(string value)
    {try{_ = Utf8.GetByteCount(value);}catch(EncoderFallbackException){throw Error("ITERATION_REVIEW_UNICODE_INVALID",400);}}
    internal static byte[] Bytes(object value)=>Bytes(JsonSerializer.SerializeToElement(value,Json));
    internal static byte[] Bytes(JsonElement value)
    {
        var output=new StringBuilder();
        void String(string text)
        {
            Text(text);output.Append('"');
            foreach(var c in text)switch(c)
            {
                case '"':output.Append("\\\"");break;case '\\':output.Append("\\\\");break;
                case '\b':output.Append("\\b");break;case '\f':output.Append("\\f");break;
                case '\n':output.Append("\\n");break;case '\r':output.Append("\\r");break;case '\t':output.Append("\\t");break;
                default:if(c<32)output.Append("\\u"+((int)c).ToString("x4"));else output.Append(c);break;
            }
            output.Append('"');
        }
        void Write(JsonElement e)
        {
            switch(e.ValueKind)
            {
                case JsonValueKind.Object:
                    var fields=e.EnumerateObject().ToArray();
                    if(fields.Select(p=>p.Name).Distinct(StringComparer.Ordinal).Count()!=fields.Length)throw Error("ITERATION_REVIEW_DUPLICATE_KEY",400);
                    output.Append('{');var first=true;
                    foreach(var p in fields.OrderBy(p=>p.Name,CodePoints.Instance)){if(!first)output.Append(',');first=false;String(p.Name);output.Append(':');Write(p.Value);}output.Append('}');break;
                case JsonValueKind.Array:
                    output.Append('[');var itemFirst=true;foreach(var item in e.EnumerateArray()){if(!itemFirst)output.Append(',');itemFirst=false;Write(item);}output.Append(']');break;
                case JsonValueKind.String:String(e.GetString()!);break;
                case JsonValueKind.Number:output.Append(e.GetRawText());break;
                case JsonValueKind.Null:output.Append("null");break;
                case JsonValueKind.True:output.Append("true");break;
                case JsonValueKind.False:output.Append("false");break;
                default:throw Error("ITERATION_REVIEW_JSON_INVALID",400);
            }
        }
        try{Write(value);return Utf8.GetBytes(output.ToString());}
        catch(InvalidOperationException){throw Error("ITERATION_REVIEW_UNICODE_INVALID",400);}
    }
    internal static string Hash(byte[] bytes)=>ApprovalCanonicalV2.Hash(bytes);
    internal static string Domain(string schema,object value)=>Hash(Utf8.GetBytes(schema+"\n").Concat(Bytes(value)).ToArray());
    internal static ApprovalDigestV2 Digest(string schema,byte[] bytes)=>new("sha256",schema,Hash(bytes));
    internal sealed class CodePoints:IComparer<string>
    {
        internal static readonly CodePoints Instance=new();
        public int Compare(string? x,string? y)
        {
            if(x==null || y==null)return x==y?0:x==null?-1:1;
            Text(x);Text(y);var a=x.EnumerateRunes().GetEnumerator();var b=y.EnumerateRunes().GetEnumerator();
            while(true){var hasA=a.MoveNext();var hasB=b.MoveNext();if(!hasA || !hasB)return hasA==hasB?0:hasA?1:-1;var c=a.Current.Value.CompareTo(b.Current.Value);if(c!=0)return c;}
        }
    }
}
