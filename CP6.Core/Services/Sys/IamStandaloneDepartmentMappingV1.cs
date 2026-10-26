using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Sys.IamAdministrationV1;

namespace CP6.Core.Services.Sys;

public sealed record IamStandaloneDepartmentCommandV1 : IamCommandV1
{
    public Guid? Id { get; init; }
    public string ObjectKind { get; init; } = "";
    public Guid ObjectId { get; init; }
    public Guid DepartmentId { get; init; }
    public bool Enabled { get; init; } = true;
}
public sealed record IamStandaloneDepartmentViewV1(Guid Id, string ObjectKind, Guid ObjectId, Guid DepartmentId, bool Enabled,
    string Version, bool Available, string? ObjectName);
public sealed record IamStandaloneDepartmentChoiceV1(Guid Id, string Name, bool Available);
public sealed record IamStandaloneDepartmentEditorV1(Guid TenantId, Guid ActorId, string ReferencesVersion,
    IamStandaloneDepartmentViewV1[] Mappings, IamStandaloneDepartmentChoiceV1[] Departments);

/// <summary>Explicit existing object binding. No current-actor inference, string identity conversion or backfill.</summary>
public sealed class IamStandaloneDepartmentMappingV1(CP6Context db, IamAdministrationV1 authority)
{
    private const string Kind = "dept-standalone-mapping";
    public Task<IamStandaloneDepartmentEditorV1> ReadAsync(Func<CancellationToken, Task<Guid>> actor, CancellationToken ct)
        => authority.ExecuteAsync<IamStandaloneDepartmentEditorV1>("pub-dept:query", actor, async (who, token) =>
        {
            await RequireAction("query", token);
            var all = await db.Sys_Depts.AsNoTracking().ToDictionaryAsync(d => d.Id, token);
            var rows = await Rows().ToArrayAsync(token);
            var views = new List<IamStandaloneDepartmentViewV1>();
            foreach (var row in rows.OrderBy(x => x.ObjectKind, StringComparer.Ordinal).ThenBy(x => x.ObjectId))
            {
                var target = await Objects(row.ObjectKind).SingleOrDefaultAsync(x => x.Id == row.ObjectId, token);
                views.Add(new(row.Id, row.ObjectKind, row.ObjectId, row.DepartmentId, row.Enabled, row.Revision.ToString("D"),
                    row.Enabled && target != null && IamUserAdministrationV1.DepartmentAvailable(row.DepartmentId, all), target?.Name));
            }
            await RequireActor(actor, who, token);
            return new(db.CurrentTenantId, who, await authority.ReferencesVersion(token), views.ToArray(),
                all.Values.OrderBy(d => d.Sort).ThenBy(d => d.Id).Select(d =>
                    new IamStandaloneDepartmentChoiceV1(d.Id, d.DeptName,
                        IamUserAdministrationV1.DepartmentAvailable(d.Id, all))).ToArray());
        }, ct);

