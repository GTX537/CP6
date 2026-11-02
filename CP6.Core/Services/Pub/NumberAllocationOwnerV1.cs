using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Pub;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Pub;

/// <summary>Independent durable allocation. Every call owns a fresh context/transaction; it never
/// enlists the later business save. No public Next HTTP endpoint or implicit legacy cutover.</summary>
public sealed partial class NumberAllocationOwnerV1(Func<CP6Context> contexts, NumberAllocationScopeV1 scope,
    NumberAllocationAuthorityV1 authority)
{
    private static NumberAllocationExceptionV1 Error(string code, int status = 409) => new(code, status);
    private static void Text(string value)
    {
        if (string.IsNullOrEmpty(value)) throw Error("NUMBER_INPUT_REQUIRED", 400);
        try { _ = new UTF8Encoding(false, true).GetByteCount(value); }
        catch (EncoderFallbackException) { throw Error("NUMBER_INPUT_INVALID", 400); }
    }
    private async Task<T> Execute<T>(bool write, Func<CP6Context, NumberAllocationStoreV1, CancellationToken,
        Task<(T Value, Guid Actor, string Purpose)>> body, CancellationToken ct)
    {
        await using var db = contexts();
        if (db.CurrentTenantId != scope.TenantId || scope.TenantId == Guid.Empty) throw Error("NUMBER_TENANT_MISMATCH", 403);
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction != null || db.ChangeTracker.HasChanges()
            || System.Transactions.Transaction.Current != null) throw Error("NUMBER_INDEPENDENT_CONTEXT_REQUIRED", 503);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct); var committing = false;
        try
        {
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='10s'", ct);
            await AuthenticationCompletionGateV1.AcquireAsync(db, ct);
            var result = await body(db, new(db, scope), ct);
            if (await authority.Require(db, result.Purpose, ct) != result.Actor) throw Error("REAUTH_REQUIRED", 401);
            ct.ThrowIfCancellationRequested(); committing = true; await tx.CommitAsync(ct); return result.Value;
        }
        catch (Exception error)
        {
            if (committing && write) throw new NumberAllocationExceptionV1("NUMBER_OUTCOME_UNKNOWN", 503, error);
            try { await tx.RollbackAsync(CancellationToken.None); }
            catch (Exception rollback) { throw new NumberAllocationExceptionV1("NUMBER_OUTCOME_UNKNOWN", 503, new AggregateException(error, rollback)); }
            throw;
        }
    }
    private async Task<NumberAllocationRuleV1> Rule(NumberAllocationStoreV1 store, CancellationToken ct)
    {
        var registration = await store.RegistrationAsync(ct) ?? throw Error("E-PUB-051", 404);
        if (registration.Scope != scope) throw Error("NUMBER_OWNER_SCOPE_MISMATCH", 403);
        return await store.CurrentRule(ct) ?? registration.Rule;
    }
    public Task<bool> RegisterNewAsync(NumberAllocationRuleV1 rule, CancellationToken ct = default)
    {
        if (scope.RuleId == Guid.Empty || scope.CutoverBasis != "NEW_DOMAIN_NO_LEGACY_INTENTS") throw Error("NUMBER_NEW_DOMAIN_REQUIRED", 400);
        Text(scope.SourceOwner); Text(scope.BizKey); Text(scope.BusinessScope); Text(rule.Version);
        if (scope.BizKey.Length > 50 || rule.Prefix.Length > 20 || rule.DateFormat?.Length > 20 || rule.MinimumWidth < 0 || rule.FirstValue < 1)
            throw Error("RULE_INVALID", 422);
        _ = Format(rule, DateTimeOffset.UtcNow, rule.FirstValue);
        return Execute(true, async (db, store, token) =>
        {
            var actor = await authority.Require(db, "register", token);
            // One-time new-domain creation arbitrates with legacy INSERT/UPDATE/DELETE.
            await db.Database.ExecuteSqlRawAsync("LOCK TABLE public.\"Pub_DocSequence\" IN SHARE ROW EXCLUSIVE MODE", token);
            var original = await store.RegistrationAsync(token);
            if (original != null)
            {
                if (original.Scope != scope || original.Rule != rule) throw Error("NUMBER_REGISTRATION_CONFLICT");
                return (true, actor, "register");
            }
            if (await db.Pub_DocSequences.FromSqlInterpolated($"SELECT * FROM public.\"Pub_DocSequence\" WHERE \"TenantId\"={scope.TenantId} AND \"BizKey\"=CAST({scope.BizKey} AS bpchar)").AnyAsync(token))
                throw Error("NUMBER_EXISTING_DOMAIN_KEEP_OWNER");
            db.Pub_DocSequences.Add(new Pub_DocSequence { Id = scope.RuleId, BizKey = scope.BizKey, Prefix = rule.Prefix,
                DateFormat = rule.DateFormat, SeqLength = rule.MinimumWidth, ResetCycle = rule.ResetCycle, CurrentValue = 0, CurrentPeriod = "" });
            await db.SaveChangesAsync(token);
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_number_allocation_v1.scopes(rule_id,tenant_id,body) VALUES({scope.RuleId},{scope.TenantId},{NumberAllocationStoreV1.Encode(new NumberAllocationStoreV1.Registration(scope, rule))})", token);
            return (true, actor, "register");
        }, ct);
    }
    private static (string Period, string Number) Format(NumberAllocationRuleV1 rule, DateTimeOffset instant, int value)
    {
        try
        {
            var local = TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById(rule.TimeZone));
            var culture = CultureInfo.GetCultureInfo(rule.Culture);
            var period = rule.ResetCycle switch { 1 => local.ToString("yyyyMMdd", CultureInfo.InvariantCulture), 2 => local.ToString("yyyyMM", CultureInfo.InvariantCulture), 3 => local.ToString("yyyy", CultureInfo.InvariantCulture), _ => "" };
            var date = string.IsNullOrEmpty(rule.DateFormat) ? "" : local.DateTime.ToString(rule.DateFormat, culture);
            return (period, rule.Prefix + date + value.ToString(culture).PadLeft(rule.MinimumWidth, '0'));
        }
        catch (Exception e) when (e is FormatException or ArgumentException or TimeZoneNotFoundException or InvalidTimeZoneException)
        { throw new NumberAllocationExceptionV1("RULE_INVALID", 422, e); }
    }
    private static NumberAllocationReceiptV1 Original(NumberAllocationStoreV1.Saved original, Guid actor, string operation, byte[]? input)
    {
        if (original.Actor != actor) throw Error("NUMBER_ORIGINAL_FORBIDDEN", 403);
        if (!original.Operation.AsSpan().SequenceEqual(NumberAllocationStoreV1.Encode(operation))
            || input != null && !original.Input.AsSpan().SequenceEqual(input)) throw Error("NUMBER_COMMAND_CONFLICT");
        return JsonSerializer.Deserialize<NumberAllocationReceiptV1>(original.Result)!;
    }
    public Task<NumberAllocationReceiptV1> AllocateAsync(NumberAllocationCommandV1 command, CancellationToken ct = default)
    {
        Text(command.OperationRef); Text(command.ExpectedRuleVersion); Text(command.BusinessInputIdentity);
        return Execute(true, async (db, store, token) =>
        {
            // Current read permission controls disclosure even when a new request would fail.
            var actor = await authority.Require(db, "read", token); var rule = await Rule(store, token);
            var original = await store.Original(command.OperationRef, token);
            if (original != null) return (Original(original, actor, command.OperationRef, NumberAllocationStoreV1.Encode(command)), actor, "read");
            await authority.Require(db, "allocate", token);
            if (command.ExpectedRuleVersion != rule.Version) throw Error("NUMBER_RULE_VERSION_CHANGED", 412);
            var period = Format(rule, command.BusinessInstant, rule.FirstValue).Period;
            var previous = await store.Counter(period, token);
            if (previous == int.MaxValue) throw Error("SEQUENCE_EXHAUSTED", 422);
            var next = previous == null ? rule.FirstValue : checked(previous.Value + 1);
            var number = Format(rule, command.BusinessInstant, next).Number;
            await store.Advance(period, previous, next, token);
            var now = await db.Database.SqlQueryRaw<DateTimeOffset>("SELECT clock_timestamp() AS \"Value\"").SingleAsync(token);
            var receipt = new NumberAllocationReceiptV1(Guid.NewGuid(), scope.TenantId, scope.RuleId, scope.SourceOwner,
                command.OperationRef, rule.Version, period, next, number, now);
            await store.Save(command, actor, receipt, token);
            return (receipt, actor, "allocate");
        }, ct);
    }
    public Task<NumberAllocationReceiptV1?> ReadAsync(string operation, CancellationToken ct = default)
    {
        Text(operation);
        return Execute<NumberAllocationReceiptV1?>(false, async (db, store, token) =>
        {
            var actor = await authority.Require(db, "read", token); _ = await Rule(store, token);
            var original = await store.Original(operation, token);
            return (original == null ? null : Original(original, actor, operation, null), actor, "read");
        }, ct);
    }
    public Task<NumberAllocationPreviewV1> PreviewAsync(DateTimeOffset instant, CancellationToken ct = default)
        => Execute(false, async (db, store, token) =>
        {
            var actor = await authority.Require(db, "read", token); var rule = await Rule(store, token);
            var period = Format(rule, instant, rule.FirstValue).Period; var previous = await store.Counter(period, token);
            if (previous == int.MaxValue) throw Error("SEQUENCE_EXHAUSTED", 422);
            var sample = Format(rule, instant, previous == null ? rule.FirstValue : previous.Value + 1).Number;
            return (new NumberAllocationPreviewV1(sample, rule.Version, period, rule.TimeZone, previous == null), actor, "read");
        }, ct);
}
