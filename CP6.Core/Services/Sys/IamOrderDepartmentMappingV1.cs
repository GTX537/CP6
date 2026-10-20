using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Sys.IamAdministrationV1;

namespace CP6.Core.Services.Sys;

public sealed record IamOrderDepartmentCommandV1 : IamCommandV1
{
    public Guid? Id { get; init; }
    public string Code { get; init; } = "";
    public Guid DepartmentId { get; init; }
    public bool Enabled { get; init; } = true;
}
public sealed record IamOrderDepartmentViewV1(Guid Id, string Code, Guid DepartmentId, bool Enabled,
    string Version, bool Available, int OrderCount);
public sealed record IamOrderDepartmentChoiceV1(Guid Id, string Name, bool Available);
public sealed record IamOrderDepartmentEditorV1(Guid TenantId, Guid ActorId, string ReferencesVersion,
    IamOrderDepartmentViewV1[] Mappings, IamOrderDepartmentChoiceV1[] Departments);

/// <summary>Explicit Main ERP identity binding. Never infer it from equal codes or backfill existing orders.</summary>
public sealed class IamOrderDepartmentMappingV1(CP6Context db, IamAdministrationV1 authority)
{
    private const string Kind = "dept-order-mapping";
    public Task<IamOrderDepartmentEditorV1> ReadAsync(Func<CancellationToken, Task<Guid>> actor, CancellationToken ct)
        => authority.ExecuteAsync<IamOrderDepartmentEditorV1>("pub-dept:query", actor, async (who, token) =>
        {
            await RequireAction("query", token);
            var all = await db.Sys_Depts.AsNoTracking().ToDictionaryAsync(d => d.Id, token);
            var rows = await Rows().ToArrayAsync(token);
            var views = new List<IamOrderDepartmentViewV1>();
            foreach (var row in rows.OrderBy(x => x.Code, StringComparer.Ordinal))
            {
                var count = await db.Orders.AsNoTracking().CountAsync(o => o.OrderDepartment == row.Code && !o.IsDeleted, token);
                views.Add(new(row.Id, row.Code, row.DepartmentId, row.Enabled, row.Revision.ToString("D"),
                    row.Enabled && IamUserAdministrationV1.DepartmentAvailable(row.DepartmentId, all), count));
            }
            await RequireActor(actor, who, token);
            return new(db.CurrentTenantId, who, await authority.ReferencesVersion(token), views.ToArray(),
                all.Values.OrderBy(d => d.Sort).ThenBy(d => d.Id).Select(d =>
                    new IamOrderDepartmentChoiceV1(d.Id, d.DeptName,
                        IamUserAdministrationV1.DepartmentAvailable(d.Id, all))).ToArray());
        }, ct);

