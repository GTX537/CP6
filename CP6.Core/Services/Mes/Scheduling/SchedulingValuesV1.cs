using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CP6.Core.Services.Erp;
namespace CP6.Core.Services.Mes.Scheduling;

internal static class SchedulingValuesV1
{
    internal const string Policy="PRESERVE_OR_EXPAND_ROOT_ENVELOPE_V1";
    internal static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow};
    internal static OrderPriceCorrectionException Error(string code,int status=400)=>new(code,status);
    internal static byte[] Bytes<T>(T value)=>JsonSerializer.SerializeToUtf8Bytes(value,Json);
    internal static DateTimeOffset Now(){var t=DateTimeOffset.UtcNow;return new(t.Ticks-t.Ticks%10,TimeSpan.Zero);}
    internal static T Decode<T>(byte[] bytes)=>JsonSerializer.Deserialize<T>(bytes,Json)!;
    // SCHED-JCS-1: sorted object properties, NFC strings, exact decimal/version strings and UTC microsecond times.
    internal static string Digest<T>(T value)
    {
        var element=JsonSerializer.SerializeToElement(value,Json);using var stream=new MemoryStream();
        using(var writer=new Utf8JsonWriter(stream))Write(element,writer);
        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }
    private static void Write(JsonElement e,Utf8JsonWriter w)
    {
        switch(e.ValueKind)
        {
            case JsonValueKind.Object:w.WriteStartObject();foreach(var p in e.EnumerateObject().OrderBy(p=>p.Name,StringComparer.Ordinal)){w.WritePropertyName(p.Name.Normalize());Write(p.Value,w);}w.WriteEndObject();break;
            case JsonValueKind.Array:w.WriteStartArray();foreach(var x in e.EnumerateArray())Write(x,w);w.WriteEndArray();break;
            case JsonValueKind.String:
                // Typed instants are canonicalized by their converter; ordinary text keeps its meaning.
                w.WriteStringValue(e.GetString()!.Normalize(NormalizationForm.FormC));break;
            default:e.WriteTo(w);break;
        }
    }
    internal static void Version(string value){if(value==null||!Regex.IsMatch(value,@"\A[1-9][0-9]{0,29}\z"))throw Error("SCHED_VERSION_INVALID");}
    internal static string Next(string value){var next=(BigInteger.Parse(value,CultureInfo.InvariantCulture)+1).ToString(CultureInfo.InvariantCulture);if(next.Length>30)throw Error("SCHED_VERSION_EXHAUSTED",409);return next;}
    internal static void Reference(SchedRefV1 r)
    {if(r==null||string.IsNullOrWhiteSpace(r.Owner)||r.Owner.Length>128||r.Id==Guid.Empty||string.IsNullOrWhiteSpace(r.Digest)||r.Digest.Length>512)throw Error("SCHED_REFERENCE_INVALID");Version(r.Version);}
    internal static SchedRefV1 Ref(string owner,Guid id,string version,object content)=>new(owner,id,version,Digest(content));
    internal static void Expected(WoExpectedV1 e)
    {if(e==null||e.SnapshotId==Guid.Empty)throw Error("SCHED_EXPECTED_INVALID");Version(e.Revision);Version(e.ControlEpoch);Version(e.EvidenceVersion);Version(e.BindingRegistryRevision);}
    internal static void Interval(SchedIntervalV1 value)
    {if(value==null||value.Start>=value.End||value.Start.Ticks%10!=0||value.End.Ticks%10!=0)throw Error("SCHED_INTERVAL_INVALID",422);}
    internal static void Reason(string value){if(string.IsNullOrWhiteSpace(value)||value.Length>500)throw Error("SCHED_REASON_INVALID");}
    internal static void Quantity(SchedQuantityV1? value)
    {if(value==null)return;if(value.Value==null||!Regex.IsMatch(value.Value,@"\A(?:0|[1-9][0-9]{0,12})(?:\.[0-9]{1,8})?\z"))throw Error("SCHED_QUANTITY_INVALID");Reference(value.UnitRef);Reference(value.BasisRef);}
    internal static bool Contains(SchedIntervalV1 a,SchedIntervalV1 b)=>a.Start<=b.Start&&a.End>=b.End;
    internal static bool Overlap(SchedIntervalV1 a,SchedIntervalV1 b)=>a.Start<b.End&&b.Start<a.End;
    internal static SchedRefV1 OperationSet(SchedRootV1 r)=>Ref("WO-OPERATION-SET",r.RootId,r.Expected.Revision,r.Operations.OrderBy(x=>x.OperationId).ToArray());
    internal static void Opening(SchedOpeningV1 input)
    {
        if(input?.DataKind!="SIMULATED"||input.RootId==Guid.Empty||input.RootSchedule==null||string.IsNullOrWhiteSpace(input.ZoneId)||input.ZoneId.Length>64)throw Error("SCHED_SIMULATED_INPUT_REQUIRED");
        Reference(input.SiteRef);Reference(input.SnapshotRef);Expected(input.Expected);Reason(input.RootSchedule.Reason);
        if(input.Expected.SnapshotId!=input.SnapshotRef.Id||input.SnapshotRef.Owner!="WO-SNAPSHOT"||input.Operations is not{Length:>0 and <=500}
            ||input.Operations.Any(x=>x==null||x.OccurrenceRef==null)
            ||input.Operations.Select(x=>x.OperationId).Distinct().Count()!=input.Operations.Length||input.Operations.Select(x=>x.OccurrenceRef.Id).Distinct().Count()!=input.Operations.Length)
            throw Error("SCHED_ROOT_MANIFEST_INVALID");
        try{TimeZoneInfo.FindSystemTimeZoneById(input.ZoneId);}catch(TimeZoneNotFoundException){throw Error("SCHED_ZONE_INVALID");}catch(InvalidTimeZoneException){throw Error("SCHED_ZONE_INVALID");}
        if(input.RootSchedule.Interval!=null)Interval(input.RootSchedule.Interval);
        if(input.RootSchedule.Priority is not("NORMAL" or "URGENT" or "EXPEDITE"))throw Error("SCHED_PRIORITY_INVALID");
        foreach(var op in input.Operations)
        {if(op.OperationId==Guid.Empty||string.IsNullOrWhiteSpace(op.ProcessLabel)||op.ProcessLabel.Length>200)throw Error("SCHED_OPERATION_INVALID");
         Reference(op.OccurrenceRef);Reference(op.AssignmentRef);Reference(op.ResourceRef);Version(op.ScheduleRevision);
         if(op.Interval!=null)Interval(op.Interval);Quantity(op.PlanQty);Quantity(op.GoodQty);Quantity(op.DefectQty);}
        Observation(input.Observation,input.RootId,input.Operations);
    }
    internal static void Observation(SchedObservationV1 input,Guid root,SchedOperationV1[] operations)
    {
        if(input?.DataKind!="SIMULATED"||input.RootId!=root)throw Error("SCHED_SIMULATED_INPUT_REQUIRED");Reference(input.Reference);
        if(input.Guards==null||input.Guards.Any(x=>x==null)||!input.Guards.Select(x=>x.OperationId).Order().SequenceEqual(operations.Select(x=>x.OperationId).Order()))throw Error("SCHED_SOURCE_MANIFEST_INVALID");
        foreach(var g in input.Guards)
        {Reference(g.SourceWindowRef);Reference(g.ClaimRef);Interval(g.SourceWindow);Interval(g.UseInterval);
         if(g.ExecutionState is not("NOT_STARTED" or "STARTED" or "COMPLETED" or "UNKNOWN"))throw Error("SCHED_EXECUTION_STATE_INVALID");
         if(g.UnavailableIntervals==null||g.PredecessorOccurrences==null)throw Error("SCHED_CONSTRAINTS_REQUIRED");
         if(g.ActualInterval!=null)Interval(g.ActualInterval);foreach(var b in g.UnavailableIntervals)Interval(b);
         foreach(var r in g.PredecessorOccurrences){Reference(r);if(!operations.Any(x=>x.OccurrenceRef==r)||operations.Single(x=>x.OccurrenceRef==r).OperationId==g.OperationId)throw Error("SCHED_PREDECESSOR_INVALID");}}
    }
}
