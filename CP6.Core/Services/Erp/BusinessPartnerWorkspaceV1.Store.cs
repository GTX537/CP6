using System.Security.Cryptography;
using CP6.Core.EFDbContext;
using System.Text.Json;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Erp;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Core.Services.Erp;

public sealed partial class BusinessPartnerWorkspaceV1
{
    private sealed class Scope
    {
        public string Key { get; init; } = "";
        public string Label { get; init; } = "";
        public long Revision { get; init; }
        public string NormVersion { get; init; } = "";
        public bool Coverage { get; init; }
    }

    private sealed class BaseGrant
    {
        public string LegalEntityKey { get; init; } = "";
        public string BaseCd { get; init; } = "";
        public string Name { get; init; } = "";
        public string SourceVersion { get; init; } = "";
        public bool CanManage { get; init; }
    }

    private sealed record BaseChoice(string LegalEntityKey, string BaseCd, string Name, string SourceVersion, bool CanManage);

    private sealed class Option
    {
        public string Field { get; init; } = "";
        public string Code { get; init; } = "";
        public string Label { get; init; } = "";
        public string Owner { get; init; } = "";
        public string SourceId { get; init; } = "";
        public string Version { get; init; } = "";
        public string Digest { get; init; } = "";
        public bool Enabled { get; init; }
    }

    private sealed class Head
    {
        public Guid Id { get; init; }
        public string Legal { get; init; } = "";
        public string Registration { get; init; } = "";
        public long Revision { get; init; }
        public byte[] RowVersion { get; init; } = [];
        public string Hash { get; init; } = "";
        public byte[] Body { get; init; } = [];
    }

    private sealed class SlotRow
    {
        public string Slot { get; init; } = "";
        public Guid? TargetId { get; init; }
    }

    private sealed class SavedPreview
    {
        public Guid Id { get; init; }
        public Guid Actor { get; init; }
        public string Legal { get; init; } = "";
        public long ScopeRevision { get; init; }
        public string Operation { get; init; } = "";
        public string Permission { get; init; } = "";
        public string IntentHash { get; init; } = "";
        public string SourceHash { get; init; } = "";
        public string CandidateHash { get; init; } = "";
        public byte[] Body { get; init; } = [];
        public Guid? Target { get; init; }
        public byte[]? RowVersion { get; init; }
        public DateTimeOffset Expires { get; init; }
        public bool CanApply { get; init; }
        public Guid? UsedCommand { get; init; }
    }

    private sealed class SavedCommand
    {
        public Guid Id { get; init; }
        public Guid Actor { get; init; }
        public string Legal { get; init; } = "";
        public Guid Target { get; init; }
        public string Operation { get; init; } = "";
        public byte[] Fingerprint { get; init; } = [];
        public string Original { get; init; } = "";
    }

    private Guid Tenant => db.CurrentTenantId;
    private DateTimeOffset Now => clock.GetUtcNow();
    private IDataProtector Protect(string kind, Guid id) => protection.CreateProtector(ProfileOwner, Tenant.ToString("D"), kind, id.ToString("D"));

    private async Task<Scope> ScopeAsync(string legal, bool write, CancellationToken ct)
    {
        if (await BpUseScopeGate.IsCutoverAsync(db, ct))
        {
            if (write) await BpUseScopeGate.RequireWriterAsync(db, legal, ct);
            else await BpUseScopeGate.LockAsync(db, legal, false, ct);
        }
        var rows = write
            ? await db.Database.SqlQuery<Scope>($"""
                SELECT legal_entity_key AS "Key", label AS "Label", revision AS "Revision", norm_version AS "NormVersion",
                       coverage_complete AS "Coverage" FROM main_bp.scopes
                WHERE tenant_id={Tenant} AND legal_entity_key={legal} AND enabled FOR UPDATE
                """).ToListAsync(ct)
            : await db.Database.SqlQuery<Scope>($"""
                SELECT legal_entity_key AS "Key", label AS "Label", revision AS "Revision", norm_version AS "NormVersion",
                       coverage_complete AS "Coverage" FROM main_bp.scopes
                WHERE tenant_id={Tenant} AND legal_entity_key={legal} AND enabled FOR SHARE
                """).ToListAsync(ct);
        return rows.SingleOrDefault() ?? throw Missing();
    }

