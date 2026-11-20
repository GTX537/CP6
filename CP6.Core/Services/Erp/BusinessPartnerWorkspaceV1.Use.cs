using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Erp;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Erp;

public sealed partial class BusinessPartnerWorkspaceV1
{
    internal sealed record UsePartner(string Legal, string Code, long Revision, string Hash, string Currency,
        Dictionary<string, bool> Roles, BpUseSourceRef[] Dependencies, BpUseFinanceGroup[] FinanceGroups);
    internal sealed record UseCoordinates
    {
        public Guid Id { get; init; }
        public string Legal { get; init; } = "";
        public long Revision { get; init; }
    }

    internal static Task<UseCoordinates?> FindUseCoordinatesAsync(CP6Context db, string code, CancellationToken ct)
        => db.Database.SqlQuery<UseCoordinates>($"""
            SELECT h.bp_id AS "Id",h.legal_entity_key AS "Legal",h.revision AS "Revision" FROM main_bp.heads h
              JOIN public."T_WebBusinessPartner" p ON p."TenantId"=h.tenant_id AND p."Id"=h.bp_id
            WHERE h.tenant_id={db.CurrentTenantId} AND p."BpCd"=CAST({code} AS bpchar)
            """).SingleOrDefaultAsync(ct);

    internal static async Task<UsePartner> ReadUsePartnerAsync(CP6Context context, IDataProtectionProvider protection,
        Guid bp, string legal, string role, long expectedRevision, CancellationToken ct)
    {
        var options = await ReadOptions(context, legal, ct);
        var candidates = new Queue<(Guid Partner, string Role)>();
        var visited = new HashSet<(Guid, string)>();
        var snapshots = new Dictionary<Guid, (BusinessPartner Row, Head Version, Dictionary<string, Guid?> Slots)>();
        candidates.Enqueue((bp, role));
        while (candidates.TryDequeue(out var work))
        {
            if (!visited.Add(work)) continue;
            if (visited.Count > 4096) throw BpUseScopeGate.LockUnproven();
            if (!snapshots.TryGetValue(work.Partner, out var current))
            {
                var row = await context.BusinessPartners.AsNoTracking().SingleOrDefaultAsync(p => p.Id == work.Partner, ct);
                var head = await ReadHead(context, work.Partner, ct);
                if (row is null || head is null || row.TenantId != context.CurrentTenantId || head.Legal != legal ||
                    row.RowVersion is null || !row.RowVersion.SequenceEqual(head.RowVersion))
                    throw BpUseScopeGate.Unproven();
                if (row.IsDeleted || row.Status == 9) throw Rejected("BP_RETIRED", 409);
                if (row.IsFrozen) throw Rejected("BP_FROZEN", 409);
                if (head.Registration != Registered || row.Status != 1) throw Rejected("BP_NOT_REGISTERED", 409);
                if (!HasRole(row, work.Role)) throw Rejected("BP_ROLE_MISSING", 409);
                if (work.Partner == bp && head.Revision != expectedRevision)
                    throw Rejected("BP_VERSION_STALE", 412);
                if (row.CurrencyCd is not { } currency || !CurrencyCode.IsMatch(currency.TrimEnd()) ||
                    !await CurrentUseBaseAsync(context, legal, Base(row), ct))
                    throw BpUseScopeGate.Unproven();
                var slots = (await ReadSlotRows(context, row.Id, ct)).ToDictionary(x => x.Slot, x => x.TargetId, StringComparer.Ordinal);
                if (Consistency(row).Length != 0 || CodeIssues(row, options).Length != 0 || MissingRequirements(row, slots).Length != 0)
                    throw BpUseScopeGate.Unproven();
                current = (row, head, slots);
                snapshots.Add(row.Id, current);
            }

            if (!HasRole(current.Row, work.Role)) throw Rejected("BP_ROLE_MISSING", 409);
            foreach (var slot in Slots.Where(slot => slot.Source == work.Role))
            {
                if (current.Slots.GetValueOrDefault(slot.Key) is not { } target) continue;
                foreach (var required in slot.Targets) candidates.Enqueue((target, required));
            }
        }

        var start = snapshots[bp];
        var sources = snapshots.Values.Where(value => value.Row.Id != bp)
            .OrderBy(value => value.Row.Id)
            .Select(value => new BpUseSourceRef(ProfileOwner, value.Row.Id.ToString("D"), Revision(value.Version.Revision),
                value.Version.Hash, "BP_ROLE_DEPENDENCY")).ToArray();
        var finance = snapshots.Values.OrderBy(value => value.Row.Id).Select(value =>
        {
            var id = value.Row.Id;
            var protector = protection.CreateProtector(ProfileOwner, context.CurrentTenantId.ToString("D"), "revision", id.ToString("D"));
            var links = ReadFinance(value.Version, protector).Where(link => link.Reference is not null)
                .Select(link => new BusinessPartnerFinanceUseV1("finance." + link.Kind, link.Reference!)).ToArray();
            return new BpUseFinanceGroup(id, value.Version.Revision, links);
        }).Where(value => value.Links.Length != 0).ToArray();
        return new(legal, Code(start.Row), start.Version.Revision, start.Version.Hash, start.Row.CurrencyCd!.TrimEnd(),
            RoleSet(start.Row), sources, finance);
    }

    internal static Task<bool> CurrentUseBaseAsync(CP6Context context, string legal, string bpBase, CancellationToken ct)
        => context.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT FROM main_bp.bases b
                JOIN public."M_Base" m ON m."TenantId"=b.tenant_id AND rtrim(m."BaseCd"::text) COLLATE "C"=b.base_cd
                WHERE b.tenant_id={context.CurrentTenantId} AND b.legal_entity_key={legal}
                  AND b.base_cd={bpBase} AND b.enabled AND NOT m."IsDeleted" FOR SHARE OF m) AS "Value"
            """).SingleAsync(ct);
}
