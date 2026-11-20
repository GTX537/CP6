using System.Globalization;
using System.Text.Json;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Erp;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Erp;

public sealed partial class BusinessPartnerWorkspaceV1
{
    private async Task<object> List(OperationsAccessV1 access, JsonElement body, CancellationToken ct)
    {
        var result = await FindWorkspaceRows(access, body, null, ct);
        var offset = Math.Min((long)(result.Page - 1) * result.PageSize, result.Rows.Length);
        var items = result.Rows.Skip((int)offset).Take(result.PageSize).Select(value => new
        {
            bp = BpReference(value.Row, value.Head, value.Head.Revision), name = Name(value.Row), baseCd = Base(value.Row),
            roles = RoleSet(value.Row), state = StateOf(value.Row, value.Head, value.Head.Revision),
            createdAt = Instant(value.Row.CreateDate), canOpen = true
        }).ToArray();
        return new { items, total = result.Rows.Length, page = result.Page, pageSize = result.PageSize, asOf = Instant(Now) };
    }

    private sealed record WorkspaceRows((BusinessPartner Row, Head Head)[] Rows, int Page, int PageSize);

    private async Task<WorkspaceRows> FindWorkspaceRows(OperationsAccessV1 access, JsonElement body, int? limit, CancellationToken ct)
    {
        var query = Exact(body, "filter", "sort", "direction", "page", "pageSize");
        var filter = Exact(query["filter"], "legalEntityKey", "rolesAny", "registrations", "lifecycles", "holds", "bpCd", "name",
            "baseCds", "salesStaffCd", "businessStaffCd", "classFilters", "createdFrom", "createdTo");
        var legal = LegalKey(Key(filter["legalEntityKey"], 64));
        var sort = Key(query["sort"], 12);
        var direction = Key(query["direction"], 4);
        var page = Positive(query["page"], int.MaxValue);
        var size = Positive(query["pageSize"], 100);
        if (sort is not ("BP_CD" or "NAME" or "CREATED_AT") || direction is not ("ASC" or "DESC")) throw Invalid();
        var roles = FilterKeys(filter["rolesAny"], Roles);
        var registrations = FilterKeys(filter["registrations"], [PreRegistered, Registered, UnknownLegacy]);
        var lifecycles = FilterKeys(filter["lifecycles"], [Active, Retired]);
        var holds = FilterKeys(filter["holds"], [Clear, Frozen]);
        var code = OptionalText(filter["bpCd"], 20);
        var name = OptionalText(filter["name"], 100);
        var salesStaff = OptionalText(filter["salesStaffCd"], 20);
        var businessStaff = OptionalText(filter["businessStaffCd"], 20);
        var dateFrom = OptionalDate(filter["createdFrom"]);
        var dateTo = OptionalDate(filter["createdTo"]);
        if (dateFrom > dateTo) throw Invalid();

        var classes = Array(filter["classFilters"], 10).Select(item =>
        {
            var rule = Exact(item, "field", "code");
            var field = Key(rule["field"], 9);
            if (!BusinessPartnerProfileFieldsV1.ByName.TryGetValue(field, out var definition) ||
                !field.StartsWith("bpClass", StringComparison.Ordinal) || !access.CanReadPartnerField(definition.Original))
                throw Denied("FIELD_READ_FORBIDDEN");
            return (Field: definition, Code: Key(rule["code"], 10));
        }).ToArray();
        if (classes.Select(x => x.Field.Name).Distinct(StringComparer.Ordinal).Count() != classes.Length) throw Invalid();
        if (!BusinessPartnerProfileFieldsV1.Core.Take(4).All(access.CanReadPartnerField) || !access.CanReadPartnerField("Roles") ||
            salesStaff is not null && !access.CanReadPartnerField("SalesStaffCd") ||
            businessStaff is not null && !access.CanReadPartnerField("BusinessStaffCd"))
            throw Denied("FIELD_READ_FORBIDDEN");

        await ScopeAsync(legal, false, ct);
        var bases = await BasesAsync(access, legal, ct);
        if (bases.Length == 0) throw Missing();
        var allowed = bases.Select(x => x.BaseCd).ToArray();
        var requested = Array(filter["baseCds"], 50).Select(x => Key(x, 10)).ToArray();
        if (requested.Distinct(StringComparer.Ordinal).Count() != requested.Length || requested.Any(x => !allowed.Contains(x, StringComparer.Ordinal)))
            throw Denied("BP_BASE_READ_REQUIRED");
        var codes = requested.Length == 0 ? allowed : requested;
        var heads = db.Database.SqlQuery<Head>($"""
            SELECT h.bp_id AS "Id",h.legal_entity_key AS "Legal",h.registration AS "Registration",
                   h.revision AS "Revision",p.row_version AS "RowVersion",p.content_hash AS "Hash",''::bytea AS "Body"
            FROM main_bp.heads h JOIN main_bp.profile_revisions p USING(tenant_id,legal_entity_key,bp_id,revision)
            WHERE h.tenant_id={Tenant} AND h.legal_entity_key={legal}
            """);
        var rows = from row in access.BusinessPartners(includeDeleted: true)
                   where codes.Contains(row.BaseCd)
                   join head in heads on row.Id equals head.Id
                   select new { row, head };
        var results = new List<(BusinessPartner Row, Head Head)>();
        var normalizedName = name is null ? null : BusinessPartnerUnicode15.Normalize(name);
        await foreach (var match in rows.AsAsyncEnumerable().WithCancellation(ct))
        {
            var (row, head) = (match.row, match.head);
            var created = row.CreateDate;
            if (code is not null && !Code(row).Contains(code, StringComparison.Ordinal) ||
                normalizedName is not null && !BusinessPartnerUnicode15.Normalize(Name(row)).Contains(normalizedName, StringComparison.Ordinal) ||
                salesStaff is not null && row.SalesStaffCd?.TrimEnd() != salesStaff ||
                businessStaff is not null && row.BusinessStaffCd?.TrimEnd() != businessStaff ||
                roles.Length > 0 && !roles.Any(role => HasRole(row, role)) ||
                lifecycles.Length > 0 && !lifecycles.Contains(Lifecycle(row), StringComparer.Ordinal) ||
                holds.Length > 0 && !holds.Contains(Hold(row), StringComparer.Ordinal) ||
                dateFrom is { } first && DateOnly.FromDateTime(created) < first ||
                dateTo is { } last && DateOnly.FromDateTime(created) > last ||
                classes.Any(item => (NormalizeFixedColumn(item.Field.Value(row)) as string) != item.Code))
                continue;
            if (!head.RowVersion.SequenceEqual(row.RowVersion ?? [])) throw Rejected("BP_PROFILE_FRONTIER_UNPROVEN", 503);
            if (registrations.Length == 0 || registrations.Contains(head.Registration, StringComparer.Ordinal))
            {
                if (results.Count == limit) throw Rejected("EXPORT_LIMIT", 422);
                results.Add((row, head));
            }
        }
        var ordered = sort switch
        {
            "NAME" => results.OrderBy(x => Name(x.Row), StringComparer.Ordinal).ThenBy(x => x.Row.Id),
            "CREATED_AT" => results.OrderBy(x => x.Row.CreateDate).ThenBy(x => x.Row.Id),
            _ => results.OrderBy(x => Code(x.Row), StringComparer.Ordinal).ThenBy(x => x.Row.Id)
        };
        return new((direction == "ASC" ? ordered : ordered.Reverse()).ToArray(), page, size);
    }

