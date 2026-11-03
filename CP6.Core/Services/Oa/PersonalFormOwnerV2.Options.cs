using System.Text.Json.Serialization;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormOptionContextV1(string Kind,Guid FormVersionId,string? Key=null,Guid? Id=null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormOptionQueryV1(FormOptionContextV1 Context,string[] FieldPath,string Search="",int Page=1);

public sealed partial class PersonalFormOwnerV2
{
    private Task CheckOptions(FormSchema schema,string data,string? before,bool requireAll,CancellationToken ct)
        =>new SharedDictionaryOptionsV1(db).Check(schema,data,before,requireAll,ct);

    public async Task<FormOptionPageV1> OptionsAsync(FormOptionQueryV1 input,CancellationToken ct=default)
    {
        if(input.Context==null||input.Context.FormVersionId==Guid.Empty||input.FieldPath==null||input.FieldPath.Length is <1 or >2
            ||input.FieldPath.Any(string.IsNullOrEmpty)||input.Search==null||input.Search.Length>100||input.Page is <1 or >100000)
            throw Error("FORM_OPTION_QUERY_INVALID",400);
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var lease=await ReadLease(ct);var actor=await Actor(ct);
        var context=input.Context;FormPublicationV2 form;Dictionary<string,string> mask;
        if(context.Kind=="FORM"&&context.Id==null&&!string.IsNullOrEmpty(context.Key))
        {
            await Action(actor,"query",ct);var source=await Resolve(actor,context.Key,false,ct);form=source.Form;mask=source.Mask;
        }
        else if(context.Kind=="DRAFT"&&context.Id is Guid draft&&context.Key==null)
        {
            await Action(actor,"query",ct);var row=await Owned(draft,actor.UserId,ct);Active(row);
            form=await Publication(row.Head,row.Version,ct);mask=await Mask(actor,row.Head,form,ct);
        }
        else if(context.Kind=="RETURNED"&&context.Id is Guid instance&&context.Key==null)
        {
            var source=await ReturnedSourceAsync(instance,ct);mask=await ReturnedAccess(source,actor,ct,"returned-edit");
            if((await db.Wf_FlowInstances.AsNoTracking().SingleAsync(i=>i.Id==instance,ct)).Status!=FlowInstanceStatus.Draft)throw Error("ALREADY_RESUBMITTED",409);
            form=await Publication(source.Head,source.FormVersion,ct);
        }
        else throw Error("FORM_OPTION_QUERY_INVALID",400);
        if(form.VersionId!=context.FormVersionId)throw Error("FORM_CONTEXT_CHANGED",409);
        if(mask.GetValueOrDefault(input.FieldPath[0])!="edit")throw Error("FIELD_OPTION_FORBIDDEN",403);
        var field=SharedDictionaryOptionsV1.Fields(PersonalFormValuesV2.Schema(form.Fields["SchemaJson"]!)).SingleOrDefault(f=>f.Field==input.FieldPath[0]
            &&f.Column==(input.FieldPath.Length==2?input.FieldPath[1]:null));
        if(field==null)throw Error("FIELD_OPTION_FORBIDDEN",403);
        var result=await new SharedDictionaryOptionsV1(db).Options(field.Source,input.Search,input.Page,ct);
        await Final(actor,lease,ct,false);return result;
    }
}
