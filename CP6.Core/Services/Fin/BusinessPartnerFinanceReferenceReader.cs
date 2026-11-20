using System.Data.Common;
using System.Globalization;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Ledger = CP6.Core.Services.Fin.ManufacturingFinanceScopeV1.LedgerRow;

namespace CP6.Core.Services.Fin;

public sealed record BusinessPartnerFinanceSourceRefV1(string Owner, string Id, string Version, string Digest, string Purpose);

public sealed record BusinessPartnerFinanceReferenceViewV1(string Availability, BusinessPartnerFinanceSourceRefV1? Reference = null,
    bool? Enabled = null, string? Reason = null, FinanceScopeV1? Scope = null, string? BpRevision = null,
    string? MaskedAccount = null, string? CreditLimit = null, string? Currency = null, DateTimeOffset? AsOf = null)
{
    public bool IsExecutionPermit => false;
}

internal sealed record BpFinanceLinkReadBodyV1(Guid RequestingBpId, string LegalEntityKey, string Kind,
    BusinessPartnerFinanceSourceRefV1 Reference, string BpRevision, DateTime ReadAt, string Mode);

internal sealed record BusinessPartnerFinanceUseV1(string Field, BusinessPartnerFinanceSourceRefV1 Reference);

internal sealed record BusinessPartnerFinanceCurrentV1(string Decision, bool HeldUntilTransactionEnd,
    string? Reason = null, IReadOnlyList<BusinessPartnerFinanceReferenceViewV1>? Observations = null, string? GuardRevision = null);

/// <summary>读取持久伙伴引用与准确原件。当前资格只能在使用方的 Main PostgreSQL 事务内复核。</summary>
public sealed class BusinessPartnerFinanceReferenceReader(CP6Context db, OperationsOwnerV1 owner, ManufacturingFinanceScopeV1 finance)
{
    public const string ReceiptBank = "finance.RECEIPT_BANK";
    public const string PaymentBank = "finance.PAYMENT_BANK";
    public const string CreditMaster = "finance.CREDIT_MASTER";
    internal const string ReadAction = "finance-reference-read";
    internal const string LegacyBankOwner = "Main.Fin.ApMaster.BankAccount.v1";
    internal const string LegacyCreditOwner = "Main.Erp.BusinessPartner.CreditLimit.v1";
    public static IReadOnlyList<FieldRegistry.Field> Fields { get; } = Array.AsReadOnly<FieldRegistry.Field>(
        [new("BpName", "客户名称"), new(CreditMaster, "信用来源"), new(PaymentBank, "付款银行引用"), new(ReceiptBank, "收款银行引用")]);

    public Task<BusinessPartnerFinanceReferenceViewV1> ReadAsync(Guid bp, string field, Guid? context,
        string? bpRevision = null, CancellationToken ct = default)
    {
        if (!owner.Owns(db)) throw Failure("AUTHORITY_FENCE_UNAVAILABLE", 503);
        if (bp == Guid.Empty || !IsField(field)) throw Failure("BP_FINANCE_REFERENCE_INPUT_INVALID", 400);
        var revision = bpRevision == null ? (long?)null : ParseBpRevision(bpRevision);
        return owner.ExecuteAsync(OperationsOwnerV1.BusinessPartner, "query", false, async (access, token) =>
        {
            await access.RequireBusinessPartner(db, bp, token);
            if (context is null || context == Guid.Empty) return Unproven();
            owner.RequireOriginalFinanceActor();
            var fields = await access.ActionAsync(OperationsOwnerV1.BusinessPartner, ReadAction, token);
            if (!fields.CanReadBusinessPartnerFinance(field)) return new("NOT_AUTHORIZED");
            if (!await FenceAsync(token)) return Unproven();
            var ledger = await finance.RequireContextAsync(context.Value, access.ActorId, token);
            var partner = await PartnerAsync(access, bp, revision, token);
            var selected = await LookupAsync(bp, field, partner, ledger, token);
            var tx = db.Database.CurrentTransaction!.GetDbTransaction();
            access.AtCommit(async final =>
            {
                RequireTransaction(tx);
                var latestLedger = await finance.RequireContextAsync(context.Value, access.ActorId, final);
                if (await LookupAsync(bp, field, partner, latestLedger, final) != selected)
                    throw Failure("BP_FINANCE_SOURCE_CHANGED", 412);
            });
            return selected.View;
        }, ct);
    }