    private async Task<BaseChoice[]> BasesAsync(OperationsAccessV1 access, string? legal, CancellationToken ct)
    {
        var roles = access.BusinessPartnerRoles;
        var rows = await db.Database.SqlQuery<BaseGrant>($"""
            SELECT b.legal_entity_key AS "LegalEntityKey", b.base_cd AS "BaseCd", m."BaseName" AS "Name",
                   b.source_version AS "SourceVersion", g.can_manage AS "CanManage"
              FROM main_bp.bases b JOIN main_bp.base_grants g USING(tenant_id,legal_entity_key,base_cd)
              JOIN main_bp.scopes s USING(tenant_id,legal_entity_key)
              JOIN public."M_Base" m ON m."TenantId"=b.tenant_id AND rtrim(m."BaseCd"::text) COLLATE "C"=b.base_cd
              WHERE b.tenant_id={Tenant} AND ({legal}::text IS NULL OR b.legal_entity_key={legal})
                AND s.enabled AND b.enabled AND g.can_read AND g.role_id=ANY({roles}) AND NOT m."IsDeleted"
              ORDER BY b.legal_entity_key,b.base_cd
              FOR SHARE OF b,g,m
            """).ToListAsync(ct);
        return rows.GroupBy(row => (row.LegalEntityKey, row.BaseCd)).Select(group =>
        {
            var source = group.First();
            return new BaseChoice(source.LegalEntityKey, source.BaseCd, source.Name.TrimEnd(), source.SourceVersion, group.Any(x => x.CanManage));
        }).ToArray();
    }

    private static BaseChoice RequireBase(BaseChoice[] bases, string code, bool modify)
    {
        var match = bases.SingleOrDefault(x => x.BaseCd == code) ?? throw Missing();
        if (modify && !match.CanManage) throw Denied("BP_BASE_MANAGE_REQUIRED");
        return match;
    }

    internal async Task<IQueryable<BusinessPartner>> LegacyProjection(IQueryable<BusinessPartner> rows, CancellationToken ct)
    {
        if (!await WorkspaceInstalled(ct)) return rows;
        var adopted = db.Database.SqlQuery<Guid>($"""
            SELECT bp_id AS "Value" FROM main_bp.heads WHERE tenant_id={Tenant}
            """);
        return rows.Where(row => !adopted.Contains(row.Id));
    }

    private Task<List<Option>> OptionsAsync(string legal, CancellationToken ct) => ReadOptions(db, legal, ct);

    private static Task<List<Option>> ReadOptions(CP6Context context, string legal, CancellationToken ct)
        => context.Database.SqlQuery<Option>($"""
            SELECT field AS "Field",code AS "Code",label AS "Label",owner AS "Owner",source_id AS "SourceId",
                   source_version AS "Version",source_digest AS "Digest",enabled AS "Enabled"
            FROM main_bp.option_values WHERE tenant_id={context.CurrentTenantId} AND legal_entity_key={legal}
            ORDER BY field,code FOR SHARE
            """).ToListAsync(ct);

    private Task<Head?> HeadAsync(Guid id, CancellationToken ct) => ReadHead(db, id, ct);

    private static Task<Head?> ReadHead(CP6Context context, Guid id, CancellationToken ct)
        => context.Database.SqlQuery<Head>($"""
            SELECT h.bp_id AS "Id",h.legal_entity_key AS "Legal",h.registration AS "Registration",h.revision AS "Revision",
                   p.row_version AS "RowVersion",p.content_hash AS "Hash",p.protected_body AS "Body"
            FROM main_bp.heads h JOIN main_bp.profile_revisions p USING(tenant_id,legal_entity_key,bp_id,revision)
            WHERE h.tenant_id={context.CurrentTenantId} AND h.bp_id={id}
            """).SingleOrDefaultAsync(ct);

