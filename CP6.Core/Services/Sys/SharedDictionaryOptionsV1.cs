using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Sys;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormOptionSourceV1(string Owner,string Scope,int TypeId)
{
    public const string OwnerName="Main.Sys.DictionaryOptions.v1";
}
public sealed record FormOptionItemV1(string Value,string Label);
public sealed record FormOptionPageV1(FormOptionItemV1[] Items,int Page,bool HasMore,string Scope="SHARED");
public sealed record FormOptionTypeV1(int Id,string Name);

/// <summary>Only the existing shared reference dictionary. Tenant business sources require their own Owner contract.</summary>
internal sealed class SharedDictionaryOptionsV1(CP6Context db)
{
    internal sealed record SourceField(string Field,string? Column,FormOptionSourceV1 Source,string Type,string Path);
    internal static IEnumerable<SourceField> Fields(FormSchema schema)
    {
        foreach(var (field,index) in schema.Fields.Select((f,i)=>(f,i)))
        {
            if(field.OptionSourceV1 is {} source)yield return new(field.Name,null,source,field.Type,$"$.fields[{index}]");
            if(!field.Type.Equals("table",StringComparison.OrdinalIgnoreCase))continue;
            foreach(var (column,j) in field.Columns.Select((c,i)=>(c,i)))
                if(column.OptionSourceV1 is {} nested)yield return new(field.Name,column.Name,nested,column.Type,$"$.fields[{index}].columns[{j}]");
        }
    }
    private bool ready;
    private readonly Dictionary<FormOptionSourceV1,FormOptionItemV1[]> sourceCache=[];
    private async Task Ready(CancellationToken ct)
    {
        if(ready)return;
        if(!await db.Database.SqlQueryRaw<bool>("SELECT cp6_form_options_v1.read_source() AS \"Value\"").SingleAsync(ct))
            throw new ScopeAuthorizationExceptionV1("FORM_OPTION_SOURCE_COVERAGE_UNAVAILABLE",503);
        ready=true;
    }
    internal async Task<FormOptionTypeV1[]> Types(string search,int page,CancellationToken ct)
    {
        await Ready(ct);
        return await db.Sys_DictTypes.AsNoTracking().Where(t=>t.Enable&&(search==""||t.TypeName.Contains(search)))
            .OrderBy(t=>t.OrderNo).ThenBy(t=>t.Id).Skip((page-1)*100).Take(100).Select(t=>new FormOptionTypeV1(t.Id,t.TypeName)).ToArrayAsync(ct);
    }
    internal async Task<FormOptionItemV1[]> Source(FormOptionSourceV1 source,CancellationToken ct)
    {
        if(sourceCache.TryGetValue(source,out var cached))return cached;
        if(source.Owner!=FormOptionSourceV1.OwnerName||source.Scope!="SHARED"||source.TypeId<=0)
            throw new ScopeAuthorizationExceptionV1("FORM_OPTION_SOURCE_INVALID",422);
        await Ready(ct);
        var type=await db.Sys_DictTypes.AsNoTracking().SingleOrDefaultAsync(t=>t.Id==source.TypeId,ct);
        if(type==null||!type.Enable)throw new ScopeAuthorizationExceptionV1("FORM_OPTION_SOURCE_UNAVAILABLE",422);
        // Existing dictionary membership uses its provider collation. More than one type for that code is ambiguous.
        if(await db.Sys_DictTypes.CountAsync(t=>t.TypeCode==type.TypeCode,ct)!=1)
            throw new ScopeAuthorizationExceptionV1("FORM_OPTION_SOURCE_AMBIGUOUS",422);
        var values=await db.Sys_DictDatas.AsNoTracking().Where(d=>d.Enable&&d.TypeCode==type.TypeCode)
            .OrderBy(d=>d.OrderNo).ThenBy(d=>d.Id).Select(d=>new FormOptionItemV1(d.Value,d.Label)).ToArrayAsync(ct);
        if(values.Select(v=>v.Value).Distinct(StringComparer.Ordinal).Count()!=values.Length)
            throw new ScopeAuthorizationExceptionV1("FORM_OPTION_SOURCE_AMBIGUOUS",422);
        sourceCache[source]=values;return values;
    }
    internal async Task<FormOptionPageV1> Options(FormOptionSourceV1 source,string search,int page,CancellationToken ct)
    {
        var all=(await Source(source,ct)).Where(v=>search==""||v.Label.Contains(search,StringComparison.OrdinalIgnoreCase)||v.Value.Contains(search,StringComparison.Ordinal)).Skip((page-1)*100).Take(101).ToArray();
        return new(all.Take(100).ToArray(),page,all.Length>100);
    }
    internal Task CheckDefaults(FormSchema schema,string text,CancellationToken ct)
    {
        // Column defaults are also executable defaults, even when the table initially has no rows.
        // Build a qualification-only projection; never write it into a user's form or published schema.
        static JsonNode? Property(JsonObject value,string name)=>value.FirstOrDefault(p=>string.Equals(p.Key,name,StringComparison.OrdinalIgnoreCase)).Value;
        var defaults=JsonNode.Parse(CP6.Core.Services.Oa.PersonalFormValuesV2.Initial(text))!.AsObject();
        var root=JsonNode.Parse(text)!.AsObject();
        foreach(var item in Property(root,"fields")!.AsArray())
        {
            var field=item!.AsObject();if(!string.Equals(Property(field,"type")?.GetValue<string>(),"table",StringComparison.OrdinalIgnoreCase))continue;
            var row=new JsonObject();
            foreach(var itemColumn in Property(field,"columns")!.AsArray())
            {
                var column=itemColumn!.AsObject();var value=Property(column,"default");
                if(value!=null)row[Property(column,"name")!.GetValue<string>()]=value.DeepClone();
            }
            if(row.Count==0)continue;var name=Property(field,"name")!.GetValue<string>();
            if(defaults[name] is not JsonArray rows){rows=new JsonArray();defaults[name]=rows;}
            rows.Add(row);
        }
        return Check(schema,defaults.ToJsonString(),null,true,ct);
    }
    private static IEnumerable<(int Index,JsonElement Value)> Values(JsonElement data,SourceField field)
    {
        if(!data.TryGetProperty(field.Field,out var value))yield break;
        if(field.Column==null){yield return (0,value);yield break;}
        if(value.ValueKind==JsonValueKind.Array)
            foreach(var (row,index) in value.EnumerateArray().Select((row,index)=>(row,index)))
                if(row.TryGetProperty(field.Column,out var item))yield return (index,item);
    }
    private static string[] Selected(JsonElement value,SourceField field)
    {
        var result=new List<string>();
            if(value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null||value.ValueKind==JsonValueKind.String&&value.GetString()=="")return [];
            if(field.Type.Equals("checkbox",StringComparison.OrdinalIgnoreCase))
            {
                if(value.ValueKind!=JsonValueKind.Array)throw new ScopeAuthorizationExceptionV1("FORM_OPTION_VALUE_UNAVAILABLE",422);
                foreach(var item in value.EnumerateArray())
                    if(item.ValueKind==JsonValueKind.String)result.Add(item.GetString()!);else throw new ScopeAuthorizationExceptionV1("FORM_OPTION_VALUE_UNAVAILABLE",422);
            }
            else if(value.ValueKind==JsonValueKind.String)result.Add(value.GetString()!);
            else throw new ScopeAuthorizationExceptionV1("FORM_OPTION_VALUE_UNAVAILABLE",422);
        return result.ToArray();
    }
    internal async Task Check(FormSchema schema,string data,string? before,bool requireAll,CancellationToken ct)
    {
        var fields=Fields(schema).ToArray();if(fields.Length==0)return;
        using var current=JsonDocument.Parse(data);using var original=JsonDocument.Parse(before??"{}");
        var sources=new Dictionary<FormOptionSourceV1,HashSet<string>>();
        foreach(var field in fields)
        {
            var prior=Values(original.RootElement,field).ToDictionary(v=>v.Index,v=>v.Value);var selected=new List<string>();
            foreach(var (index,value) in Values(current.RootElement,field))
            {
                var hasPrior=prior.TryGetValue(index,out var old);
                if(!requireAll&&hasPrior&&value.GetRawText()==old.GetRawText())continue;
                IEnumerable<string> values=Selected(value,field);
                if(!requireAll&&hasPrior)
                {
                    // A rebase can retain an invalid old representation. Only unchanged string identities
                    // in this same cell may survive an ordinary save; copying into another row is a new value.
                    IEnumerable<string> retained=old.ValueKind==JsonValueKind.Array?old.EnumerateArray().Where(v=>v.ValueKind==JsonValueKind.String).Select(v=>v.GetString()!)
                        :old.ValueKind==JsonValueKind.String?[old.GetString()!]:Enumerable.Empty<string>();
                    values=values.Except(retained,StringComparer.Ordinal);
                }
                selected.AddRange(values);
            }
            if(!requireAll&&selected.Count==0)continue;
            if(!sources.TryGetValue(field.Source,out var permitted))
                sources[field.Source]=permitted=(await Source(field.Source,ct)).Select(v=>v.Value).ToHashSet(StringComparer.Ordinal);
            if(selected.Any(v=>!permitted.Contains(v)))throw new ScopeAuthorizationExceptionV1("FORM_OPTION_VALUE_UNAVAILABLE",422);
        }
    }
}
