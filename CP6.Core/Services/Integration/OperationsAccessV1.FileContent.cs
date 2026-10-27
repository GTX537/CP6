using CP6.Core.Services.Erp;
using CP6.Core.Services.Pub;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Integration;

internal sealed partial class OperationsAccessV1
{
    internal void RequireFileFields(bool write)
    {
        if (authority.Resource != OperationsOwnerV1.FileContent) throw new OrderPriceCorrectionException("ACTION_FORBIDDEN", 403);
        foreach (var name in new[] { "Content", "FileName" })
            if (!authority.Fields.Fields.TryGetValue(name, out var value) || !value.CanRead || write && !value.CanWrite)
                throw new OrderPriceCorrectionException(write ? "FIELD_WRITE_FORBIDDEN" : "FIELD_READ_FORBIDDEN", 403);
    }
    internal IQueryable<FileContentRowV1> FileContents()
    {
        RequireFileFields(false);
        if (authority.Source.TenantId != db.CurrentTenantId || authority.Action is not ("query" or "add" or "edit")) throw new OrderPriceCorrectionException("ACTION_FORBIDDEN", 403);
        var rows = FileContentStoreV1.Rows(db);
        if (authority.Scope == 1) return rows.Where(x => x.CreatorId == ActorId);
        if (authority.Scope == 5) return rows;
        var allowed = StandaloneDepartmentScopeV1.Objects(db, authority, "plm-engineering-revision");
        return rows.Where(x => allowed.Contains(x.ParentId));
    }
    internal async Task RequireFileContent(Guid id, CancellationToken ct)
    { if (!await FileContents().AnyAsync(x => x.Id == id, ct)) throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN", 403); }
    internal async Task RequireFileCreation(Guid parent, CancellationToken ct)
    {
        RequireFileFields(true);
        if (authority.Action != "add" || parent == Guid.Empty) throw new OrderPriceCorrectionException("ACTION_FORBIDDEN", 403);
        if (authority.Scope is 1 or 5) return;
        if (!await StandaloneDepartmentScopeV1.Objects(db, authority, "plm-engineering-revision").AnyAsync(x => x == parent, ct))
            throw new OrderPriceCorrectionException("CREATION_DEPARTMENT_FORBIDDEN", 403);
    }
}