    private static string[] FilterKeys(JsonElement input, string[] selection)
    {
        var values = Array(input, selection.Length).Select(item => Key(item, 30)).ToArray();
        if (values.Any(x => !selection.Contains(x, StringComparer.Ordinal)) ||
            values.Distinct(StringComparer.Ordinal).Count() != values.Length) throw Invalid();
        return values;
    }

    private static DateOnly? OptionalDate(JsonElement input)
        => input.ValueKind == JsonValueKind.Null ? null :
           DateOnly.TryParseExact(Key(input, 10), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date : throw Invalid();

    private sealed class AuditRow
    {
        public long Version { get; init; }
        public string Hash { get; init; } = "";
        public Guid Actor { get; init; }
        public string? Reason { get; init; }
        public string[] Fields { get; init; } = [];
        public DateTimeOffset At { get; init; }
        public byte[] Body { get; init; } = [];
    }

    private async Task<object> History(OperationsAccessV1 access, JsonElement body, CancellationToken ct)
    {
        var input = Exact(body, "legalEntityKey", "bpId", "kind", "beforeSequence", "limit");
        var legal = LegalKey(Key(input["legalEntityKey"], 64));
        var target = Uuid(input["bpId"]);
        var limit = Positive(input["limit"], 100);
        var kind = Key(input["kind"], 15);
        if (kind is not ("PROFILE" or "STATUS" or "USE")) throw Invalid();
        long? before = input["beforeSequence"].ValueKind == JsonValueKind.Null ? null : Version(input["beforeSequence"]);
        var reader = await access.ActionAsync(Resource, View, ct);
        var useHistory = kind == "USE" && await BpUseScopeGate.IsCutoverAsync(db, ct);
        if (useHistory) await BpUseScopeGate.LockAsync(db, legal, false, ct);
        var known = await VisibleAsync(reader, target, false, ct);
        if (Adopted(known.Head).Legal != legal) throw Missing();
        var fields = await Readable(reader, ct);
        if (kind == "USE")
            return useHistory ? await UseHistory(reader, fields, legal, target, before, limit, ct)
                : new { items = System.Array.Empty<object>(), nextBeforeSequence = (string?)null, asOf = Instant(Now) };
        var records = await db.Database.SqlQuery<AuditRow>($"""
            SELECT a.revision AS "Version", p.content_hash AS "Hash", a.actor_id AS "Actor", a.reason AS "Reason",
                a.fields AS "Fields", a.recorded_at AS "At", p.protected_body AS "Body"
            FROM main_bp.audit a JOIN main_bp.profile_revisions p USING(tenant_id,legal_entity_key,bp_id,revision)
            WHERE a.tenant_id={Tenant} AND a.legal_entity_key={legal} AND a.bp_id={target}
              AND a.outcome='APPLIED' AND ({before}::bigint IS NULL OR a.revision < {before})
              AND ({kind}='PROFILE' OR EXISTS (
                SELECT FROM main_bp.state_history s WHERE s.tenant_id=a.tenant_id AND s.command_id=a.command_id))
            ORDER BY a.revision DESC LIMIT {limit + 1}
            """).ToArrayAsync(ct);
        var items = records.Take(limit).Select(record =>
        {
            var hidden = record.Fields.Where(field => !CanReadChange(reader, fields, field)).ToArray();
            var reference = new
            {
                owner = ProfileOwner, id = target.ToString("D"), version = Revision(record.Version),
                digest = fields.IsComplete && hidden.Length == 0 ? record.Hash : null, purpose = "BP_PROFILE_HISTORY"
            };
            return new { sequence = Revision(record.Version), kind, occurredAt = Instant(record.At),
                actorDisplay = record.Actor.ToString("D"), reason = fields.Sensitive ? record.Reason : null,
                reference, changes = HistoryChanges(target, record, hidden), redactedFields = hidden };
        }).ToArray();
        return new { items, nextBeforeSequence = records.Length > limit ? items[^1].sequence : null, asOf = Instant(Now) };
    }

    private sealed record UseHistoryRow
    {
        public long Sequence { get; init; }
        public Guid UseKey { get; init; }
        public string Actor { get; init; } = "";
        public DateTimeOffset At { get; init; }
        public string Receipt { get; init; } = "";
    }

    private async Task<object> UseHistory(OperationsAccessV1 reader, ReadProjection fields, string legal, Guid bp, long? before,
        int limit, CancellationToken ct)
    {
        var stored = await db.Database.SqlQuery<UseHistoryRow>($"""
            SELECT history_sequence AS "Sequence",use_key AS "UseKey",actor_display AS "Actor",recorded_at AS "At",
                receipt AS "Receipt" FROM main_bp.use_receipts
            WHERE tenant_id={Tenant} AND legal_entity_key={legal} AND bp_id={bp}
                AND ({before}::bigint IS NULL OR history_sequence < {before})
            ORDER BY history_sequence DESC LIMIT {limit + 1}
            """).ToArrayAsync(ct);
        var full = fields.IsComplete && reader.CanReadPartnerField("Relations") && reader.CanReadPartnerField("FinanceLinks");
        var items = stored.Take(limit).Select(row =>
        {
            var original = BpUseParticipant.ReadOriginal(row.Receipt);
            if (original.UseKey != row.UseKey || original.Bp.BpId != bp || original.Bp.LegalEntityKey != legal)
                throw BpUseScopeGate.Unproven();
            // 本页只证明当前伙伴下的原凭证。仍需其他 Owner 范围证明时只披露安全引用。
            var canDisclose = full && original.AccountId is null && original.DependencyRefs.Length == 0;
            var changes = canDisclose
                ? new[] { new { field = "useReceipt", value = (object)original } }
                : new[]
                {
                    new { field = "purpose", value = (object)original.Purpose },
                    new { field = "bpRevision", value = (object)original.Bp.Revision }
                };
            return new
            {
                sequence = Revision(row.Sequence), kind = "USE", occurredAt = Instant(row.At), actorDisplay = row.Actor,
                reason = (string?)null, reference = new
                {
                    owner = "Main.Erp.BusinessPartnerUse.v1", id = original.UseKey.ToString("D"),
                    version = Revision(row.Sequence), digest = canDisclose ? original.ReceiptHash : null, purpose = "BP_USE_HISTORY"
                }, changes, redactedFields = canDisclose ? System.Array.Empty<string>() : ["useReceipt"]
            };
        }).ToArray();
        return new { items, nextBeforeSequence = stored.Length > limit ? items[^1].sequence : null, asOf = Instant(Now) };
    }

    private static bool CanReadChange(OperationsAccessV1 reader, ReadProjection projection, string field)
        => CoreEditFields.Contains(field, StringComparer.Ordinal) ? reader.CanReadPartnerField(PermissionName(field))
            : projection.Original.Contains(field);

    private object[] HistoryChanges(Guid bp, AuditRow record, string[] hidden)
    {
        var bytes = Protect("revision", bp).Unprotect(record.Body);
        try
        {
            using var json = JsonDocument.Parse(bytes);
            var root = json.RootElement;
            var profile = root.GetProperty("profile");
            var changes = new List<object>();
            foreach (var field in record.Fields)
            {
                if (hidden.Contains(field, StringComparer.Ordinal)) continue;
                var name = field switch { "bpName" => "name", "bpAbbrev" => "abbreviation", _ => field };
                if (CoreEditFields.Contains(field, StringComparer.Ordinal) && root.TryGetProperty(name, out var core))
                    changes.Add(new { field, value = core.Clone() });
                else if (profile.TryGetProperty(field, out var value)) changes.Add(new { field, value = value.Clone() });
            }
            return changes.ToArray();
        }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
    }
}
