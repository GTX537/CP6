using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;
public sealed partial class PersonalFormOwnerV2
{
    internal sealed record UnboundSubmission(Guid Data, Guid Head, Guid Version, string Key, int Number, Guid Actor,
        DateTimeOffset Submitted, string? Title, string DataJson, DateTimeOffset Available);
    internal IQueryable<UnboundSubmission> Unbound(Guid actor) => db.Database.SqlQuery<UnboundSubmission>($"""
        SELECT s.form_data_id AS "Data",s.head_id AS "Head",s.form_version AS "Version",f."FormKey" AS "Key",f."FormVersion" AS "Number",
          s.actor_id AS "Actor",s.submitted_at AS "Submitted",s.title AS "Title",f."DataJson",s.query_available_at AS "Available"
        FROM cp6_personal_form_v2.submissions s JOIN public."Wf_FormData" f ON f."TenantId"=s.tenant_id AND f."Id"=s.form_data_id
        WHERE s.tenant_id={db.CurrentTenantId} AND s.environment_id={fence.EnvironmentId} AND s.root_id IS NULL AND s.actor_id={actor}
        """);
    internal async Task<(NativeFormQueryItemV2 Item, NativeFormContentV2 Content)> UnboundHistory(UnboundSubmission row, UserPermissionContext actor, CancellationToken ct)
    {
        var publication = await Publication(row.Head, row.Version, ct); var mask = await Mask(actor, row.Head, publication, ct);
        var title = TitleReadable(actor) ? row.Title : null;
        return (new(row.Data, row.Data, null, row.Key, row.Number, row.Version, publication.Fields["Name"],
            null, null, null, null, title, row.Actor, row.Submitted, null, "SubmittedUnbound", null, "PINNED_PUBLICATION",
            "FORM_SUBMISSION", "/api/oa/v2/form-data/" + row.Data.ToString("D"), row.Available),
            new(row.Data, row.Version, title, ProjectSchema(publication.Fields["SchemaJson"]!, mask),
                PersonalFormValuesV2.Project(row.DataJson, mask), mask, "UNBOUND_SUBMISSION"));
    }
}
