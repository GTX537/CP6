using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Net;

namespace CP6.Core.Services.Platform;

public sealed class CrmInquiryTenantPauseBindingV1
{
    public Guid StoreId { get; set; }
    public string ConnectionString { get; set; } = "";
}

/// <summary>Records authority changes in the registered CRM store before the Main source commits.</summary>
public sealed class CrmInquiryTenantPauseV1 : IAsyncDisposable
{
    private const string AdvanceIdentitySql = "SELECT crm_inquiry_tenant_pause_v1.advance_identity_v1(@tenant,@store,@aggregate,@version)";
    private readonly Dictionary<Guid, (Guid StoreId, NpgsqlDataSource Source)> bindings = [];

    public CrmInquiryTenantPauseV1(IReadOnlyDictionary<Guid, CrmInquiryTenantPauseBindingV1> configuration)
    {
        var configured = configuration.Select(pair =>
        {
            var (tenant, binding) = pair;
            if (tenant == Guid.Empty || binding.StoreId == Guid.Empty || string.IsNullOrWhiteSpace(binding.ConnectionString))
                throw new InvalidOperationException("CRM_TENANT_PAUSE_BINDING_INVALID");
            var connection = new NpgsqlConnectionStringBuilder(binding.ConnectionString)
            {
                Enlist = false,
                Timeout = 5,
                CommandTimeout = 8,
                ApplicationName = "cp6-main-inquiry-tenant-pause-v1"
            };
            var local = IPAddress.TryParse(connection.Host, out var address) && IPAddress.IsLoopback(address);
            if (string.IsNullOrWhiteSpace(connection.Database) || string.IsNullOrWhiteSpace(connection.Username) ||
                connection.SslMode != SslMode.VerifyFull && !local)
                throw new InvalidOperationException("CRM_TENANT_PAUSE_BINDING_INVALID");
            return (tenant, binding.StoreId, connection.ConnectionString);
        }).ToArray();
        foreach (var (tenant, storeId, connection) in configured)
            bindings.Add(tenant, (storeId, NpgsqlDataSource.Create(connection)));
    }

    internal bool IsRegistered(Guid tenant) => bindings.ContainsKey(tenant);

    internal Guid? RegisteredStore(Guid tenant) => bindings.TryGetValue(tenant, out var binding) ? binding.StoreId : null;

    internal async Task<(Guid Store, int Version)> ReadSuspensionBoundaryAsync(CP6Context authority, Guid tenant, CancellationToken ct)
    {
        if (!bindings.TryGetValue(tenant, out var binding))
            throw new TenantManagementExceptionV1("AUTHORITY_FENCE_UNAVAILABLE", 503);
        RequireIdentityCapture(authority, tenant);
        return (binding.StoreId, await CurrentTenantVersionAsync(authority, tenant, ct));
    }

    internal async Task CloseSuspensionAsync(CP6Context authority, Guid tenant, Guid store, int version, CancellationToken ct)
    {
        if (await ReadSuspensionBoundaryAsync(authority, tenant, ct) != (store, version))
            throw new TenantManagementExceptionV1("AUTHORITY_FENCE_UNAVAILABLE", 503);
        await SetBoundaryAsync(bindings[tenant], tenant, version, "SELECT crm_inquiry_tenant_pause_v1.close_v1(@tenant,@store,@version)", ct);
    }

    internal async Task ReopenAsync(CP6Context authority, Guid tenant, CancellationToken ct)
    {
        if (!bindings.TryGetValue(tenant, out var binding)) return;
        var (_, version) = await ReadSuspensionBoundaryAsync(authority, tenant, ct);
        if (version == int.MaxValue)
            throw new TenantManagementExceptionV1("TENANT_REVISION_EXHAUSTED", 422);

        // The source tenant is still disabled when reserving a reactivation version. Main
        // cannot issue for that version until it commits the activation and its C02 snapshot.
        await SetBoundaryAsync(binding, tenant, version + 1, "SELECT crm_inquiry_tenant_pause_v1.reopen_v1(@tenant,@store,@version)", ct);
    }