    public Task<IamOperationV1> ChangeAsync(IamStandaloneDepartmentCommandV1 command,
        Func<CancellationToken, Task<Guid>> actor, CancellationToken ct)
    {
        Validate(command);
        if (!Kinds.Contains(command.ObjectKind, StringComparer.Ordinal) || command.ObjectId == Guid.Empty
            || command.DepartmentId == Guid.Empty || command.Id == Guid.Empty) throw Error(400, "STANDALONE_DEPARTMENT_MAPPING_INPUT_INVALID");
        return authority.ExecuteAsync("pub-dept:edit", actor, async (who, token) =>
        {
            await RequireAction("edit", token);
            var original = await authority.Original(who, Kind, command, token);
            if (original != null) { await RequireActor(actor, who, token); return original; }
            var row = command.Id == null ? null : await Rows().SingleOrDefaultAsync(x => x.Id == command.Id, token);
            if (command.Id != null && row == null) throw Error(404, "STANDALONE_DEPARTMENT_MAPPING_NOT_FOUND");
            if ((row?.Revision.ToString("D") ?? "absent") != command.ExpectedVersion
                || await authority.ReferencesVersion(token) != command.ExpectedReferencesVersion)
                throw Error(412, "IAM_REFERENCES_CHANGED");
            if (row != null && (row.ObjectKind != command.ObjectKind || row.ObjectId != command.ObjectId))
                throw Error(409, "STANDALONE_OBJECT_IMMUTABLE");
            if ((row == null || command.Enabled) && !await Objects(command.ObjectKind).AnyAsync(x => x.Id == command.ObjectId, token))
                throw Error(409, "STANDALONE_OBJECT_UNAVAILABLE");
            var all = await db.Sys_Depts.AsNoTracking().ToDictionaryAsync(d => d.Id, token);
            if (!all.ContainsKey(command.DepartmentId)
                || command.Enabled && !IamUserAdministrationV1.DepartmentAvailable(command.DepartmentId, all))
                throw Error(409, "IAM_DEPARTMENT_UNAVAILABLE");
            if (await db.Database.SqlQuery<int>($"""
                SELECT COUNT(*)::integer AS "Value" FROM cp6_operations_owner_v1.department_mappings
                WHERE tenant_id={db.CurrentTenantId} AND object_kind={command.ObjectKind} AND object_id={command.ObjectId}
                  AND id<>{command.Id ?? Guid.Empty}
                """).SingleAsync(token) != 0) throw Error(409, "STANDALONE_OBJECT_CONFLICT");
            var id = row?.Id ?? Guid.NewGuid();
            if (row == null)
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO cp6_operations_owner_v1.department_mappings(id,tenant_id,object_kind,object_id,department_id,enabled)
                    VALUES({id},{db.CurrentTenantId},{command.ObjectKind},{command.ObjectId},{command.DepartmentId},{command.Enabled})
                    """, token);
            else
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE cp6_operations_owner_v1.department_mappings SET department_id={command.DepartmentId},enabled={command.Enabled}
                    WHERE id={id} AND tenant_id={db.CurrentTenantId}
                    """, token);
            db.Sys_SecurityLogs.Add(new Sys_SecurityLog { TenantId = db.CurrentTenantId, UserId = who,
                EventType = (int)SecurityEventType.IamAdministrationChanged, CreatedAt = DateTime.Now, Reason = command.Reason,
                ContextJson = JsonSerializer.Serialize(new { operationId = command.OperationId, kind = Kind, mappingId = id,
                    objectKind = command.ObjectKind, objectId = command.ObjectId, previousDepartmentId = row?.DepartmentId, departmentId = command.DepartmentId,
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

    private static readonly string[] Kinds = ["stock", "stock-transaction", "stocktake", "work-order", "machine", "replenish-order", "mobile-task", "work-center", "cross-dock", "slotting-plan", "vmi-billing", "kit-master", "kit-order", "warehouse", "location", "routing-rule", "sample-stock", "product-master", "uom-profile", "currency-policy", "plm-engineering-product", "plm-engineering-revision", "plm-configuration-rule", "plate-mold"];
    public sealed record ObjectChoice { public Guid Id { get; init; } public string Name { get; init; } = ""; }
    public Task<ObjectChoice[]> FindObjectsAsync(string kind, string search,
        Func<CancellationToken, Task<Guid>> actor, CancellationToken ct)
    {
        if (!Kinds.Contains(kind, StringComparer.Ordinal) || string.IsNullOrWhiteSpace(search) || search.Length > 100)
            throw Error(400, "STANDALONE_LOOKUP_INPUT_INVALID");
        return authority.ExecuteAsync("pub-dept:query", actor, async (who, token) =>
        {
            await RequireAction("query", token);
            var rows = await Objects(kind).Where(x => x.Name.Contains(search)).OrderBy(x => x.Id).Take(50).ToArrayAsync(token);
            await RequireActor(actor, who, token); return rows;
        }, ct);
    }
    // The IAM directory grants identity metadata only; it does not disclose quantities, prices or business details.
    private IQueryable<ObjectChoice> Objects(string kind) => kind switch
    {
        "plate-mold" => db.PlateMolds.AsNoTracking().Where(x => !x.IsDeleted).Select(x => new ObjectChoice { Id=x.Id,Name=x.BaseCd+" / "+x.WdPtnNo+" / "+x.WdRev }),
        "plm-configuration-rule" => db.Database.SqlQuery<ObjectChoice>($"""
            SELECT rule_id AS "Id",rule_id::text AS "Name" FROM cp6_configuration_rule_v1.heads WHERE tenant_id={db.CurrentTenantId}
            """),
        "plm-engineering-revision" => db.Database.SqlQuery<ObjectChoice>($"""
            SELECT revision_id AS "Id",code AS "Name" FROM cp6_engineering_revision_v1.heads WHERE tenant_id={db.CurrentTenantId}
            """),
        "plm-engineering-product" => db.Database.SqlQuery<ObjectChoice>($"""
            SELECT product_id AS "Id",code AS "Name" FROM cp6_engineering_product_v1.heads WHERE tenant_id={db.CurrentTenantId}
            """),
        "currency-policy" => db.Database.SqlQuery<ObjectChoice>($"""
            SELECT policy_id AS "Id",code AS "Name" FROM cp6_currency_policy_v1.heads WHERE tenant_id={db.CurrentTenantId}
            """),
        "uom-profile" => db.Database.SqlQuery<ObjectChoice>($"""
            SELECT profile_id AS "Id",code AS "Name" FROM cp6_uom_profile_v1.heads WHERE tenant_id={db.CurrentTenantId}
            """),
        "stock" => db.Stocks.AsNoTracking().Where(x => !x.IsDeleted).Select(x => new ObjectChoice { Id=x.Id,
            Name=x.WarehouseCd + "/" + x.LocationCd + "/" + x.ProductCd + "/" + x.LotNo }),
        "stock-transaction" => db.StockTransactions.AsNoTracking().Where(x => !x.IsDeleted).Select(x => new ObjectChoice { Id=x.Id,Name=x.TxnNo }),
        "stocktake" => db.StockTakes.AsNoTracking().Where(x => !x.IsDeleted).Select(x => new ObjectChoice { Id=x.Id,Name=x.StockTakeNo }),
        "work-order" => db.WorkOrders.AsNoTracking().Where(x => !x.IsDeleted && (x.WebOrderNo == null || x.WebOrderNo == ""))
            .Select(x => new ObjectChoice { Id=x.Id,Name=x.WorkOrderNo }),
        "product-master" => db.ProductMasters.AsNoTracking().Where(x=>!x.IsDeleted).Select(x=>new ObjectChoice {Id=x.Id,Name=x.ProductCd}),
        "sample-stock" => db.SampleStocks.AsNoTracking().Where(x=>!x.IsDeleted).Select(x=>new ObjectChoice {Id=x.Id,Name=x.SampleNo}),
        "routing-rule" => db.OutboundRoutingRules.AsNoTracking().Where(x=>!x.IsDeleted && db.Warehouses.Any(w=>!w.IsDeleted && w.WarehouseCd==x.TargetWarehouseCd)).Select(x=>new ObjectChoice {Id=x.Id,Name=x.RuleName}),
        "warehouse" => db.Warehouses.AsNoTracking().Where(x=>!x.IsDeleted).Select(x=>new ObjectChoice {Id=x.Id,Name=x.WarehouseCd}),
        "location" => db.Locations.AsNoTracking().Where(x=>!x.IsDeleted && db.Warehouses.Any(w=>!w.IsDeleted && w.WarehouseCd==x.WarehouseCd)).Select(x=>new ObjectChoice {Id=x.Id,Name=x.WarehouseCd+" / "+x.LocationCd}),
        "kit-master" => db.KitMasters.AsNoTracking().Where(x => !x.IsDeleted).Select(x => new ObjectChoice { Id = x.Id, Name = x.KitSku }),
        "kit-order" => db.KitOrders.AsNoTracking().Where(x => !x.IsDeleted).Select(x => new ObjectChoice { Id = x.Id, Name = x.KitOrderNo }),
        "vmi-billing" => db.VmiBillings.AsNoTracking().Where(x => !x.IsDeleted).Select(x => new ObjectChoice { Id = x.Id, Name = x.BillingNo }),
        "slotting-plan" => db.SlottingPlans.AsNoTracking().Where(x => !x.IsDeleted).Select(x => new ObjectChoice { Id = x.Id, Name = x.SlottingPlanNo }),
        "cross-dock" => db.CrossDockOrders.AsNoTracking().Where(x => !x.IsDeleted).Select(x => new ObjectChoice { Id = x.Id, Name = x.XDockNo }),
        "work-center" => db.WorkCenters.AsNoTracking().Where(x => !x.IsDeleted).Select(x => new ObjectChoice { Id = x.Id, Name = x.WgCd + " / " + x.WgName }),
        "mobile-task" => db.MobileTasks.AsNoTracking().Where(x => !x.IsDeleted).Select(x => new ObjectChoice { Id = x.Id, Name = x.MobileTaskNo }),
        "replenish-order" => db.ReplenishOrders.AsNoTracking().Where(x => !x.IsDeleted)
            .Select(x => new ObjectChoice { Id=x.Id,Name=x.ReplenishNo }),
        "machine" => db.Machines.AsNoTracking().Where(x => !x.IsDeleted)
            .Select(x => new ObjectChoice { Id=x.Id,Name=x.MachineCd + " / " + x.MachineName }),
        _ => throw Error(400, "STANDALONE_LOOKUP_INPUT_INVALID")
    };

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
        SELECT id AS "Id",object_kind AS "ObjectKind",object_id AS "ObjectId",
          department_id AS "DepartmentId",enabled AS "Enabled",revision AS "Revision"
        FROM cp6_operations_owner_v1.department_mappings WHERE tenant_id={db.CurrentTenantId}
        """);
    private sealed class Row
    {
        public Guid Id { get; set; }
        public string ObjectKind { get; set; } = "";
        public Guid ObjectId { get; set; }
        public Guid DepartmentId { get; set; }
        public bool Enabled { get; set; }
        public Guid Revision { get; set; }
    }
}
