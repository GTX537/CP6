using System.Text.Json;
using CP6.Core.Auth;
using CP6.Core.Services.CrmIdentity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace CP6.Core.Services.Platform;

public partial class TenantAdminService
{
    private async Task<(Guid Store, int Version)?> DeadlineBoundaryForUpdate(
        TenantManagementSnapshotV1 current, DateTime? expiry, CancellationToken ct)
    {
        if (!current.Enable || current.ExpireDate == expiry || _crmPause?.IsRegistered(current.Id) != true)
            return null;

        var boundary = await _crmPause.ReadSuspensionBoundaryAsync(_db, current.Id, ct);
        var aggregate = $"tenant:{current.Id:D}";
        var payload = await _db.CrmIdentitySnapshots.AsNoTracking()
            .Where(row => row.TenantId == current.Id && row.AggregateId == aggregate && row.Version == boundary.Version)
            .Select(row => row.PayloadJson).SingleAsync(ct);
        var previous = JsonSerializer.Deserialize<TenantIdentityData>(payload, IdentityEventContracts.Json)!;
        DateTimeOffset? newExpiry = expiry is { } local ? new DateTimeOffset(local).ToUniversalTime() : null;
        return previous.Enabled && previous.ExpiresAtUtc != newExpiry ? boundary : null;
    }

    private async Task UpdateRegisteredLegacyAsync(Guid id, TenantConfigurationV1 configuration, CancellationToken ct)
    {
        var command = await ManagementTransaction<TenantManagementCommandV1?>(true, null, async (actor, token) =>
        {
            var current = await ManagementSnapshot(id, token) ?? throw new InvalidOperationException("E-SEC-032");
            var pending = await TenantPendingSuspensionV1.ReadAsync(_db, id, token);
            if (pending is not null)
            {
                var request = PendingCommand(pending) with { Kind = "update", Configuration = configuration };
                RequirePendingIntent(pending, request, ManagementBytes(request), actor, legacy: true);
                return request;
            }

            var timeZone = NormalizeManagementTimeZone(configuration.TimeZoneId);
            if (current.Enable && FitsLegacyStorage(configuration.Name, 200) && FitsLegacyStorage(configuration.Remark, 500) &&
                FitsLegacyStorage(timeZone, 64))
            {
                var expiry = configuration.ExpireDate;
                if (expiry is { } requested && requested != current.ExpireDate)
                    expiry = await LegacyStoredExpiry(requested, token);
                if (await DeadlineBoundaryForUpdate(current, expiry, token) is { } source)
                {
                    var request = new TenantManagementCommandV1(
                        LegacyDeadlineOperation(source.Store.ToString("D"), source.Version), current.Revision, "update", configuration);
                    await StageManagementIntent(id, request, ManagementBytes(request), actor, source, legacy: true, token);
                    return request;
                }

                if (await IsCompletedLegacyDeadline(current, configuration, expiry, timeZone, actor, token))
                    return null;
            }

            await UpdateCoreAsync(id, configuration.Name, configuration.ExpireDate, configuration.Remark, timeZone, actor, token);
            return null;
        }, ct);
        if (command is not null)
            await CompleteStagedManagement(id, command, ManagementBytes(command), legacy: true, null, ct);
    }

    private Task<DateTime?> LegacyStoredExpiry(DateTime requested, CancellationToken ct)
    {
        // Bind as Sys_Tenants.ExpireDate to preserve the legacy PostgreSQL microsecond projection.
        var parameter = new NpgsqlParameter("expiry", NpgsqlDbType.Timestamp) { Value = requested };
        return _db.Database.SqlQueryRaw<DateTime?>("""SELECT @expiry AS "Value" """, parameter).SingleAsync(ct);
    }

    private static bool FitsLegacyStorage(string? value, int capacity)
        => value is null || value.Length <= capacity && !value.Contains('\0');

    private static string LegacyDeadlineOperation(string store, int sourceVersion)
        => FormattableString.Invariant($"deadline/{store}/{sourceVersion}");

    private async Task<bool> IsCompletedLegacyDeadline(TenantManagementSnapshotV1 current, TenantConfigurationV1 configuration,
        DateTime? expiry, string? timeZone, PlatformAdminAuthorization.Actor actor, CancellationToken ct)
    {
        if (current.TenantName != configuration.Name || current.ExpireDate != expiry ||
            current.Remark != configuration.Remark || current.TimeZoneId != timeZone)
            return false;

        // A legacy request has no client key. Its exact completed result is recoverable only while
        // the original Main revision and the successor of the same CRM source are still current.
        var aggregate = $"tenant:{current.Id:D}";
        var version = await _db.CrmIdentitySnapshots.AsNoTracking()
            .Where(row => row.TenantId == current.Id && row.AggregateId == aggregate && !row.IsDeleted && row.Version > 1)
            .Select(row => (int?)row.Version).SingleOrDefaultAsync(ct);
        if (version is not int successor)
            return false;

        var binding = (await _crmPause!.ReadBindingAsync(_db, current.Id, ct))!;
        var operation = LegacyDeadlineOperation(binding[..binding.IndexOf('/')], successor - 1);
        var original = await ManagementOriginal(current.Id, operation, ct, legacy: true);
        if (original is null || original.Actor != actor.Id || original.ActorTenant != actor.TenantId)
            return false;
        var request = new TenantManagementCommandV1(operation, current.Revision - 1, "update", configuration);
        if (!original.Input.AsSpan().SequenceEqual(ManagementBytes(request)))
            return false;
        return ManagementReceipt(original, actor, operation, null).Snapshot == current;
    }
}
