using System.Text.Json.Nodes;
using CP6.Core.Services.Sys;

namespace CP6.Core.Services.Oa;

internal sealed record PersonalFormRebasePlanV2(string Data, PersonalFormRebaseChangeV2[] Changes, string[] Errors, bool Loss,
    string[] RetainedPaths, string[] RemovedPaths, string[] AddedPaths, string[] TypeChangedPaths,
    string[] DefaultedPaths, string[] RequiredAfterRebase, PersonalFormRebaseDiagnosticV2[] Diagnostics);
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
        var paths=new List<(string Field,string Path,string Kind)>();var diagnostics=new List<(string Field,string Path,string Code)>();
        void Path(string field,string path,string kind)=>paths.Add((field,path,kind));
        JsonNode? Default(string field,string path,JsonObject definition)
        {
            if(definition.Any(x=>string.Equals(x.Key,"default",StringComparison.OrdinalIgnoreCase)))Path(field,path,"DEFAULTED");
            return P(definition,"default")?.DeepClone();
        }
        static string Type(JsonObject field)=>P(field,"type")?.GetValue<string>().ToLowerInvariant()??"input";
        static bool HasValue(JsonNode? node)=>node switch{null=>false,JsonArray a=>a.Count!=0,JsonObject o=>o.Count!=0,_=>node.ToJsonString()!="\"\""};
        void Changed(string field,string path,string kind,bool valued)
        {
            if(valued && (sourceMask.GetValueOrDefault(field,"hidden")=="hidden" || targetMask.GetValueOrDefault(field,sourceMask.GetValueOrDefault(field,"hidden"))=="hidden"))
                throw new ScopeAuthorizationExceptionV1("REBASE_HIDDEN_LOSS_BLOCKED",403);
            loss|=valued;
            Path(field,path,kind);
            if(kind=="TYPE_CHANGED")Path(field,path,"REMOVED");
            if(sourceMask.GetValueOrDefault(field,"readonly")!="hidden" && targetMask.GetValueOrDefault(field,"readonly")!="hidden")changes.Add(new(path,kind,valued));
        }
        foreach(var (name,field) in target)
        {
            if(!source.TryGetValue(name,out var previous) || Type(previous)!=Type(field))
            {
                var valued=original.TryGetPropertyValue(name,out var old)&&HasValue(old);
                Changed(name,name,previous==null?"ADDED":"TYPE_CHANGED",valued);result[name]=Default(name,name,field);continue;
            }
            if(!original.TryGetPropertyValue(name,out var value)){Path(name,name,"RETAINED");continue;} // Preserve unset, distinct from null.
            if(Type(field)!="table" || value==null){Path(name,name,"RETAINED");result[name]=value?.DeepClone();continue;}
            var columns=Fields(P(field,"columns")!.AsArray());
            var oldColumns=Fields(P(previous,"columns")!.AsArray());
            var rows=new JsonArray();var index=0;
            foreach(var row in value.AsArray())
            {
                var current=row!.AsObject();var next=new JsonObject();
                foreach(var (column,definition) in columns)
                {
                    if(oldColumns.TryGetValue(column,out var oldColumn)&&Type(oldColumn)==Type(definition))
                    {Path(name,$"{name}[{index}].{column}","RETAINED");if(current.TryGetPropertyValue(column,out var cell))next[column]=cell?.DeepClone();}
                    else
                    {
                        Changed(name,$"{name}[{index}].{column}",oldColumn==null?"ADDED":"TYPE_CHANGED",current.TryGetPropertyValue(column,out var cell)&&HasValue(cell));
                        next[column]=Default(name,$"{name}[{index}].{column}",definition);
                    }
                }
                foreach(var column in oldColumns.Keys.Where(c=>!columns.ContainsKey(c)))
                    Changed(name,$"{name}[{index}].{column}","REMOVED",current.TryGetPropertyValue(column,out var cell)&&HasValue(cell));
                rows.Add(next);index++;
            }
            result[name]=rows;
            if(rows.Count==0)Path(name,name,"RETAINED");
            var maxRows=P(field,"maxRows")?.GetValue<int>()??100;
            if(rows.Count>maxRows)
            {
                errors.Add(targetMask[name]=="hidden"?"FORM_VALIDATION_FAILED":name+":MAX_ROWS_EXCEEDED");
                for(var row=maxRows;row<rows.Count;row++)diagnostics.Add((name,$"{name}[{row}]","ROW_COUNT_EXCEEDS_TARGET"));
            }
        }
        foreach(var name in source.Keys.Where(n=>!target.ContainsKey(n))) Changed(name,name,"REMOVED",original.TryGetPropertyValue(name,out var value)&&HasValue(value));
        var computed=PersonalFormValuesV2.Recompute(PersonalFormValuesV2.Schema(targetSchema),result.ToJsonString(),validateShape:false,
            observeRequired:(field,path,missing)=>{Path(field,path,"REQUIRED");if(missing)diagnostics.Add((field,path,"REQUIRED"));});
        if(computed.Errors.Length!=0)errors.Add("FORM_VALIDATION_FAILED");
        bool Visible(string field)=>sourceMask.GetValueOrDefault(field,"readonly")!="hidden"
            && targetMask.GetValueOrDefault(field,"readonly")!="hidden" && !computed.Hidden.Contains(field);
        string[] Paths(string kind)=>paths.Where(x=>x.Kind==kind&&Visible(x.Field)).Select(x=>x.Path).Distinct().ToArray();
        return new(computed.Data,changes.ToArray(),errors.Distinct().ToArray(),loss,Paths("RETAINED"),Paths("REMOVED"),Paths("ADDED"),
            Paths("TYPE_CHANGED"),Paths("DEFAULTED"),Paths("REQUIRED"),diagnostics.Where(x=>Visible(x.Field)).Select(x=>new PersonalFormRebaseDiagnosticV2(x.Path,x.Code)).ToArray());
    }
}