    private static async Task SetBoundaryAsync((Guid StoreId, NpgsqlDataSource Source) binding, Guid tenant,
        int boundary, string sql, CancellationToken ct)
    {
        try
        {
            // The CRM commit is a prerequisite. A lost acknowledgement permits repeating the
            // same boundary, while an uncommitted Main attempt cannot roll the CRM boundary back.
            await using var db = await binding.Source.OpenConnectionAsync(ct);
            await using var tx = await db.BeginTransactionAsync(ct);
            await using var durability = new NpgsqlCommand("""
                SELECT set_config('synchronous_commit','on',true)
                WHERE current_setting('synchronous_commit') IN ('off','local')
                """, db, tx);
            await durability.ExecuteNonQueryAsync(ct);
            await using var command = new NpgsqlCommand(sql, db, tx);
            command.Parameters.AddWithValue("tenant", tenant);
            command.Parameters.AddWithValue("store", binding.StoreId);
            command.Parameters.AddWithValue("version", boundary);
            if (await command.ExecuteScalarAsync(ct) is not int recorded || recorded != boundary)
                throw new TenantManagementExceptionV1("AUTHORITY_FENCE_UNAVAILABLE", 503);
            await tx.CommitAsync(ct);
        }
        catch (Exception error) when (error is NpgsqlException or TimeoutException)
        {
            throw new TenantManagementExceptionV1("AUTHORITY_FENCE_UNAVAILABLE", 503, error);
        }
    }

    internal async Task RequireDeadlineCaptureAsync(CP6Context authority, Guid tenant, CancellationToken ct)
    {
        if (!bindings.ContainsKey(tenant)) return;
        RequireIdentityCapture(authority, tenant);
        await CurrentTenantVersionAsync(authority, tenant, ct);
    }

    private static void RequireIdentityCapture(CP6Context authority, Guid tenant)
    {
        try { authority.RequireIdentityCapture(tenant); }
        catch (InvalidOperationException error) { throw new TenantManagementExceptionV1("AUTHORITY_FENCE_UNAVAILABLE", 503, error); }
    }

    internal async Task<string?> ReadBindingAsync(CP6Context authority, Guid tenant, CancellationToken ct)
    {
        if (!bindings.TryGetValue(tenant, out var binding)) return null;
        var version = await CurrentTenantVersionAsync(authority, tenant, ct);
        return FormattableString.Invariant($"{binding.StoreId:D}/{version}");
    }

    // CRM records the authority boundary while the Main source transaction is still open.
    internal Task AdvanceIdentityAsync(Guid tenant, string aggregate, int version, bool role, CancellationToken ct)
        => AdvanceVersionAsync(tenant, version, role
            ? "SELECT crm_inquiry_tenant_pause_v1.advance_role_v1(@tenant,@store,@aggregate,@version)"
            : AdvanceIdentitySql, ct, aggregate);

    internal Task AdvanceDeadlineAsync(Guid tenant, int version, CancellationToken ct)
        => AdvanceVersionAsync(tenant, version,
            "SELECT crm_inquiry_tenant_pause_v1.advance_deadline_v1(@tenant,@store,@version)", ct);

    internal Task AdvanceDepartmentAsync(Guid tenant, Guid department, int version, CancellationToken ct)
        => AdvanceVersionAsync(tenant, version,
            "SELECT crm_inquiry_tenant_pause_v1.advance_department_v1(@tenant,@store,@aggregate,@version)", ct, department);

    private async Task AdvanceVersionAsync(Guid tenant, int version, string sql, CancellationToken ct, object? aggregate = null)
    {
        if (!bindings.TryGetValue(tenant, out var binding)) return;
        try
        {
            await using var db = await binding.Source.OpenConnectionAsync(ct);
            await AdvanceVersionAsync(db, tenant, binding.StoreId, version, sql, ct, aggregate);
        }
        catch (Exception error) when (error is NpgsqlException or TimeoutException)
        {
            throw new TenantManagementExceptionV1("AUTHORITY_FENCE_UNAVAILABLE", 503, error);
        }
    }

    internal async Task<NpgsqlConnection> OpenSealAsync(Guid tenant, Guid store, CancellationToken ct)
    {
        if (!bindings.TryGetValue(tenant, out var binding) || binding.StoreId != store)
            throw new TenantManagementExceptionV1("AUTHORITY_FENCE_UNAVAILABLE", 503);
        try { return await binding.Source.OpenConnectionAsync(ct); }
        catch (Exception error) when (error is NpgsqlException or TimeoutException)
        { throw new TenantManagementExceptionV1("AUTHORITY_FENCE_UNAVAILABLE", 503, error); }
    }

