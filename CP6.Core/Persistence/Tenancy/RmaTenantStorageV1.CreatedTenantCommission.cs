using System.Security.Cryptography;
using System.Text.Json;
using CP6.Core.Services.Platform;
using Npgsql;

namespace CP6.Core.Persistence.Tenancy;

internal sealed record RmaCreatedTenantLoginV2(string CreationOperationRef, Guid CreatorTenantId, Guid CreatorActorId, string Login);

internal sealed record RmaCreatedTenantCoreCommissionRequestV2(
    RmaTenantTargetV1 Target, Guid InstallationId, Guid OperationId, RmaCreatedTenantLoginV2[] Creations);

internal static partial class RmaTenantStorageV1
{
    private const string CoreCreationRejected = "core-tenant-creation-rejected";

    internal static async Task<string> PrepareCoreFromCreationAsync(NpgsqlConnection connection,
        RmaCreatedTenantCoreCommissionRequestV2 request, CancellationToken ct)
    {
        var plan = CanonicalCreatedTenantPlan(request);
        await connection.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await RequireControlTarget(connection, tx, request.Target, ct);
        await Sql(connection, tx, $"SELECT pg_catalog.pg_advisory_xact_lock({TenantLoginDirectoryV1.DirectoryLockNamespace},4)", ct);
        if (await CorePreparationExistsAsync(connection, tx, ct))
            return (await ReadCreatedTenantPreparationAsync(connection, tx, plan, ct)).Prepared;

        await RequireFreshCorePreparationAsync(connection, tx, ct);
        await CreateCoreControlAsync(connection, tx, ct);
        var result = CorePreparedReceipt(request.Target, request.InstallationId, request.OperationId);
        await using var command = new NpgsqlCommand($"""
            ALTER TABLE {CoreControl}.installation ADD COLUMN creation_request_json text NOT NULL;
            INSERT INTO {CoreControl}.installation (singleton,request_json,prepared_receipt,creation_request_json)
            VALUES (true,'null',@receipt,@plan);
            """, connection, tx);
        command.Parameters.AddWithValue("receipt", result);
        command.Parameters.AddWithValue("plan", plan);
        await command.ExecuteNonQueryAsync(ct);
        await Commit(tx, ct);
        return result;
    }

    internal static async Task<string> CommissionCoreFromCreationAsync(NpgsqlConnection connection,
        RmaCreatedTenantCoreCommissionRequestV2 request, CancellationToken ct)
    {
        var plan = CanonicalCreatedTenantPlan(request);
        await connection.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await RequireControlTarget(connection, tx, request.Target, ct);
        await Sql(connection, tx, $"SELECT pg_catalog.pg_advisory_xact_lock({TenantLoginDirectoryV1.DirectoryLockNamespace},4)", ct);
        if (!await CorePreparationExistsAsync(connection, tx, ct))
            throw new TenantProvisioningException("core-preparation-required");
        var (resolved, _, previous) = await ReadCreatedTenantPreparationAsync(connection, tx, plan, ct);
        var history = await LockCoreCommissionAsync(connection, tx, ct);
        if (previous is not null)
        {
            var original = JsonSerializer.Deserialize<RmaCoreCommissionRequestV1>(resolved);
            if (original is null || original.Target != request.Target || original.InstallationId != request.InstallationId
                || original.OperationId != request.OperationId || original.Tenants is not { } bindings
                || bindings.Length != request.Creations.Length)
                throw new TenantProvisioningException("core-preparation-conflict");
            TenantLoginDirectoryV1.ValidateArguments(connection, tx, bindings);
            if (CanonicalCoreRequest(original) != resolved || !bindings.Select(x => x.Login).Order(StringComparer.Ordinal)
                .SequenceEqual(request.Creations.Select(x => x.Login).Order(StringComparer.Ordinal), StringComparer.Ordinal))
                throw new TenantProvisioningException("core-preparation-conflict");
            return await ApplyCoreCommissionAsync(connection, tx, original, previous, history, ct);
        }
        if (resolved != "null") throw new TenantProvisioningException("core-preparation-conflict");

        var tenants = await ResolveCreatedTenantsAsync(connection, tx, request.Creations, ct);
        var commission = new RmaCoreCommissionRequestV1(request.Target, request.InstallationId, request.OperationId, tenants);

        // Existing journal, writers and archive readers use this exact V1-shaped binding.
        // Publish it in the same transaction as the original Ready records and commission receipt.
        await using var save = new NpgsqlCommand($"""
            UPDATE {CoreControl}.installation SET request_json=@request
            WHERE singleton AND request_json='null' AND commission_receipt IS NULL AND creation_request_json=@plan
            """, connection, tx);
        save.Parameters.AddWithValue("request", CanonicalCoreRequest(commission));
        save.Parameters.AddWithValue("plan", plan);
        if (await save.ExecuteNonQueryAsync(ct) != 1)
            throw new TenantProvisioningException("core-preparation-conflict");
        return await ApplyCoreCommissionAsync(connection, tx, commission, null, history, ct);
    }

