using CP6.Core.Services.Erp;
using CP6.Core.Services.Plm;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Integration;

internal sealed partial class OperationsAccessV1
{
    internal void RequireDocumentFields(bool write)
    {
        if (authority.Resource != OperationsOwnerV1.ControlledDocument) throw new OrderPriceCorrectionException("ACTION_FORBIDDEN",403);
        foreach (var field in new[] { "Metadata", "Content" })
            if (!authority.Fields.Fields.TryGetValue(field,out var value) || !value.CanRead || write && !value.CanWrite)
                throw new OrderPriceCorrectionException(write ? "FIELD_WRITE_FORBIDDEN" : "FIELD_READ_FORBIDDEN",403);
    }
    internal IQueryable<ControlledDocumentHeadV1> ControlledDocuments()
    {
        if (authority.Source.TenantId != db.CurrentTenantId || authority.Resource != OperationsOwnerV1.ControlledDocument
            || authority.Action is not ("query" or "add" or "edit")) throw new OrderPriceCorrectionException("ACTION_FORBIDDEN", 403);
        RequireDocumentFields(false); var rows = ControlledDocumentStoreV1.Heads(db);
        if (authority.Scope == 1) return rows.Where(x => x.CreatorId == ActorId);
        if (authority.Scope == 5) return rows;
        // The immutable parent revision is the trusted department source. No guessed creator department or duplicate mapping.
        var allowed = StandaloneDepartmentScopeV1.Objects(db, authority, "plm-engineering-revision");
        return rows.Where(x => allowed.Contains(x.RevisionId));
    }
    internal async Task RequireControlledDocument(Guid id, CancellationToken ct)
    { if (!await ControlledDocuments().AnyAsync(x => x.Id == id, ct)) throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN", 403); }
    internal async Task RequireControlledDocumentCreation(Guid parent, CancellationToken ct)
    {
        if (authority.Resource != OperationsOwnerV1.ControlledDocument || authority.Action != "add" || parent == Guid.Empty)
            throw new OrderPriceCorrectionException("ACTION_FORBIDDEN", 403);
        RequireDocumentFields(true); if (authority.Scope is 1 or 5) return;
        if (!await StandaloneDepartmentScopeV1.Objects(db, authority, "plm-engineering-revision").AnyAsync(x => x == parent, ct))
            throw new OrderPriceCorrectionException("CREATION_DEPARTMENT_FORBIDDEN", 403);
    }
}
