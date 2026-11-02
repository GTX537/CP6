using System.Text.Json.Nodes;
using CP6.Core.Services.Sys;

namespace CP6.Core.Services.Oa;

internal sealed record PersonalFormRebasePlanV2(string Data, PersonalFormRebaseChangeV2[] Changes, string[] Errors, bool Loss);
internal static class PersonalFormRebaseV2
{
    internal static PersonalFormRebasePlanV2 Build(string sourceSchema,string targetSchema,string data,
        IReadOnlyDictionary<string,string> sourceMask,IReadOnlyDictionary<string,string> targetMask)
    {
        static JsonNode? P(JsonObject value,string key)=>PersonalFormValuesV2.SchemaProperty(value,key);
        static Dictionary<string,JsonObject> Fields(JsonArray array)=>array.ToDictionary(x=>P(x!.AsObject(),"name")!.GetValue<string>(),x=>x!.AsObject(),StringComparer.Ordinal);
        var source=Fields(P(JsonNode.Parse(sourceSchema)!.AsObject(),"fields")!.AsArray());
        var target=Fields(P(JsonNode.Parse(targetSchema)!.AsObject(),"fields")!.AsArray());
        var original=JsonNode.Parse(data)!.AsObject();var result=new JsonObject();var changes=new List<PersonalFormRebaseChangeV2>();var errors=new List<string>();var loss=false;
        static string Type(JsonObject field)=>P(field,"type")?.GetValue<string>().ToLowerInvariant()??"input";
        static bool HasValue(JsonNode? node)=>node switch{null=>false,JsonArray a=>a.Count!=0,JsonObject o=>o.Count!=0,_=>node.ToJsonString()!="\"\""};
        void Changed(string field,string path,string kind,bool valued)
        {
            if(valued && (sourceMask.GetValueOrDefault(field,"hidden")=="hidden" || targetMask.GetValueOrDefault(field,sourceMask.GetValueOrDefault(field,"hidden"))=="hidden"))
                throw new ScopeAuthorizationExceptionV1("REBASE_HIDDEN_LOSS_BLOCKED",403);
            loss|=valued;
            if(sourceMask.GetValueOrDefault(field,"readonly")!="hidden" && targetMask.GetValueOrDefault(field,"readonly")!="hidden")changes.Add(new(path,kind,valued));
        }
        foreach(var (name,field) in target)
        {
            if(!source.TryGetValue(name,out var previous) || Type(previous)!=Type(field))
            {
                var valued=original.TryGetPropertyValue(name,out var old)&&HasValue(old);
                Changed(name,name,previous==null?"ADDED":"TYPE_CHANGED",valued);result[name]=P(field,"default")?.DeepClone();continue;
            }
            if(!original.TryGetPropertyValue(name,out var value))continue; // Preserve unset, distinct from null.
            if(Type(field)!="table" || value==null){result[name]=value?.DeepClone();continue;}
            var columns=Fields(P(field,"columns")!.AsArray());
            var oldColumns=Fields(P(previous,"columns")!.AsArray());
            var rows=new JsonArray();var index=0;
            foreach(var row in value.AsArray())
            {
                var current=row!.AsObject();var next=new JsonObject();
                foreach(var (column,definition) in columns)
                {
                    if(oldColumns.TryGetValue(column,out var oldColumn)&&Type(oldColumn)==Type(definition))
                    {if(current.TryGetPropertyValue(column,out var cell))next[column]=cell?.DeepClone();}
                    else
                    {
                        Changed(name,$"{name}[{index}].{column}",oldColumn==null?"ADDED":"TYPE_CHANGED",current.TryGetPropertyValue(column,out var cell)&&HasValue(cell));
                        next[column]=P(definition,"default")?.DeepClone();
                    }
                }
                foreach(var column in oldColumns.Keys.Where(c=>!columns.ContainsKey(c)))
                    Changed(name,$"{name}[{index}].{column}","REMOVED",current.TryGetPropertyValue(column,out var cell)&&HasValue(cell));
                rows.Add(next);index++;
            }
            result[name]=rows;
            if(rows.Count>(P(field,"maxRows")?.GetValue<int>()??100))errors.Add(targetMask[name]=="hidden"?"FORM_VALIDATION_FAILED":name+":MAX_ROWS_EXCEEDED");
        }
        foreach(var name in source.Keys.Where(n=>!target.ContainsKey(n))) Changed(name,name,"REMOVED",original.TryGetPropertyValue(name,out var value)&&HasValue(value));
        var computed=PersonalFormValuesV2.Recompute(PersonalFormValuesV2.Schema(targetSchema),result.ToJsonString(),validateShape:false);
        if(computed.Errors.Length!=0)errors.Add("FORM_VALIDATION_FAILED");
        return new(computed.Data,changes.ToArray(),errors.Distinct().ToArray(),loss);
    }
}