    private static string CanonicalCreatedTenantPlan(RmaCreatedTenantCoreCommissionRequestV2 request)
    {
        if (request.InstallationId == Guid.Empty || request.OperationId == Guid.Empty
            || request.Creations is not { Length: >= 1 and <= 128 }
            || request.Creations.Any(x => x is null || x.CreatorTenantId == Guid.Empty || x.CreatorActorId == Guid.Empty
                || !TenantLoginDirectoryV1.IsLoginName(x.Login))
            || request.Creations.Select(x => x.CreationOperationRef).Distinct(StringComparer.Ordinal).Count() != request.Creations.Length
            || request.Creations.Select(x => x.Login).Distinct(StringComparer.Ordinal).Count() != request.Creations.Length)
            throw new TenantProvisioningException("input-rejected");
        foreach (var creation in request.Creations)
        {
            try { TenantAdminService.ManagementOperation(creation.CreationOperationRef); }
            catch (TenantManagementExceptionV1) { throw new TenantProvisioningException("input-rejected"); }
        }
        return JsonSerializer.Serialize(request with
        {
            Creations = request.Creations.OrderBy(x => x.CreationOperationRef, StringComparer.Ordinal).ToArray()
        });
    }

    private static async Task<(string Resolved, string Prepared, string? Commissioned)> ReadCreatedTenantPreparationAsync(
        NpgsqlConnection connection, NpgsqlTransaction tx, string plan, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand($"""
            SELECT request_json,prepared_receipt,commission_receipt FROM {CoreControl}.installation i
            WHERE singleton AND pg_catalog.to_jsonb(i)->>'creation_request_json'=@plan
            """, connection, tx);
        command.Parameters.AddWithValue("plan", plan);
        await using var row = await command.ExecuteReaderAsync(ct);
        if (!await row.ReadAsync(ct)) throw new TenantProvisioningException("core-preparation-conflict");
        return (row.GetString(0), row.GetString(1), row.IsDBNull(2) ? null : row.GetString(2));
    }

    private static async Task<TenantLoginV1[]> ResolveCreatedTenantsAsync(NpgsqlConnection connection, NpgsqlTransaction tx,
        IReadOnlyCollection<RmaCreatedTenantLoginV2> creations, CancellationToken ct)
    {
        if (await Scalar(connection, tx, """
                SELECT count(*)=2 FROM pg_catalog.pg_class
                WHERE relkind='r' AND relowner=current_user::regrole
                  AND oid IN (pg_catalog.to_regclass('cp6_tenant_creation_v1.commands'),
                              pg_catalog.to_regclass('public."Sys_Tenants"'))
                """, ct) is not true)
            throw new TenantProvisioningException(CoreCreationRejected);

        var tenants = new List<TenantLoginV1>(creations.Count);
        var identities = new HashSet<Guid>();
        foreach (var creation in creations)
        {
            var operation = TenantAdminService.ManagementBytes(creation.CreationOperationRef);
            await using var lookup = new NpgsqlCommand("""
                SELECT result_bytes FROM cp6_tenant_creation_v1.commands
                WHERE operation_hash=@hash AND operation_bytes=@operation AND actor_tenant=@tenant AND actor_id=@actor
                """, connection, tx);
            lookup.Parameters.AddWithValue("hash", SHA256.HashData(operation));
            lookup.Parameters.AddWithValue("operation", operation);
            lookup.Parameters.AddWithValue("tenant", creation.CreatorTenantId);
            lookup.Parameters.AddWithValue("actor", creation.CreatorActorId);
            if (await lookup.ExecuteScalarAsync(ct) is not byte[] saved)
                throw new TenantProvisioningException(CoreCreationRejected);
            TenantCreationReceiptV1? receipt;
            try { receipt = JsonSerializer.Deserialize<TenantCreationReceiptV1>(saved); }
            catch (JsonException) { throw new TenantProvisioningException(CoreCreationRejected); }
            if (receipt is null || receipt.OperationRef != creation.CreationOperationRef || receipt.TenantId == Guid.Empty
                || !identities.Add(receipt.TenantId))
                throw new TenantProvisioningException(CoreCreationRejected);

            await using var current = new NpgsqlCommand("""
                SELECT true FROM public."Sys_Tenants" WHERE "Id"=@tenant FOR KEY SHARE
                """, connection, tx);
            current.Parameters.AddWithValue("tenant", receipt.TenantId);
            if (await current.ExecuteScalarAsync(ct) is not true)
                throw new TenantProvisioningException(CoreCreationRejected);
            tenants.Add(new(receipt.TenantId, creation.Login));
        }
        return tenants.ToArray();
    }
}
