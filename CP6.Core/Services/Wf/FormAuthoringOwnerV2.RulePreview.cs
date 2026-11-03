using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.Services.Oa;
using CP6.Entity.Contracts.Wire;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormRulePreviewInputV2(Guid DraftVersionId,string ExpectedVersion,string DataJson);
public sealed record FormRuleEffectV2(string Path,string Action,string Target,string? ValueJson);
public sealed record FormRuleFieldV2(string Name,bool Visible,bool Required,bool Disabled,string? OptionsJson);
public sealed record FormRulePreviewV2(string Mode,Guid DefinitionId,Guid DraftVersionId,string ExpectedVersion,
    string DataJson,string[] ValidationErrors,FormRuleFieldV2[] Presentation,FormRuleEffectV2[] AppliedEffects,string[] Unproven);

public sealed partial class FormAuthoringOwnerV2
{
    private static FormRulePreviewInputV2 RuleInput(byte[] raw)
    {
        try
        {
            using var doc=WorkflowCommandJsonV1.Parse(raw);
            var input=doc.Deserialize<FormRulePreviewInputV2>(Json)??throw new JsonException();
            if(input.DraftVersionId==Guid.Empty||!Guid.TryParseExact(input.ExpectedVersion,"D",out _)||input.DataJson==null)throw new JsonException();
            using var data=WorkflowCommandJsonV1.Parse(Encoding.UTF8.GetBytes(input.DataJson));
            if(data.RootElement.ValueKind!=JsonValueKind.Object)throw new JsonException();
            return input;
        }
        catch(JsonException){throw Error("RULE_PREVIEW_INPUT_INVALID",400);}
    }
    public async Task<FormRulePreviewV2> PreviewRulesAsync(string key,byte[] raw,CancellationToken ct=default)
    {
        var input=RuleInput(raw);
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);await Root(ct);var actor=await Actor(ct);
        var head=await Heads().SingleOrDefaultAsync(x=>x.Key==key,ct)??throw Error("DEFINITION_NOT_FOUND",404);
        var fields=await Permission(actor,head,false,false,ct);
        if(!fields.Fields["SchemaJson"].CanRead)throw Error("FIELD_READ_FORBIDDEN",403);
        if(head.Status!=0)throw Error("DRAFT_REQUIRED",409);
        if(head.DraftId!=input.DraftVersionId||head.Version.ToString("D")!=input.ExpectedVersion)throw Error("DEFINITION_CHANGED",409);
        var schema=PersonalFormValuesV2.Schema(head.SchemaJson);
        using var data=JsonDocument.Parse(input.DataJson);PersonalFormValuesV2.Validate(schema,data.RootElement,partial:true);
        using var source=JsonDocument.Parse(head.SchemaJson);
        static JsonElement Property(JsonElement obj,string name)=>obj.EnumerateObject().FirstOrDefault(p=>p.Name.Equals(name,StringComparison.OrdinalIgnoreCase)).Value;
        var sourceFields=Property(source.RootElement,"fields").EnumerateArray().ToArray();
        var sourceRules=Property(source.RootElement,"rules");
        var presentation=schema.Fields.Select((f,i)=>new FormRuleFieldV2(f.Name,true,f.Required,false,
            Property(sourceFields[i],"options") is var options&&options.ValueKind!=JsonValueKind.Undefined?options.GetRawText():null)).ToDictionary(f=>f.Name,StringComparer.Ordinal);
        var effects=new List<FormRuleEffectV2>();
        var result=PersonalFormValuesV2.Recompute(schema,input.DataJson,observedEffect:(r,e,effect,values)=>
        {
            var field=presentation[effect.Target];string? value=null;
            switch(effect.Action)
            {
                case "show":field=field with{Visible=true};break;
                case "hide":field=field with{Visible=false};break;
                case "require":field=field with{Required=true};break;
                case "optional":field=field with{Required=false};break;
                case "disable":field=field with{Disabled=true};break;
                case "enable":field=field with{Disabled=false};break;
                case "setOptions":value=Property(Property(sourceRules[r],"then")[e],"options").GetRawText();field=field with{OptionsJson=value};break;
                case "compute":value=values[effect.Target]?.ToJsonString()??"null";break;
            }
            presentation[effect.Target]=field;effects.Add(new($"$.rules[{r}].then[{e}]",effect.Action,effect.Target,value));
        });
        CompletionClock();
        // This is a pure explanation of supplied synthetic values. No publication
        // preview, command, business data, task or external provider is created/read.
        return new("EXPLANATION_ONLY",head.Id,head.DraftId,input.ExpectedVersion,result.Data,result.Errors,presentation.Values.ToArray(),effects.ToArray(),
            ["CURRENT_SUBMISSION_FIELD_AUTHORITY_NOT_EVALUATED","EXTERNAL_FILE_AND_OPTION_ADMISSION_NOT_EVALUATED"]);
    }
}