    internal Task<BusinessPartnerFinanceCurrentV1> EnlistAsync(OperationsAccessV1 consumer, DbTransaction transaction,
        Guid bp, Guid context, string expectedBpRevision, IReadOnlyList<BusinessPartnerFinanceUseV1> references, CancellationToken ct)
        => ValidateCurrentAsync(consumer, transaction, bp, context, expectedBpRevision, references, null, ct);

    // 资料域核对准确财务原件；本调用不为当前交易签发使用资格。
    internal Task<BusinessPartnerFinanceCurrentV1> ValidateProfileReferencesAsync(OperationsAccessV1 consumer, DbTransaction transaction,
        Guid bp, Guid context, string legal, string expectedBpRevision, IReadOnlyList<BusinessPartnerFinanceUseV1> references, CancellationToken ct)
        => ValidateCurrentAsync(consumer, transaction, bp, context, expectedBpRevision, references, legal, ct);

    private async Task<BusinessPartnerFinanceCurrentV1> ValidateCurrentAsync(OperationsAccessV1 consumer, DbTransaction transaction,
        Guid bp, Guid context, string expectedBpRevision, IReadOnlyList<BusinessPartnerFinanceUseV1> references, string? profileLegal, CancellationToken ct)
    {
        RequireTransaction(transaction);
        owner.RequireOriginalFinanceActor();
        var revision = ParseBpRevision(expectedBpRevision);
        var requested = references.ToArray();
        if (bp == Guid.Empty || context == Guid.Empty || requested.Length == 0 || requested.Length > 3
            || requested.Any(r => r is null || !IsField(r.Field) || r.Reference is null)
            || requested.Select(r => r.Field).Distinct(StringComparer.Ordinal).Count() != requested.Length)
            return new("UNPROVEN", false, "REFERENCE_SET_UNPROVEN");

        var access = await consumer.SourceAsync(OperationsOwnerV1.BusinessPartner, ct);
        await access.RequireBusinessPartner(db, bp, ct);
        var fields = await consumer.ActionAsync(OperationsOwnerV1.BusinessPartner, ReadAction, ct);
        if (requested.Any(r => !fields.CanReadBusinessPartnerFinance(r.Field)))
            return new("DENIED", false, "NOT_AUTHORIZED");
        if (!await FenceAsync(ct)) return new("UNPROVEN", false, "SOURCE_UNAVAILABLE");
        var ledger = await finance.RequireContextAsync(context, access.ActorId, ct);
        if (profileLegal is not null && ledger.LegalEntityKey != profileLegal)
            return new("UNPROVEN", false, "SOURCE_SCOPE_UNPROVEN");
        var partner = await PartnerAsync(access, bp, null, ct, allowOwnedProfileAdvance: profileLegal is not null);
        if (partner.Revision != revision || profileLegal is null && !partner.CurrentlyActive)
            return new("DENIED", false, "PARTNER_CHANGED");

        var guardRevision = await GuardRevisionAsync(ledger.Id, ct);
        if (guardRevision is null) return new("UNPROVEN", false, "SOURCE_SCOPE_UNPROVEN");
        var selected = new List<Snapshot>(requested.Length);
        foreach (var request in requested)
        {
            var snapshot = await LookupAsync(bp, request.Field, partner, ledger, ct);
            if (snapshot.View.Availability != "FOUND" || snapshot.View.Reference != request.Reference)
                return new("UNPROVEN", false, snapshot.View.Reason ?? "ORIGINAL_UNPROVEN");
            if (snapshot.View.Enabled != true)
                return new("DENIED", false, snapshot.View.Reason ?? "SOURCE_NOT_CURRENT");
            selected.Add(snapshot);
        }
        consumer.AtCommit(async final =>
        {
            RequireTransaction(transaction);
            var latestLedger = await finance.RequireContextAsync(context, access.ActorId, final);
            if (await GuardRevisionAsync(latestLedger.Id, final) != guardRevision)
                throw Failure("BP_FINANCE_SOURCE_CHANGED", 412);
            for (var i = 0; i < requested.Length; i++)
                if (await LookupAsync(bp, requested[i].Field, partner, latestLedger, final) != selected[i])
                    throw Failure("BP_FINANCE_SOURCE_CHANGED", 412);
        });
        return new("VALID", true, Observations: selected.Select(s => s.View).ToArray(), GuardRevision: Text(guardRevision.Value));
    }