    private async Task<(BusinessPartner Row, Head? Head)> VisibleAsync(OperationsAccessV1 access, Guid id, bool tracked, CancellationToken ct)
    {
        var rows = access.BusinessPartners(includeDeleted: true);
        var row = await (tracked ? rows.AsTracking() : rows).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Missing();
        if (!BusinessPartnerProfileFieldsV1.Core.Take(4).All(access.CanReadPartnerField) || !access.CanReadPartnerField("Roles"))
            throw Missing();
        var head = await HeadAsync(id, ct);
        if (head is not null)
        {
            RequireBase(await BasesAsync(access, head.Legal, ct), Base(row), false);
            if (!(row.RowVersion ?? []).SequenceEqual(head.RowVersion)) throw Rejected("BP_PROFILE_FRONTIER_UNPROVEN", 503);
        }
        return (row, head);
    }

    private async Task<long> LegacyRevision(Guid id, CancellationToken ct)
        => await db.Database.SqlQuery<long>($"""
            SELECT revision AS "Value" FROM cp6_business_partner_identity_v1.heads WHERE tenant_id={Tenant} AND partner_id={id}
            """).SingleOrDefaultAsync(ct) is var value && value > 0 ? value : throw Rejected("BP_PROFILE_FRONTIER_UNPROVEN", 503);

    private static Head Adopted(Head? head) => head ?? throw Rejected("BP_LEGAL_SCOPE_UNPROVEN", 409);

    private Task<SlotRow[]> ReadSlotsAsync(Guid id, CancellationToken ct) => ReadSlotRows(db, id, ct);

    private static Task<SlotRow[]> ReadSlotRows(CP6Context context, Guid id, CancellationToken ct)
        => context.Database.SqlQuery<SlotRow>($"""
            SELECT slot AS "Slot",target_id AS "TargetId" FROM main_bp.relations
            WHERE tenant_id={context.CurrentTenantId} AND bp_id={id} ORDER BY slot
            """).ToArrayAsync(ct);

    private FinanceLink[] PriorFinance(Head head) => ReadFinance(head, Protect("revision", head.Id));

    private static FinanceLink[] ReadFinance(Head head, IDataProtector protector)
    {
        var original = protector.Unprotect(head.Body);
        try
        {
            using var doc = JsonDocument.Parse(original);
            if (!string.Equals(Convert.ToHexString(Sha(original)), head.Hash, StringComparison.OrdinalIgnoreCase))
                throw Rejected("BP_PROFILE_FRONTIER_UNPROVEN", 503);
            return doc.RootElement.GetProperty("financeLinks").Deserialize<FinanceLink[]>(Json)
                ?? throw Rejected("BP_PROFILE_FRONTIER_UNPROVEN", 503);
        }
        finally { CryptographicOperations.ZeroMemory(original); }
    }

