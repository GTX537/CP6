using System.Text.Json;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;
public sealed record PersonalFormReferenceV2(Guid Id,string Name);

public sealed partial class PersonalFormOwnerV2
{
    // The personal-form policy grants selection of active references in this tenant.
    // The shared WF_AUTH fence holds organization/user changes until this decision completes.
    private async Task References(FormSchema schema,string data,CancellationToken ct)
    {
        using var document=JsonDocument.Parse(data);
        foreach(var field in schema.Fields.Where(f=>f.Type.Equals("user",StringComparison.OrdinalIgnoreCase)||f.Type.Equals("dept",StringComparison.OrdinalIgnoreCase)))
        {
            if(!document.RootElement.TryGetProperty(field.Name,out var value)||value.ValueKind==JsonValueKind.Null)continue;
            var items=value.ValueKind==JsonValueKind.Array?value.EnumerateArray().ToArray():[value];
            var ids=new HashSet<Guid>();
            foreach(var item in items)
            {
                var text=item.GetString();if(text=="")continue;
                if(!Guid.TryParseExact(text,"D",out var id)||id==Guid.Empty)throw Error("FORM_REFERENCE_INVALID",422);
                ids.Add(id);
            }
            if(ids.Count==0)continue;
            var at=Clock().LocalDateTime;
            var count=field.Type.Equals("user",StringComparison.OrdinalIgnoreCase)
                ?await db.Sys_Users.CountAsync(u=>ids.Contains(u.Id)&&u.Enable&&(u.LockedUntil==null||u.LockedUntil<=at),ct)
                :await PostgreSqlOrganizationV1.Healthy(db).CountAsync(d=>ids.Contains(d.Id),ct);
            if(count!=ids.Count)throw Error("FORM_REFERENCE_INVALID",422);
        }
    }
    public async Task<PersonalFormReferenceV2[]> ReferencesAsync(string key,string field,string search,Guid? draft=null,CancellationToken ct=default)
    {
        if(search.Length>100)throw Error("SEARCH_INVALID",400);
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var lease=await ReadLease(ct);var actor=await Actor(ct);await Action(actor,"query",ct);
        FormPublicationV2 publication;Dictionary<string,string> mask;
        if(draft is Guid id)
        {
            var row=await Owned(id,actor.UserId,ct);Active(row);publication=await Publication(row.Head,row.Version,ct);mask=await Mask(actor,row.Head,publication,ct);
            if(publication.Key!=key)throw Error("FORM_NOT_FOUND",404);
        }
        else{var source=await Resolve(actor,key,false,ct);publication=source.Form;mask=source.Mask;}
        var definition=PersonalFormValuesV2.Schema(publication.Fields["SchemaJson"]!).Fields.SingleOrDefault(f=>f.Name==field);
        if(definition==null||mask[field]!="edit")throw Error("FIELD_REFERENCE_FORBIDDEN",403);
        PersonalFormReferenceV2[] values;var at=Clock().LocalDateTime;
        if(definition.Type.Equals("user",StringComparison.OrdinalIgnoreCase))
            values=await db.Sys_Users.AsNoTracking().Where(u=>u.Enable&&(u.LockedUntil==null||u.LockedUntil<=at)
                && (search==""||u.UserName.Contains(search)||u.NickName!=null&&u.NickName.Contains(search)))
                .OrderBy(u=>u.UserName).ThenBy(u=>u.Id).Take(20).Select(u=>new PersonalFormReferenceV2(u.Id,u.NickName??u.UserName)).ToArrayAsync(ct);
        else if(definition.Type.Equals("dept",StringComparison.OrdinalIgnoreCase))
            values=await PostgreSqlOrganizationV1.Healthy(db).Where(d=>search==""||d.DeptName.Contains(search)||d.DeptCode.Contains(search))
                .OrderBy(d=>d.DeptCode).ThenBy(d=>d.Id).Take(20).Select(d=>new PersonalFormReferenceV2(d.Id,d.DeptName)).ToArrayAsync(ct);
        else throw Error("FIELD_REFERENCE_FORBIDDEN",403);
        await Final(actor,lease,ct,false);return values;
    }
    public Task<PersonalFormPreviewV2> PreviewAsync(string key,PersonalFormPreviewInputV2 input,CancellationToken ct=default)=>Preview(key,null,input,ct);
    public Task<PersonalFormPreviewV2> DraftPreviewAsync(Guid draft,PersonalFormPreviewInputV2 input,CancellationToken ct=default)=>Preview(null,draft,input,ct);
    private async Task<PersonalFormPreviewV2> Preview(string? key,Guid? draft,PersonalFormPreviewInputV2 input,CancellationToken ct)
    {
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var lease=await ReadLease(ct);
        var actor=await Actor(ct);await Action(actor,"query",ct);
        FormPublicationV2 form;Dictionary<string,string> mask;string original;
        if(draft is Guid id)
        {
            var row=await Owned(id,actor.UserId,ct);Active(row);form=await Publication(row.Head,row.Version,ct);
            mask=await Mask(actor,row.Head,form,ct);original=row.Data;
        }
        else
        {
            var source=await Resolve(actor,key!,false,ct);form=source.Form;mask=source.Mask;
            original=PersonalFormValuesV2.Initial(form.Fields["SchemaJson"]!);
        }
        if(input.FormVersionId!=form.VersionId)throw Error("FORM_CONTEXT_CHANGED",409);
        var schema=PersonalFormValuesV2.Schema(form.Fields["SchemaJson"]!);
        var merged=PersonalFormValuesV2.Merge(schema,input.Data,original,mask);
        var computed=PersonalFormValuesV2.Recompute(schema,merged);await References(schema,computed.Data,ct);
        await Final(actor,lease,ct,false);
        return new(PersonalFormValuesV2.Project(computed.Data,mask),computed.Hidden.Where(x=>mask[x]!="hidden").ToArray(),
            computed.Errors.Length==0,computed.Errors.Length==0?[]:["FORM_VALIDATION_FAILED"]);
    }
}