    internal async Task<BusinessPartnerFinanceReferenceViewV1> ReadProfileReferenceAsync(OperationsAccessV1 consumer,
        DbTransaction transaction, Guid bp, Guid context, string legal, string field, CancellationToken ct)
    {
        RequireTransaction(transaction);
        owner.RequireOriginalFinanceActor();
        var access = await consumer.SourceAsync(OperationsOwnerV1.BusinessPartner, ct);
        await access.RequireBusinessPartner(db, bp, ct);
        var fields = await consumer.ActionAsync(OperationsOwnerV1.BusinessPartner, ReadAction, ct);
        if (!fields.CanReadBusinessPartnerFinance(field)) return new("NOT_AUTHORIZED");
        if (!await FenceAsync(ct)) return Unproven();
        var ledger = await finance.RequireContextAsync(context, access.ActorId, ct);
        if (ledger.LegalEntityKey != legal) return Unproven();
        var partner = await PartnerAsync(access, bp, null, ct);
        var original = await LookupAsync(bp, field, partner, ledger, ct);
        consumer.AtCommit(async final =>
        {
            RequireTransaction(transaction);
            var latest = await finance.RequireContextAsync(context, access.ActorId, final);
            if (latest.LegalEntityKey != legal || await LookupAsync(bp, field, partner, latest, final) != original)
                throw Failure("BP_FINANCE_SOURCE_CHANGED", 412);
        });
        return original.View;
    }

    internal async Task<BpFinanceLinkReadBodyV1?> ReadFinanceLinkInUowAsync(OperationsAccessV1 consumer,
        DbTransaction transaction, Guid bp, Guid context, string legalEntityKey, string field, CancellationToken ct)
    {
        RequireTransaction(transaction);
        owner.RequireOriginalFinanceActor();
        if (bp == Guid.Empty || context == Guid.Empty || !IsField(field))
            throw Failure("BP_FINANCE_REFERENCE_INPUT_INVALID", 400);
        var access = await consumer.SourceAsync(OperationsOwnerV1.BusinessPartner, ct);
        var fields = await consumer.ActionAsync(OperationsOwnerV1.BusinessPartner, ReadAction, ct);
        await access.RequireBusinessPartner(db, bp, ct);
        if (!fields.CanReadBusinessPartnerFinance(field))
            throw Failure("FIELD_READ_FORBIDDEN", 403);
        if (!await FenceAsync(ct, linksOnly: true)) throw Failure("BP_FINANCE_LINK_UNPROVEN", 503);
        var ledger = await finance.RequireContextAsync(context, access.ActorId, ct);
        if (ledger.LegalEntityKey != legalEntityKey) throw Failure("WRONG_SUBJECT", 404);
        var partner = await PartnerAsync(access, bp, null, ct);
        var link = await FindLinkAsync(bp, field, partner.Revision, ct);
        if (link?.Reference is not { } reference || link.Ledger != ledger.Id)
            return null;
        if (!MatchesField(field, reference, bp)) throw Failure("BP_FINANCE_LINK_UNPROVEN", 412);
        consumer.AtCommit(async final =>
        {
            RequireTransaction(transaction);
            await finance.RequireContextAsync(context, access.ActorId, final);
            if (await FindLinkAsync(bp, field, partner.Revision, final) != link)
                throw Failure("BP_FINANCE_SOURCE_CHANGED", 412);
        });
        return new(bp, ledger.LegalEntityKey, field["finance.".Length..], reference, Text(partner.Revision), DateTime.UtcNow, "REAL");
    }

