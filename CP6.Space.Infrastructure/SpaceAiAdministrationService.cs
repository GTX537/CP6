using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Space.Infrastructure;

public sealed class SpaceAiAdministrationService(
    SpaceContext context,
    ISpaceExecutionContext execution,
    ISpaceClock clock,
    IWarehouseGenerationProviderRegistry providers) :
    ISpaceAiAdministrationService,
    ISpaceAiTenantPolicySource
{
    private const string UpdateOperation = "space.ai-policy.update";
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<SpaceAiPolicyDto> GetPolicyAsync(
        CancellationToken cancellationToken = default)
    {
        RequireInternalTenant();
        var entity = await CurrentPolicyQuery()
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
        return entity is null ? DisabledDto() : ToDto(entity);
    }

    async Task<SpaceAiTenantPolicy> ISpaceAiTenantPolicySource.GetPolicyAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var currentTenant = RequireTenant(allowExternal: true);
        if (tenantId != currentTenant)
            throw new SpaceTenantScopeException("A cross-tenant AI policy read was rejected.");
        var entity = await CurrentPolicyQuery()
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
        if (entity is null ||
            entity.DataPolicy == nameof(SpaceAiDataPolicy.Disabled))
        {
            return SpaceAiTenantPolicy.Disabled(tenantId);
        }

        return SpaceAiTenantPolicy.Enabled(
            tenantId,
            Enum.Parse<SpaceAiDataPolicy>(entity.DataPolicy),
            Deserialize<Guid[]>(entity.AllowedSiteIdsJson),
            Deserialize<string[]>(entity.AllowedProviderAliasesJson),
            entity.MaxConcurrentRuns,
            entity.ExternalProviderEnabled,
            new SpaceAiBudgetLimits(
                entity.DailyBudgetMinor,
                entity.MonthlyBudgetMinor,
                entity.Currency));
    }

    public async Task<UpdateSpaceAiPolicyResponse> UpdatePolicyAsync(
        UpdateSpaceAiPolicyRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tenantId = RequireInternalTenant();
        var actorId = RequireActor();
        var normalized = Normalize(request);
        var requestHash = Hash(JsonSerializer.Serialize(normalized, JsonOptions));
        var keyHash = IdempotencyKeyHash(idempotencyKey);

        var replay = await ReadReplayAsync(
            keyHash,
            requestHash,
            cancellationToken);
        if (replay is not null)
            return replay;

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        try
        {
            var concurrentReplay = await ReadReplayAsync(
                keyHash,
                requestHash,
                cancellationToken);
            if (concurrentReplay is not null)
            {
                if (transaction is not null)
                    await transaction.CommitAsync(cancellationToken);
                return concurrentReplay;
            }

            var current = await CurrentPolicyQuery()
                .SingleOrDefaultAsync(cancellationToken);
            var currentVersion = current?.Version ?? 0;
            if (normalized.ExpectedVersion != currentVersion)
                throw Conflict();

            var now = RequireUtcNow();
            current?.Deactivate();
            if (transaction is not null && current is not null)
                await context.SaveChangesAsync(cancellationToken);
            var next = SpaceAiTenantPolicyConfiguration.Create(
                tenantId,
                checked(currentVersion + 1),
                normalized.DataPolicy,
                JsonSerializer.Serialize(normalized.AllowedSiteIds, JsonOptions),
                JsonSerializer.Serialize(
                    normalized.AllowedProviderAliases,
                    JsonOptions),
                normalized.MaxConcurrentRuns,
                normalized.ExternalProviderEnabled,
                normalized.DailyBudgetMinor,
                normalized.MonthlyBudgetMinor,
                normalized.Currency,
                actorId,
                now);
            context.AiTenantPolicies.Add(next);

            var response = new UpdateSpaceAiPolicyResponse(
                ToDto(next),
                IdempotentReplay: false);
            context.IdempotencyRecords.Add(SpaceIdempotencyRecord.Create(
                tenantId,
                actorId,
                UpdateOperation,
                keyHash,
                requestHash,
                JsonSerializer.Serialize(response, JsonOptions),
                200,
                now.AddHours(24),
                now.AddDays(90)));
            await context.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return response;
        }
        catch
        {
            if (transaction is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<SpaceAiUsagePageDto> GetUsageAsync(
        SpaceAiUsageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        RequireInternalTenant();
        var normalized = Normalize(query);
        var usage = context.AiUsageRecords.AsNoTracking()
            .Where(item =>
                item.RecordedAtUtc >= normalized.FromUtc &&
                item.RecordedAtUtc < normalized.ToUtc);
        if (normalized.ProviderAlias is not null)
        {
            usage = usage.Where(item =>
                item.ProviderCode == normalized.ProviderAlias);
        }
        if (normalized.Outcome.HasValue)
            usage = usage.Where(item => item.Outcome == normalized.Outcome);

        var total = await usage.LongCountAsync(cancellationToken);
        var totals = await usage
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Input = group.Sum(item => item.InputUnits),
                Output = group.Sum(item => item.OutputUnits),
                Estimated = group.Sum(item => item.EstimatedCostMinor),
                Actual = group.Sum(item => item.ActualCostMinor ?? 0),
                HasUnpriced = group.Any(item => item.Currency == null),
            })
            .SingleOrDefaultAsync(cancellationToken);
        var items = await usage
            .OrderByDescending(item => item.RecordedAtUtc)
            .ThenByDescending(item => item.Id)
            .Skip((normalized.Page - 1) * normalized.PageSize)
            .Take(normalized.PageSize)
            .Select(item => new SpaceAiUsageItemDto(
                item.Id,
                item.RunId,
                item.ProviderCode,
                item.ProviderModel,
                item.InputUnits,
                item.OutputUnits,
                item.EstimatedCostMinor,
                item.ActualCostMinor,
                item.Currency,
                item.LatencyMs,
                item.Outcome.ToString(),
                item.RecordedAtUtc))
            .ToArrayAsync(cancellationToken);

        var policy = await CurrentPolicyQuery()
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
        var now = RequireUtcNow();
        var today = DateOnly.FromDateTime(now);
        var month = now.Year * 100 + now.Month;
        var reservations = await context.AiBudgetReservations
            .AsNoTracking()
            .Where(item =>
                item.PeriodDay == today || item.PeriodMonth == month)
            .Select(item => new
            {
                item.PeriodDay,
                item.PeriodMonth,
                item.ReservedCostMinor,
                item.ActualCostMinor,
                item.Status,
                item.Currency,
            })
            .ToArrayAsync(cancellationToken);
        static long Effective(
            long reserved,
            long? actual,
            SpaceAiBudgetReservationStatus status) =>
            status == SpaceAiBudgetReservationStatus.Released
                ? 0
                : status is SpaceAiBudgetReservationStatus.Reported or
                    SpaceAiBudgetReservationStatus.Reconciled
                    ? actual ?? reserved
                    : reserved;
        var currency = policy?.Currency;
        var dailyConsumed = reservations
            .Where(item => item.PeriodDay == today &&
                (currency is null || item.Currency == currency))
            .Sum(item => Effective(
                item.ReservedCostMinor,
                item.ActualCostMinor,
                item.Status));
        var monthlyConsumed = reservations
            .Where(item => item.PeriodMonth == month &&
                (currency is null || item.Currency == currency))
            .Sum(item => Effective(
                item.ReservedCostMinor,
                item.ActualCostMinor,
                item.Status));

        var summary = new SpaceAiUsageSummaryDto(
            total,
            totals?.Input ?? 0,
            totals?.Output ?? 0,
            totals?.Estimated ?? 0,
            totals?.Actual ?? 0,
            totals?.HasUnpriced ?? false,
            Balance(policy?.DailyBudgetMinor, dailyConsumed, currency),
            Balance(policy?.MonthlyBudgetMinor, monthlyConsumed, currency));
        return new SpaceAiUsagePageDto(
            items,
            total,
            normalized.Page,
            normalized.PageSize,
            summary);
    }

    private IQueryable<SpaceAiTenantPolicyConfiguration> CurrentPolicyQuery() =>
        context.AiTenantPolicies.Where(item => item.IsActive);

    private SpaceAiPolicyDto DisabledDto() =>
        new(
            0,
            nameof(SpaceAiDataPolicy.Disabled),
            [],
            [],
            SpaceAiTenantPolicy.PlatformMaxConcurrentRuns,
            false,
            null,
            null,
            null,
            ApprovedProviders(),
            null,
            null);

    private SpaceAiPolicyDto ToDto(SpaceAiTenantPolicyConfiguration entity) =>
        new(
            entity.Version,
            entity.DataPolicy,
            Deserialize<Guid[]>(entity.AllowedSiteIdsJson),
            Deserialize<string[]>(entity.AllowedProviderAliasesJson),
            entity.MaxConcurrentRuns,
            entity.ExternalProviderEnabled,
            entity.DailyBudgetMinor,
            entity.MonthlyBudgetMinor,
            entity.Currency,
            ApprovedProviders(),
            entity.UpdatedAtUtc,
            entity.UpdatedBy);

    private SpaceAiApprovedProviderDto[] ApprovedProviders() =>
        providers.Registrations
            .Select(item => new SpaceAiApprovedProviderDto(
                item.Alias,
                item.Kind.ToString()))
            .ToArray();

    private UpdateSpaceAiPolicyRequest Normalize(UpdateSpaceAiPolicyRequest request)
    {
        if (request.ExpectedVersion < 0)
            throw Invalid("ExpectedVersion cannot be negative.");
        if (!Enum.TryParse<SpaceAiDataPolicy>(
                request.DataPolicy?.Trim(),
                ignoreCase: true,
                out var dataPolicy) ||
            !Enum.IsDefined(dataPolicy))
        {
            throw Invalid("DataPolicy is unsupported.");
        }
        if (request.MaxConcurrentRuns is < 1 or
            > SpaceAiTenantPolicy.PlatformMaxConcurrentRuns)
        {
            throw Invalid("MaxConcurrentRuns must be between 1 and 3.");
        }

        var sites = (request.AllowedSiteIds ?? [])
            .Where(id => id != Guid.Empty)
            .Distinct()
            .Order()
            .ToArray();
        string[] aliases;
        try
        {
            aliases = (request.AllowedProviderAliases ?? [])
                .Select(WarehouseGenerationProviderRegistration.NormalizeAlias)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
        }
        catch (ArgumentException exception)
        {
            throw Invalid(exception.Message);
        }

        var registrations = aliases.Select(alias =>
        {
            if (!providers.TryGet(alias, out var registration) ||
                registration is null)
            {
                throw new SpaceProblemException(
                    SpaceErrorCodes.AiProviderAliasNotApproved,
                    422,
                    "The selected AI provider alias is not deployment-approved.",
                    recoveryAction: "select-approved-provider-alias");
            }
            return registration;
        }).ToArray();
        if (dataPolicy != SpaceAiDataPolicy.Disabled &&
            (sites.Length == 0 || aliases.Length == 0))
        {
            throw Invalid(
                "An enabled policy requires at least one site and approved provider alias.");
        }
        var hasExternal = registrations.Any(item =>
            item.Kind == WarehouseGenerationProviderKind.External);
        if (hasExternal != request.ExternalProviderEnabled)
        {
            throw Invalid(
                "ExternalProviderEnabled must match the selected external provider aliases.");
        }

        SpaceAiBudgetLimits budgets;
        try
        {
            budgets = new SpaceAiBudgetLimits(
                request.DailyBudgetMinor,
                request.MonthlyBudgetMinor,
                request.Currency).Validate();
        }
        catch (ArgumentException exception)
        {
            throw Invalid(exception.Message);
        }
        return request with
        {
            DataPolicy = dataPolicy.ToString(),
            AllowedSiteIds = sites,
            AllowedProviderAliases = aliases,
            Currency = budgets.Currency,
        };
    }

    private NormalizedUsageQuery Normalize(SpaceAiUsageQuery query)
    {
        var to = query.ToUtc ?? RequireUtcNow();
        var from = query.FromUtc ?? to.AddDays(-30);
        if (from.Kind != DateTimeKind.Utc || to.Kind != DateTimeKind.Utc ||
            from >= to || to - from > TimeSpan.FromDays(366) ||
            query.Page < 1 || query.PageSize is < 1 or > 100)
        {
            throw UsageInvalid();
        }
        SpaceAiUsageOutcome? outcome = null;
        if (!string.IsNullOrWhiteSpace(query.Outcome))
        {
            if (!Enum.TryParse<SpaceAiUsageOutcome>(
                    query.Outcome.Trim(),
                    ignoreCase: true,
                    out var parsed) ||
                !Enum.IsDefined(parsed))
            {
                throw UsageInvalid();
            }
            outcome = parsed;
        }
        var alias = query.ProviderAlias?.Trim();
        if (alias?.Length > 64 || alias?.Any(char.IsControl) == true)
            throw UsageInvalid();
        return new NormalizedUsageQuery(
            from,
            to,
            string.IsNullOrEmpty(alias) ? null : alias,
            outcome,
            query.Page,
            query.PageSize);
    }

    private async Task<UpdateSpaceAiPolicyResponse?> ReadReplayAsync(
        string keyHash,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var actor = RequireActor();
        var record = await context.IdempotencyRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(item =>
                item.PrincipalId == actor &&
                item.Operation == UpdateOperation &&
                item.IdempotencyKeyHash == keyHash,
                cancellationToken);
        if (record is null)
            return null;
        if (record.RequestHash != requestHash ||
            record.ReplayUntilUtc < RequireUtcNow())
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.IdempotencyConflict,
                409,
                "The Idempotency-Key was already used with different or expired input.",
                recoveryAction: "use-new-idempotency-key");
        }
        return (JsonSerializer.Deserialize<UpdateSpaceAiPolicyResponse>(
                    record.ResponseJson,
                    JsonOptions)
                ?? throw new InvalidOperationException(
                    "The AI policy idempotency response is invalid."))
            with { IdempotentReplay = true };
    }

    private string IdempotencyKeyHash(string key)
    {
        var normalized = key?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) ||
            Encoding.UTF8.GetByteCount(normalized) > 128 ||
            normalized.Any(char.IsControl))
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.IdempotencyKeyRequired,
                400,
                "A valid Idempotency-Key is required.",
                recoveryAction: "supply-idempotency-key");
        }
        return Hash($"{execution.TenantId:D}\n{UpdateOperation}\n{normalized}");
    }

    private async Task<IDbContextTransaction?> BeginTransactionAsync(
        CancellationToken cancellationToken) =>
        context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken)
            : null;

    private Guid RequireInternalTenant()
    {
        if (execution.IsExternal)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.ExternalSubjectDenied,
                403,
                "External principals cannot administer tenant AI policy or usage.",
                recoveryAction: "use-internal-tenant-administrator");
        }
        return RequireTenant(allowExternal: false);
    }

    private Guid RequireTenant(bool allowExternal)
    {
        if (!allowExternal && execution.IsExternal ||
            execution.TenantId == Guid.Empty ||
            context.CurrentTenantId != execution.TenantId)
        {
            throw new SpaceTenantScopeException(
                "A verified Space tenant context is required.");
        }
        return execution.TenantId;
    }

    private Guid RequireActor() =>
        execution.ActorId != Guid.Empty
            ? execution.ActorId
            : throw new SpaceTenantScopeException(
                "A verified Space actor is required.");

    private DateTime RequireUtcNow()
    {
        var now = clock.UtcNow;
        if (now.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("The Space clock must return UTC.");
        return now;
    }

    private static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, JsonOptions)
        ?? throw new InvalidOperationException("Stored AI policy JSON is invalid.");

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static SpaceAiBudgetBalanceDto Balance(
        long? limit,
        long consumed,
        string? currency) =>
        new(
            limit,
            consumed,
            limit.HasValue ? Math.Max(0, limit.Value - consumed) : null,
            currency);

    private static SpaceProblemException Invalid(string detail) =>
        new(
            SpaceErrorCodes.AiPolicyInvalid,
            422,
            "The tenant AI policy is invalid.",
            detail,
            "correct-ai-policy");

    private static SpaceProblemException Conflict() =>
        new(
            SpaceErrorCodes.AiPolicyConflict,
            409,
            "The tenant AI policy changed before this update.",
            recoveryAction: "reload-ai-policy");

    private static SpaceProblemException UsageInvalid() =>
        new(
            SpaceErrorCodes.AiUsageQueryInvalid,
            422,
            "The AI usage query is invalid.",
            recoveryAction: "correct-ai-usage-query");

    private sealed record NormalizedUsageQuery(
        DateTime FromUtc,
        DateTime ToUtc,
        string? ProviderAlias,
        SpaceAiUsageOutcome? Outcome,
        int Page,
        int PageSize);
}
