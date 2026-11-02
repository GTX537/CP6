using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public sealed record NativeFormContentV2(Guid FormDataId, Guid FormVersionId, string? Title,
    string SchemaJson, string DataJson, Dictionary<string,string> FieldMask, string DataSource = "ROOT_SUBMISSION");

public sealed partial class PersonalFormOwnerV2
{
    private sealed record ReadSubmission(Guid Data,Guid Head,Guid Version,string? Title,string DataJson);
    // Participant in Inbox's existing transaction and current authorization fence.
    // The root submission owns the data; a child's FORM pin is a separate definition.
    private static Dictionary<string,string> IntersectMasks(Dictionary<string,string> actual,Dictionary<string,string> principal)
        =>actual.ToDictionary(x=>x.Key,x=>x.Value=="hidden"||principal[x.Key]=="hidden"?"hidden":x.Value=="readonly"||principal[x.Key]=="readonly"?"readonly":"edit",StringComparer.Ordinal);
    internal async Task<NativeFormContentV2> ReadSubmissionAsync(Guid root,UserPermissionContext actor,CancellationToken ct,UserPermissionContext? principal=null)
    {
        var source=await db.Database.SqlQuery<ReadSubmission>($"""
            SELECT s.form_data_id AS "Data",s.head_id AS "Head",s.form_version AS "Version",s.title AS "Title",f."DataJson"
            FROM cp6_personal_form_v2.submissions s JOIN public."Wf_FormData" f ON f."Id"=s.form_data_id AND f."TenantId"=s.tenant_id
            WHERE s.root_id={root} AND s.tenant_id={db.CurrentTenantId} AND s.environment_id={fence.EnvironmentId}
            """).SingleOrDefaultAsync(ct)??throw Error("RESOURCE_NOT_FOUND",404);
        var form=await Publication(source.Head,source.Version,ct);var mask=await Mask(actor,source.Head,form,ct);
        if(principal!=null)mask=IntersectMasks(mask,await Mask(principal,source.Head,form,ct));
        return new(source.Data,source.Version,TitleReadable(actor)&&(principal==null||TitleReadable(principal))?source.Title:null,
            ProjectSchema(form.Fields["SchemaJson"]!,mask),PersonalFormValuesV2.Project(source.DataJson,mask),mask);
    }
}