    private async Task<Partner> PartnerAsync(OperationsAccessV1 access, Guid bp, long? exact, CancellationToken ct,
        bool allowOwnedProfileAdvance = false)
    {
        // 复用伙伴 Owner 的原版本与提交前身份复核，冻结/退休不依赖本读取器。
        var view = await BusinessPartnerIdentityOwnerV1.ReadOwnedAsync(db, access, bp, exact, ct, allowOwnedProfileAdvance);
        var active = view.IsCurrent && view.SourcePresent
            && view.Source.GetProperty("status").GetInt32() == 1
            && !view.Source.GetProperty("isDeleted").GetBoolean()
            && !view.Source.GetProperty("isFrozen").GetBoolean();
        return new(ParseBpRevision(view.Reference.Version), active);
    }

    private async Task<bool> FenceAsync(CancellationToken ct, bool linksOnly = false)
    {
        const string savepoint = "cp6_bp_finance_source_fence";
        var tx = db.Database.CurrentTransaction!;
        await tx.CreateSavepointAsync(savepoint, ct);
        try
        {
            // 旧写者在同一物理库登记前沿。来源锁繁忙由读取方及时返回，释放伙伴门。
            await db.Database.ExecuteSqlRawAsync(linksOnly
                ? """
                    LOCK TABLE public."T_WebBusinessPartner",cp6_fin_mfg_v1.ledgers,
                        cp6_fin_mfg_v1.members,cp6_fin_mfg_v1.contexts,cp6_bp_finance_ref_v1.links IN SHARE MODE NOWAIT
                    """
                : """
                LOCK TABLE public."T_WebBusinessPartner",public."Fin_BankAccount",
                    cp6_fin_mfg_v1.ledgers,cp6_fin_mfg_v1.members,cp6_fin_mfg_v1.contexts,
                    cp6_bp_finance_ref_v1.scopes,cp6_bp_finance_ref_v1.links,
                    cp6_bp_finance_ref_v1.versions,cp6_bp_finance_ref_v1.frontiers,
                    cp6_bp_finance_ref_v1.guards IN SHARE MODE NOWAIT
                """, ct);
        }
        catch (PostgresException error) when (error.SqlState is PostgresErrorCodes.LockNotAvailable or PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.InvalidSchemaName)
        {
            await tx.RollbackToSavepointAsync(savepoint, ct);
            await tx.ReleaseSavepointAsync(savepoint, ct);
            return false;
        }
        await tx.ReleaseSavepointAsync(savepoint, ct);
        return true;
    }

    private Task<long?> GuardRevisionAsync(Guid ledger, CancellationToken ct)
        => db.Database.SqlQuery<long?>($"""
            SELECT revision AS "Value" FROM cp6_bp_finance_ref_v1.guards
            WHERE tenant_id={db.CurrentTenantId} AND ledger_id={ledger}
            """).SingleOrDefaultAsync(ct);

    private void RequireTransaction(DbTransaction transaction)
    {
        if (!owner.Owns(db) || !db.Database.IsNpgsql() || db.Database.CurrentTransaction is not { } native
            || !ReferenceEquals(native.GetDbTransaction(), transaction)
            || !ReferenceEquals(transaction.Connection, db.Database.GetDbConnection()))
            throw Failure("AUTHORITY_FENCE_UNAVAILABLE", 503);
    }

    private Task<LinkRow?> FindLinkAsync(Guid bp, string field, long revision, CancellationToken ct)
        => db.Database.SqlQuery<LinkRow>($"""
            SELECT bp_revision AS "Revision", ledger_id AS "Ledger", owner AS "Owner", source_id AS "Id",
                version AS "Version", digest AS "Digest", purpose AS "Purpose"
            FROM cp6_bp_finance_ref_v1.links
            WHERE tenant_id={db.CurrentTenantId} AND partner_id={bp} AND field={field} AND bp_revision<={revision}
            ORDER BY bp_revision DESC LIMIT 1
            """).SingleOrDefaultAsync(ct);

