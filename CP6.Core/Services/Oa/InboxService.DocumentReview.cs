using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;
public partial class InboxService
{
    private sealed record NativeDocumentRouteSource(Guid Instance,Guid Submission,string Owner,byte[] Payload,string Digest);
    private async Task<Dictionary<Guid,string>> NativeDocumentRoutes(Guid[] instances)
    {
        var result=new Dictionary<Guid,string>();
        if(instances.Length==0)return result;
        if(!_db.Database.IsNpgsql())throw new ScopeAuthorizationExceptionV1("DOCUMENT_TASK_SOURCE_UNAVAILABLE");
        var rows=await _db.Database.SqlQuery<NativeDocumentRouteSource>($"""
            SELECT s.instance_id AS "Instance",s.submission_id AS "Submission",s.owner AS "Owner",i.payload AS "Payload",i.payload_digest AS "Digest"
            FROM cp6_approval_start_v2.starts s JOIN cp6_approval_intake_v2.submissions i
              ON i.environment_id=s.environment_id AND i.tenant_id=s.tenant_id AND i.id=s.submission_id AND i.owner=s.owner
            WHERE s.tenant_id={_db.CurrentTenantId} AND s.instance_id=ANY({instances})
              AND s.environment_id=(SELECT environment_id FROM cp6_scope_authorization_v1.environment WHERE singleton)
            """).ToArrayAsync();
        foreach(var row in rows)
        {
            if(row.Owner!=ApprovalBusinessProfileV2.DocumentOwner)continue;
            var original=ApprovalSubmissionWireV2.Parse(row.Payload);var value=original.Value;
            if(original.Full!=row.Digest || !original.Canonical.AsSpan().SequenceEqual(row.Payload)
                || value.SubmissionId!=row.Submission || value.Scope.TenantId!=_db.CurrentTenantId
                || value.Subject.BusinessOwner!=ApprovalBusinessProfileV2.DocumentOwner
                || !Guid.TryParse(value.Subject.SubjectId,out var document))
                throw new ScopeAuthorizationExceptionV1("DOCUMENT_TASK_SOURCE_UNAVAILABLE");
            result.Add(row.Instance,$"/plm/controlled-document/{document}?submission={row.Submission}");
        }
        if(rows.Length!=instances.Length)throw new ScopeAuthorizationExceptionV1("DOCUMENT_TASK_SOURCE_UNAVAILABLE");
        return result;
    }
}
