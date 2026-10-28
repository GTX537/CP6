using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.Services.Plm;
namespace CP6.Core.Services.Wms;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LocationRuleCodeRefV1(string Id,string Version);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LocationRuleSelectorKeyV1(string RuleFamily,string MetricId,LocationRuleCodeRefV1 ApplicabilityDomainRef);
[JsonConverter(typeof(LocationRuleCapacityConverterV1))]
public sealed record LocationRuleCapacityV1(string Mode,string? Value=null,LocationRuleCodeRefV1? MetricRef=null,
    LocationRuleCodeRefV1? UomRef=null,LocationRuleCodeRefV1? PrecisionPolicyRef=null,string? Reason=null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LocationRuleDraftV1(LocationRuleSelectorKeyV1 SelectorKey,LocationRuleCapacityV1 Capacity,
    string[] AllowedProductTypes,string[] AllowedOperations,[property:JsonRequired] bool IsPickable,
    [property:JsonRequired] string? ValidFrom,[property:JsonRequired] string? ValidTo);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LocationRuleCreateV1(Guid OperationId,LocationRuleDraftV1 Draft);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LocationRulePatchV1([property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingDefault)] JsonElement Capacity=default,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingDefault)] JsonElement AllowedProductTypes=default,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingDefault)] JsonElement AllowedOperations=default,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingDefault)] JsonElement IsPickable=default,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingDefault)] JsonElement ValidFrom=default,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingDefault)] JsonElement ValidTo=default);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LocationRuleUpdateV1(Guid OperationId,LocationRulePatchV1 Patch);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LocationRuleWithdrawV1(Guid OperationId,string Reason);
public sealed record LocationRuleScopeV1(Guid TenantId,Guid ActorId,Guid LocationId);
public sealed record LocationRuleFieldV1(string Field,bool CanRead,bool CanWrite);
public sealed record LocationRuleSelectorV1(Guid Id,string RuleFamily,string MetricId,string ApplicabilityDomainId,string Revision,Guid? CurrentRuleVersionId);
public sealed record LocationRuleOriginalV1(Guid TenantId,Guid Id,Guid LocationId,Guid SelectorId,string Version,
    Guid OperationId,Guid ActorId,DateTimeOffset RecordedAt,LocationRuleDraftV1 Draft,string ReferenceResolution,RuleReferenceBindingV1? ReferenceBinding=null);
public sealed record LocationRuleViewV1(EngineeringSourceRefV1 Reference,Guid LocationId,Guid SelectorId,string State,
    string Etag,Dictionary<string,object?> Candidate,string[] RedactedFields,string? OriginalJson)
{
    public string ReferenceResolution{get;init;}="UNRESOLVED";
    public RuleReferenceBindingV1? ReferenceBinding{get;init;}
    public string Approval=>"NOT_SUBMITTED";
    public bool AdmissionGrant=>false;
}
public sealed record LocationRuleContextV1(LocationRuleScopeV1 Scope,string LocationCode,string InitializationState,
    LocationRuleFieldV1[] Fields,LocationRuleSelectorV1[] Selectors)
{
    public string ReferenceAuthorities{get;init;}="UNAVAILABLE";
    public Guid? WarehouseId{get;init;}
    public EngineeringSourceRefV1? ReferencePolicy{get;init;}
    public RuleReferenceAuthorityV1[] ReferenceAuthorityBindings{get;init;}=[];
    public RuleReferencePolicyV1? ReferenceCatalog{get;init;}
    public string ApplicationCapability=>"UNAVAILABLE";
    public bool AdmissionGrant=>false;
}
public sealed record LocationRuleResultV1(Guid OperationId,Guid LocationId,EngineeringSourceRefV1 Reference,
    Guid SelectorId,string Etag,string State,string Effect);
public sealed record LocationRuleOperationV1(Guid OperationId,Guid LocationId,string State,LocationRuleResultV1? Result=null);

public sealed class LocationRuleCapacityConverterV1:JsonConverter<LocationRuleCapacityV1>
{
    public override LocationRuleCapacityV1 Read(ref Utf8JsonReader reader,Type type,JsonSerializerOptions options)
    {
        using var doc=JsonDocument.ParseValue(ref reader);var e=doc.RootElement;
        if(e.ValueKind!=JsonValueKind.Object||!e.TryGetProperty("mode",out var m)||m.ValueKind!=JsonValueKind.String)throw new JsonException("CAPACITY_MODE_REQUIRED");
        var mode=m.GetString();string[] fields=mode switch{"LIMITED"=>["mode","value","metricRef","uomRef","precisionPolicyRef"],"UNLIMITED"=>["mode"],"UNKNOWN"=>["mode","reason"],_=>throw new JsonException("CAPACITY_MODE_INVALID")};
        var names=e.EnumerateObject().Select(p=>p.Name).ToArray();
        if(names.Length!=fields.Length||names.Distinct(StringComparer.Ordinal).Count()!=names.Length||names.Except(fields,StringComparer.Ordinal).Any())throw new JsonException("CAPACITY_FIELDS_INVALID");
        string Text(string name)=>e.GetProperty(name).ValueKind==JsonValueKind.String?e.GetProperty(name).GetString()!:throw new JsonException("CAPACITY_FIELD_INVALID");
        LocationRuleCodeRefV1 Ref(string name)=>e.GetProperty(name).Deserialize<LocationRuleCodeRefV1>(new JsonSerializerOptions(JsonSerializerDefaults.Web))??throw new JsonException("CAPACITY_REFERENCE_INVALID");
        return mode switch{"LIMITED"=>new(mode,Text("value"),Ref("metricRef"),Ref("uomRef"),Ref("precisionPolicyRef")),"UNKNOWN"=>new(mode,Reason:Text("reason")),_=>new(mode!)};
    }
    public override void Write(Utf8JsonWriter writer,LocationRuleCapacityV1 value,JsonSerializerOptions options)
    {
        writer.WriteStartObject();writer.WriteString("mode",value.Mode);
        if(value.Mode=="LIMITED")
        {
            writer.WriteString("value",value.Value);
            foreach(var (name,r) in new[]{("metricRef",value.MetricRef),("uomRef",value.UomRef),("precisionPolicyRef",value.PrecisionPolicyRef)})
            {writer.WritePropertyName(name);writer.WriteStartObject();writer.WriteString("id",r?.Id);writer.WriteString("version",r?.Version);writer.WriteEndObject();}
        }
        else if(value.Mode=="UNKNOWN")writer.WriteString("reason",value.Reason);
        writer.WriteEndObject();
    }
}