    public Task<IamOperationV1> ChangeAsync(IamOrderDepartmentCommandV1 command,
        Func<CancellationToken, Task<Guid>> actor, CancellationToken ct)
    {
        Validate(command);
        if (string.IsNullOrWhiteSpace(command.Code) || command.Code.Length > 10 || command.DepartmentId == Guid.Empty
            || command.Id == Guid.Empty) throw Error(400, "ORDER_DEPARTMENT_MAPPING_INPUT_INVALID");
        return authority.ExecuteAsync("pub-dept:edit", actor, async (who, token) =>
        {
            await RequireAction("edit", token);
            var original = await authority.Original(who, Kind, command, token);
            if (original != null) { await RequireActor(actor, who, token); return original; }
            var row = command.Id == null ? null : await Rows().SingleOrDefaultAsync(x => x.Id == command.Id, token);
            if (command.Id != null && row == null) throw Error(404, "ORDER_DEPARTMENT_MAPPING_NOT_FOUND");
            if ((row?.Revision.ToString("D") ?? "absent") != command.ExpectedVersion
                || await authority.ReferencesVersion(token) != command.ExpectedReferencesVersion)
                throw Error(412, "IAM_REFERENCES_CHANGED");
            if (row != null && row.Code != command.Code) throw Error(409, "ORDER_DEPARTMENT_CODE_IMMUTABLE");
            var all = await db.Sys_Depts.AsNoTracking().ToDictionaryAsync(d => d.Id, token);
            if (!all.ContainsKey(command.DepartmentId)
                || command.Enabled && !IamUserAdministrationV1.DepartmentAvailable(command.DepartmentId, all))
                throw Error(409, "IAM_DEPARTMENT_UNAVAILABLE");
            if (await db.Database.SqlQuery<int>($"""
                SELECT COUNT(*)::integer AS "Value" FROM cp6_order_owner_v1.department_mappings
                WHERE tenant_id={db.CurrentTenantId} AND code=CAST({command.Code} AS bpchar)
                  AND id<>{command.Id ?? Guid.Empty}
                """).SingleAsync(token) != 0) throw Error(409, "ORDER_DEPARTMENT_CODE_CONFLICT");
            var id = row?.Id ?? Guid.NewGuid();
            if (row == null)
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO cp6_order_owner_v1.department_mappings(id,tenant_id,code,department_id,enabled)
                    VALUES({id},{db.CurrentTenantId},CAST({command.Code} AS bpchar),{command.DepartmentId},{command.Enabled})
                    """, token);
            else
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE cp6_order_owner_v1.department_mappings SET department_id={command.DepartmentId},enabled={command.Enabled}
                    WHERE id={id} AND tenant_id={db.CurrentTenantId}
                    """, token);
            db.Sys_SecurityLogs.Add(new Sys_SecurityLog { TenantId = db.CurrentTenantId, UserId = who,
                EventType = (int)SecurityEventType.IamAdministrationChanged, CreatedAt = DateTime.Now, Reason = command.Reason,
                ContextJson = JsonSerializer.Serialize(new { operationId = command.OperationId, kind = Kind, mappingId = id,
                    code = command.Code, previousDepartmentId = row?.DepartmentId, departmentId = command.DepartmentId,
                    previousEnabled = row?.Enabled, enabled = command.Enabled }) });
            await db.SaveChangesAsync(token);
            var saved = await Rows().SingleAsync(x => x.Id == id, token);
            var result = new IamOperationV1(command.OperationId, Kind, "COMMITTED", id, saved.Revision.ToString("D"),
                await authority.ReferencesVersion(token));
            await authority.Store(who, command, result, token);
            await RequireActor(actor, who, token);
            return result;
        }, ct);
    }

    private static async Task RequireActor(Func<CancellationToken, Task<Guid>> actor, Guid expected, CancellationToken ct)
    { if (await actor(ct) != expected) throw Error(401, "REAUTH_REQUIRED"); }
    private async Task RequireAction(string action, CancellationToken ct)
    {
        var rows = await (from a in db.Sys_MenuActions.AsNoTracking()
                          join m in db.Sys_Menus.AsNoTracking() on a.MenuId equals m.MenuId
                          where m.Enable && m.MenuKey == "pub-dept"
                          select new { m.MenuKey, a.ActionCode }).ToArrayAsync(ct);
        if (!rows.Any(x => x.MenuKey == "pub-dept" && x.ActionCode == action)) throw Error(403, "IAM_FORBIDDEN");
    }
    private IQueryable<Row> Rows() => db.Database.SqlQuery<Row>($"""
        SELECT id AS "Id",convert_from(pg_catalog.bpcharsend(code),'UTF8') AS "Code",
          department_id AS "DepartmentId",enabled AS "Enabled",revision AS "Revision"
        FROM cp6_order_owner_v1.department_mappings WHERE tenant_id={db.CurrentTenantId}
        """);
    private sealed class Row
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = "";
        public Guid DepartmentId { get; set; }
        public bool Enabled { get; set; }
        public Guid Revision { get; set; }
    }
}