    private async Task<Snapshot> LookupAsync(Guid bp, string field, Partner partner, Ledger ledger, CancellationToken ct)
    {
        var tenant = db.CurrentTenantId;
        var link = await FindLinkAsync(bp, field, partner.Revision, ct);
        if (link?.Reference is not { } reference || link.Ledger != ledger.Id)
            return new(Unproven(), null, null);
        if (!MatchesField(field, reference, bp))
            return new(Unproven("NOT_THIS_KIND"), link.Revision, null);

        var source = await db.Database.SqlQuery<SourceRow>($"""
            SELECT f.generation AS "Generation", f.version AS "CurrentVersion", f.availability AS "Availability",
                f.enabled AND (f.effective_from IS NULL OR f.effective_from<={ledger.BusinessDate})
                    AND (f.effective_until IS NULL OR f.effective_until>{ledger.BusinessDate}) AS "Enabled",
                v.masked_account AS "MaskedAccount", v.amount::text AS "Amount", v.currency AS "Currency",
                v.observed_at AS "AsOf",
                CASE f.owner
                    WHEN {LegacyBankOwner} THEN
                        CASE WHEN f.availability='ABSENT' THEN NOT EXISTS (
                            SELECT FROM public."Fin_BankAccount" b WHERE b."TenantId"={tenant} AND b."Id"=v.source_id::uuid)
                        ELSE EXISTS (
                            SELECT FROM public."Fin_BankAccount" b WHERE b."TenantId"={tenant} AND b."Id"=v.source_id::uuid
                            AND cp6_bp_finance_ref_v1.bank_proof(to_jsonb(b),live.native_nonce)=live.envelope::jsonb->>'accountCommitment')
                        END
                    WHEN {LegacyCreditOwner} THEN EXISTS (
                        SELECT FROM public."T_WebBusinessPartner" p WHERE p."TenantId"={tenant} AND p."Id"={bp}
                        AND p."CreditLimit" IS NOT DISTINCT FROM live.amount)
                    ELSE true
                END AS "MatchesSource"
            FROM cp6_bp_finance_ref_v1.scopes s
            JOIN cp6_bp_finance_ref_v1.versions v USING(tenant_id,owner,source_id)
            JOIN cp6_bp_finance_ref_v1.frontiers f USING(tenant_id,owner,source_id)
            JOIN cp6_bp_finance_ref_v1.versions live ON (live.tenant_id,live.owner,live.source_id,live.version)=
                (f.tenant_id,f.owner,f.source_id,f.version)
            WHERE s.tenant_id={tenant} AND s.ledger_id={ledger.Id} AND (s.partner_id IS NULL OR s.partner_id={bp})
                AND (v.publisher_ledger_id IS NULL OR v.publisher_ledger_id=s.ledger_id)
                AND (live.publisher_ledger_id IS NULL OR live.publisher_ledger_id=s.ledger_id)
                AND v.owner={reference.Owner} AND v.source_id={reference.Id} AND v.version={reference.Version}
                AND v.digest={reference.Digest} AND v.purpose={reference.Purpose}
            """).SingleOrDefaultAsync(ct);
        if (source is null || source.Availability == "OFFLINE" || !source.MatchesSource || !await CaptureAvailableAsync(reference.Owner, ct))
            return new(Unproven(), link.Revision, source?.Generation);
        // 历史记录当前可见时仍保持原版本。后继、停用与旧额度观察均不构成当前可采用的证明。
        var reason = source.Availability == "ABSENT" ? "SOURCE_REMOVED"
            : reference.Version != source.CurrentVersion ? "REFERENCE_VERSION_CHANGED"
            : reference.Owner == LegacyCreditOwner ? "LEGACY_CREDIT_OBSERVATION"
            : reference.Owner == LegacyBankOwner && (source.Currency == null || source.MaskedAccount == null) ? "SOURCE_INCOMPLETE"
            : !source.Enabled ? "SOURCE_NOT_CURRENT" : null;
        var projection = new BusinessPartnerFinanceReferenceViewV1("FOUND", reference, reason is null, reason,
            ledger.Scope(tenant), Text(partner.Revision), source.MaskedAccount, Money(source.Amount), source.Currency, source.AsOf);
        return new(projection, link.Revision, source.Generation);
    }