    internal static Task AdvanceConnectedRoleAsync(NpgsqlConnection connection, Guid tenant, Guid store,
        string aggregate, int version, CancellationToken ct)
        => AdvanceConnectedVersionAsync(connection, tenant, store, aggregate, version,
            "SELECT crm_inquiry_tenant_pause_v1.advance_role_v1(@tenant,@store,@aggregate,@version)", ct);

    internal static Task AdvanceConnectedIdentityAsync(NpgsqlConnection connection, Guid tenant, Guid store,
        string aggregate, int version, CancellationToken ct)
        => AdvanceConnectedVersionAsync(connection, tenant, store, aggregate, version, AdvanceIdentitySql, ct);

    internal static async Task AdvanceConnectedIdentitiesAsync(NpgsqlConnection connection, Guid tenant, Guid store,
        IReadOnlyList<(string Aggregate, int Version)> advances, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            await using var tx = await connection.BeginTransactionAsync(deadline.Token);
            await SetCommitDurabilityAsync(connection, tx, deadline.Token);
            foreach (var (aggregate, version) in advances)
                await AdvanceVersionInTransactionAsync(connection, tx, tenant, store, version, AdvanceIdentitySql, deadline.Token, aggregate);
            await tx.CommitAsync(deadline.Token);
        }
        catch (Exception error) when (error is NpgsqlException or TimeoutException ||
                                      error is OperationCanceledException && !ct.IsCancellationRequested)
        { throw new TenantManagementExceptionV1("AUTHORITY_FENCE_UNAVAILABLE", 503, error); }
    }

    private static async Task AdvanceConnectedVersionAsync(NpgsqlConnection connection, Guid tenant, Guid store,
        string aggregate, int version, string sql, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            await AdvanceVersionAsync(connection, tenant, store, version, sql, deadline.Token, aggregate);
        }
        catch (Exception error) when (error is NpgsqlException or TimeoutException ||
                                      error is OperationCanceledException && !ct.IsCancellationRequested)
        { throw new TenantManagementExceptionV1("AUTHORITY_FENCE_UNAVAILABLE", 503, error); }
    }

    private static async Task AdvanceVersionAsync(NpgsqlConnection db, Guid tenant, Guid store, int version, string sql,
        CancellationToken ct, object? aggregate)
    {
        await using var tx = await db.BeginTransactionAsync(ct);
        await SetCommitDurabilityAsync(db, tx, ct);
        await AdvanceVersionInTransactionAsync(db, tx, tenant, store, version, sql, ct, aggregate);
        await tx.CommitAsync(ct);
    }

    private static async Task SetCommitDurabilityAsync(NpgsqlConnection db, NpgsqlTransaction tx, CancellationToken ct)
    {
        await using var durability = new NpgsqlCommand("SET LOCAL synchronous_commit='on'", db, tx);
        await durability.ExecuteNonQueryAsync(ct);
    }

    private static async Task AdvanceVersionInTransactionAsync(NpgsqlConnection db, NpgsqlTransaction tx, Guid tenant, Guid store,
        int version, string sql, CancellationToken ct, object? aggregate)
    {
        await using var command = new NpgsqlCommand(sql, db, tx);
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("store", store);
        if (aggregate is not null) command.Parameters.AddWithValue("aggregate", aggregate);
        command.Parameters.AddWithValue("version", version);
        if (await command.ExecuteScalarAsync(ct) is not int recorded || recorded != version)
            throw new TenantManagementExceptionV1("AUTHORITY_FENCE_UNAVAILABLE", 503);
    }

    private static async Task<int> CurrentTenantVersionAsync(CP6Context authority, Guid tenant, CancellationToken ct)
    {
        var aggregate = $"tenant:{tenant:D}";
        var version = await authority.CrmIdentitySnapshots.AsNoTracking()
            .Where(row => row.TenantId == tenant && row.AggregateId == aggregate && !row.IsDeleted)
            .Select(row => (int?)row.Version).SingleOrDefaultAsync(ct);
        if (version is null or <= 0) throw new TenantManagementExceptionV1("AUTHORITY_FENCE_UNAVAILABLE", 503);
        return version.Value;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var binding in bindings.Values) await binding.Source.DisposeAsync();
    }
}