    private async Task<Head> SaveRevision(BusinessPartner row, Head? prior, Scope scope, string registration,
        IReadOnlyDictionary<string, Guid?> slots, FinanceLink[] finance, bool writeSlots, DateTimeOffset recorded, CancellationToken ct)
    {
        var revision = checked((prior?.Revision ?? 0) + 1);
        var original = new
        {
            bpId = row.Id, bpCd = Code(row), legalEntityKey = scope.Key, revision = Revision(revision), name = Name(row),
            abbreviation = row.BpAbbrev?.TrimEnd(), baseCd = Base(row), commercialCurrency = row.CurrencyCd?.TrimEnd(),
            roles = RoleSet(row), profile = Profile(row),
            relations = slots.OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => new Related(x.Key, x.Value is { } target ? new("BP", target) : null)).ToArray(),
            financeLinks = finance,
            state = new { registration, lifecycle = Lifecycle(row), hold = Hold(row), legacyScopeState = "ADOPTED" }
        };
        var canonical = Canonical(original);
        var hash = Convert.ToHexString(Sha(canonical)).ToLowerInvariant();
        byte[] cipher;
        try { cipher = Protect("revision", row.Id).Protect(canonical); }
        finally { CryptographicOperations.ZeroMemory(canonical); }
        var rv = row.RowVersion ?? throw Rejected("BP_PROFILE_FRONTIER_UNPROVEN", 503);
        if (prior is null)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO main_bp.heads(tenant_id,bp_id,legal_entity_key,registration,revision)
                VALUES ({Tenant},{row.Id},{scope.Key},{registration},{revision})
                """, ct);
        else if (await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE main_bp.heads SET registration={registration},revision={revision}
            WHERE tenant_id={Tenant} AND bp_id={row.Id} AND legal_entity_key={scope.Key} AND revision={prior.Revision}
            """, ct) != 1) throw Rejected("BP_VERSION_STALE", 412);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO main_bp.profile_revisions(tenant_id,legal_entity_key,bp_id,revision,row_version,content_hash,protected_body,recorded_at)
            VALUES ({Tenant},{scope.Key},{row.Id},{revision},{rv},{hash},{cipher},{recorded})
            """, ct);
        if (writeSlots)
            foreach (var slot in Slots)
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO main_bp.relations(tenant_id,legal_entity_key,bp_id,slot,target_id)
                    VALUES({Tenant},{scope.Key},{row.Id},{slot.Key},{slots[slot.Key]})
                    ON CONFLICT (tenant_id,legal_entity_key,bp_id,slot) DO UPDATE SET target_id=EXCLUDED.target_id
                    """, ct);
        await SaveNormalized(row, scope.Key, revision, ct);
        if (await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE main_bp.scopes SET revision=revision+1 WHERE tenant_id={Tenant} AND legal_entity_key={scope.Key} AND revision={scope.Revision}
            """, ct) != 1) throw Stale();
        return new Head { Id = row.Id, Legal = scope.Key, Revision = revision, Registration = registration, RowVersion = rv, Hash = hash, Body = cipher };
    }

    private async Task TouchRoot(BusinessPartner row, OperationsAccessV1 access, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('cp6.bp_workspace_writer',{row.Id.ToString("D")},true)", ct);
        row.Modifier = access.Actor;
        row.ModifyDate = DateTime.Now;
        db.Entry(row).Property(x => x.ModifyDate).IsModified = true;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw Rejected("BP_VERSION_STALE", 412); }
    }

    private async Task InsertRoot(BusinessPartner row, OperationsAccessV1 access, CancellationToken ct)
    {
        row.TenantId = Tenant;
        row.Creator = row.Modifier = access.Actor;
        row.CreateDate = DateTime.Now;
        row.ModifyDate = row.CreateDate;
        // 新建与维护使用相同的事务内写者标记，数据库同时复核 Scope X 和主写者登记。
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('cp6.bp_workspace_writer',{row.Id.ToString("D")},true)", ct);
        db.BusinessPartners.Add(row);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_T_WebBusinessPartner_BpCd" })
        {
            throw Rejected("BP_CODE_EXISTS", 409);
        }
    }

    private Task SaveNormalized(BusinessPartner row, string legal, long revision, CancellationToken ct)
    {
        var name = BusinessPartnerUnicode15.Normalize(Name(row));
        var standard = NormalizedOptional(row.StdCoCd);
        var ein = NormalizedOptional(row.Ein);
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO main_bp.normalized_identity(tenant_id,legal_entity_key,bp_id,norm_version,revision,
                name_norm,name_hash,standard_norm,standard_hash,ein_type,ein_norm,ein_hash)
            VALUES({Tenant},{legal},{row.Id},{BusinessPartnerUnicode15.Version},{revision},{name},{Sha(Bytes(name))},
                {standard},{HashOptional(standard)},{row.EinType?.TrimEnd()},{ein},{HashOptional(ein)})
            ON CONFLICT (tenant_id,legal_entity_key,bp_id) DO UPDATE SET
                norm_version=EXCLUDED.norm_version,revision=EXCLUDED.revision,name_norm=EXCLUDED.name_norm,name_hash=EXCLUDED.name_hash,
                standard_norm=EXCLUDED.standard_norm,standard_hash=EXCLUDED.standard_hash,ein_type=EXCLUDED.ein_type,
                ein_norm=EXCLUDED.ein_norm,ein_hash=EXCLUDED.ein_hash
            """, ct);
    }

    private static string? NormalizedOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : BusinessPartnerUnicode15.Normalize(value);
    private static byte[]? HashOptional(string? value) => value is null ? null : Sha(Bytes(value));

    private Task<SavedPreview?> FindPreview(Guid id, CancellationToken ct)
        => db.Database.SqlQuery<SavedPreview>($"""
            SELECT preview_id AS "Id",actor_id AS "Actor",legal_entity_key AS "Legal",scope_revision AS "ScopeRevision",
                operation AS "Operation",permission_version AS "Permission",intent_hash AS "IntentHash",
                source_hash AS "SourceHash",candidate_hash AS "CandidateHash",protected_body AS "Body",bp_id AS "Target",
                row_version AS "RowVersion",expires_at AS "Expires",can_apply AS "CanApply",used_command AS "UsedCommand"
            FROM main_bp.previews WHERE tenant_id={Tenant} AND preview_id={id}
            """).SingleOrDefaultAsync(ct);

    private Task<SavedCommand?> FindCommand(Guid id, CancellationToken ct)
        => db.Database.SqlQuery<SavedCommand>($"""
            SELECT command_id AS "Id",actor_id AS "Actor",legal_entity_key AS "Legal",bp_id AS "Target",operation AS "Operation",
                fingerprint AS "Fingerprint",receipt AS "Original"
            FROM main_bp.commands WHERE tenant_id={Tenant} AND command_id={id}
            """).SingleOrDefaultAsync(ct);

    private async Task<CommandResponse> Record(OperationsAccessV1 access, Guid id, byte[] fingerprint, string operation,
        BusinessPartner row, Head head, bool applied, string? reason, string[] fields, bool recordState, CancellationToken ct)
    {
        var occurred = Now;
        var bp = new Reference(row.Id, Code(row), head.Legal, Revision(head.Revision), null);
        var outcome = applied ? "APPLIED" : "NO_CHANGE";
        var receipt = new Receipt(id, operation, outcome, bp, null, null, null, Instant(occurred), "");
        receipt = receipt with { ReceiptHash = ReceiptHash(receipt) };
        var original = Utf8.GetString(Canonical(receipt));
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO main_bp.commands(tenant_id,command_id,actor_id,legal_entity_key,bp_id,operation,fingerprint,receipt)
            VALUES({Tenant},{id},{access.ActorId},{head.Legal},{row.Id},{operation},{fingerprint},{original})
            """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO main_bp.audit(tenant_id,command_id,legal_entity_key,bp_id,revision,actor_id,operation,outcome,reason,fields,recorded_at)
            VALUES({Tenant},{id},{head.Legal},{row.Id},{head.Revision},{access.ActorId},{operation},{outcome},{reason},{fields},{occurred})
            """, ct);
        if (applied && recordState)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO main_bp.state_history(tenant_id,legal_entity_key,bp_id,revision,command_id,registration,lifecycle,hold)
                VALUES({Tenant},{head.Legal},{row.Id},{head.Revision},{id},{head.Registration},{Lifecycle(row)},{Hold(row)})
                """, ct);
        return new(receipt, false);
    }
}