    private Task<bool> CaptureAvailableAsync(string sourceOwner, CancellationToken ct)
        => sourceOwner switch
        {
            LegacyBankOwner => db.Database.SqlQueryRaw<bool>("""
                SELECT count(*)=2 AS "Value" FROM pg_catalog.pg_trigger
                WHERE tgrelid='public."Fin_BankAccount"'::regclass
                AND tgname IN ('bp_finance_capture_bank','bp_finance_no_truncate_bank')
                AND tgenabled IN ('O','A') AND NOT tgisinternal
                """).SingleAsync(ct),
            LegacyCreditOwner => db.Database.SqlQueryRaw<bool>("""
                SELECT count(*)=2 AS "Value" FROM pg_catalog.pg_trigger
                WHERE tgrelid='public."T_WebBusinessPartner"'::regclass
                AND tgname IN ('bp_finance_capture_credit','business_partner_identity_no_truncate')
                AND tgenabled IN ('O','A') AND NOT tgisinternal
                """).SingleAsync(ct),
            _ => Task.FromResult(true)
        };

    private static bool MatchesField(string field, BusinessPartnerFinanceSourceRefV1 reference, Guid bp)
        => (field, reference.Owner, reference.Purpose) switch
        {
            (ReceiptBank, LegacyBankOwner, "BP_RECEIPT_BANK_REFERENCE") => IsNativeId(reference.Id),
            (PaymentBank, "FIN-MASTER", "BP_PAYMENT_BANK_REFERENCE") => IsNativeId(reference.Id) && IsNativeId(reference.Version),
            (CreditMaster, "FIN-CREDIT", "BP_CREDIT_MASTER_REFERENCE") => true,
            (CreditMaster, LegacyCreditOwner, "BP_LEGACY_CREDIT_LIMIT_OBSERVATION") => reference.Id == bp.ToString("D"),
            _ => false
        };

    private static bool IsNativeId(string value) => Guid.TryParseExact(value, "D", out var id) && id != Guid.Empty
        && id.ToString("D") == value;
    private static bool IsField(string field) => field is ReceiptBank or PaymentBank or CreditMaster;
    private static BusinessPartnerFinanceReferenceViewV1 Unproven(string? reason = null) => new("UNPROVEN", Reason: reason);
    private static OrderPriceCorrectionException Failure(string code, int status) => new(code, status);
    private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);
    internal static long ParseBpRevision(string value) => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var version)
        && version > 0 && Text(version) == value ? version : throw Failure("BP_FINANCE_REFERENCE_INPUT_INVALID", 400);
    private static string? Money(string? value) => value?.Contains('.') == true ? value.TrimEnd('0').TrimEnd('.') : value;

    private sealed record Partner(long Revision, bool CurrentlyActive);
    private sealed record Snapshot(BusinessPartnerFinanceReferenceViewV1 View, long? LinkRevision, long? Generation);

    private sealed record LinkRow
    {
        public long Revision { get; init; }
        public Guid Ledger { get; init; }
        public string? Owner { get; init; }
        public string? Id { get; init; }
        public string? Version { get; init; }
        public string? Digest { get; init; }
        public string? Purpose { get; init; }
        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public BusinessPartnerFinanceSourceRefV1? Reference => Owner == null ? null : new(Owner, Id!, Version!, Digest!, Purpose!);
    }

    private sealed class SourceRow
    {
        public long Generation { get; init; }
        public string CurrentVersion { get; init; } = "";
        public string Availability { get; init; } = "";
        public bool Enabled { get; init; }
        public bool MatchesSource { get; init; }
        public string? MaskedAccount { get; init; }
        public string? Amount { get; init; }
        public string? Currency { get; init; }
        public DateTimeOffset AsOf { get; init; }
    }
}
