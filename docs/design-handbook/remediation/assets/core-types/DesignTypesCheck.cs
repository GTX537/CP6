using System.Text.Json;
using Cp6.DesignRevision;

var checks = 0;
void Assert(bool value, string name) { if (!value) throw new Exception(name); checks++; }
string Key(string json) { using var d=JsonDocument.Parse(json); return JsonSemanticEquality.Key(d.RootElement); }
Assert(JsonSerializer.Serialize(JsonSerializer.Deserialize<NullCarrier>("{\"fixedNull\":null}"))=="{\"fixedNull\":null}", "null-only round trip");
foreach(var text in new[]{"{}", "{\"fixedNull\":0}", "{\"fixedNull\":1}", "{\"fixedNull\":false}", "{\"fixedNull\":\"null\"}", "{\"fixedNull\":{}}"})
{
    var rejected=false;
    try { JsonSerializer.Deserialize<NullCarrier>(text); } catch(JsonException) { rejected=true; }
    Assert(rejected, "required null rejects "+text);
}
Assert(Key("{\"a\":1,\"b\":[true,null]}")==Key("{\"b\":[true,null],\"a\":1.0}"), "property order and exact numeric equality");
Assert(Key("[1,2]")!=Key("[2,1]"),"array order");
Assert(Key("\"1\"")!=Key("1"),"JSON type distinction");
Assert(Key("-0.00")==Key("0e1000"),"zero variants");
Assert(Key("12345678901234567890123456789012345678")==Key("123456789012345678901234567890123456780e-1"),"38 digit equality without double");
Assert(Key("1e1000")==Key("10e999"),"large exponent no floating point overflow");
Assert(Key("{\"a:b\":\"x,y\"}")!=Key("{\"a\":\"b:x,y\"}"),"escaped property boundary");
Console.WriteLine(JsonSerializer.Serialize(new { result="PASS",checks,scope="Isolated design helper only; archived DTO/application/database not compiled or run",runtime_acceptance="NOT_RUN" }));

public sealed class NullCarrier
{
    [System.Text.Json.Serialization.JsonPropertyName("fixedNull")]
    public required NullOnly FixedNull { get; init; }
}
