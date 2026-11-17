using System.Data.Common;
using System.Text.Json;
using CP6.Core.EFDbContext;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Text.Json.Serialization;

namespace CP6.Core.Services.CrmIdentity;

/// <summary>来源映射独立于租户展示名、聚合版本和数据库物理位置；不含客户端秘密。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record IdentitySourceBindingV1(Guid TenantId, string Slug, string Issuer, string Region,
    string[] ServiceClientIds, string[] ReaderClientIds, string ContractBundleSha256);

public static class IdentitySourceAuthorityV1
{
    internal static byte[] Bytes<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, IdentityEventContracts.Json);
    internal static IdentitySourceBindingV1 Binding(CrmIdentityRuntime runtime, Guid tenant)
    {
        if (runtime.Options.SourceBindings.TryGetValue(tenant, out var binding)) return binding;
        // 旧直接构造入口没有组织 slug/逐租户服务登记；此记录不能用于批准迁移。
        var readers = runtime.Options.ProjectionReaderClientIds.Order(StringComparer.Ordinal).ToArray();
        return new(tenant, "", runtime.Options.Issuer, runtime.Options.Tenants[tenant], readers, readers,
            runtime.ContractBundleSha256);
    }

    internal static async Task<bool> Available(CP6Context db, CancellationToken ct)
        => db.Database.IsNpgsql() && await db.Database.SqlQueryRaw<bool>("""
            SELECT pg_catalog.to_regprocedure('cp6_identity_source_v1.read_hash(uuid)') IS NOT NULL AS "Value"
            """).SingleAsync(ct);

    internal static async Task EnrollInitialAsync(CP6Context db, CrmIdentityRuntime runtime, Guid tenant,
        bool newBootstrap, CancellationToken ct)
    {
        if (!await Available(db, ct))
        {
            if (runtime.Options.RequireSourceRegistration) throw new IdentityReadException("C02_SOURCE_SCHEMA_REQUIRED");
            return;
        }
        if (newBootstrap && runtime.Options.SourceBindings.ContainsKey(tenant))
            await EnrollFreshAsync(db, runtime, tenant, ct);
        await RequireCurrentAsync(db, runtime, tenant, ct);
    }

    internal static async Task RequireWriterAsync(CP6Context db, CrmIdentityRuntime runtime, Guid tenant, CancellationToken ct)
    {
        if (db.Database.IsNpgsql() && runtime.Options.RequireSourceRegistration)
            await EnrollFreshAsync(db, runtime, tenant, ct);
        await RequireCurrentAsync(db, runtime, tenant, ct);
    }

    private static async Task EnrollFreshAsync(CP6Context db, CrmIdentityRuntime runtime, Guid tenant, CancellationToken ct)
    {
        // 首条 C02 事实可早于 Bootstrap；历史初始化不完整也不能从启动配置重新认领旧事实。
        var bytes = Bytes(Binding(runtime, tenant));
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_identity_source_v1.registrations(tenant_id,revision,binding_bytes,binding_hash)
            SELECT {tenant},0,{bytes},sha256({bytes})
            WHERE NOT EXISTS(SELECT FROM crm_identity."Bootstrap" WHERE "TenantId"={tenant})
              AND NOT EXISTS(SELECT FROM crm_identity."Snapshot" WHERE "TenantId"={tenant})
              AND NOT EXISTS(SELECT FROM crm_identity."ServiceToken" WHERE "TenantId"={tenant})
              AND NOT EXISTS(SELECT FROM public."CrmOidcGrant" WHERE "OrganizationId"={tenant})
            ON CONFLICT(tenant_id) DO NOTHING
            """, ct);
    }

    public static async Task RequireCurrentAsync(CP6Context db, CrmIdentityRuntime runtime, Guid tenant,
        CancellationToken ct = default)
    {
        if (!runtime.Options.Tenants.ContainsKey(tenant)) throw new IdentityReadException("C02_BOOTSTRAP_NOT_READY");
        if (!await Available(db, ct))
        {
            if (runtime.Options.RequireSourceRegistration) throw new IdentityReadException("C02_SOURCE_SCHEMA_REQUIRED");
            return;
        }
        await RequireCurrentAsync(db.Database.GetDbConnection(), db.Database.CurrentTransaction?.GetDbTransaction(), runtime, tenant, ct);
    }

    internal static async Task<IDbContextTransaction?> ReadLeaseAsync(CP6Context db, CrmIdentityRuntime runtime, Guid tenant, CancellationToken ct)
    {
        var tx = db.Database.IsNpgsql() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct) : null;
        try { await RequireCurrentAsync(db, runtime, tenant, ct); return tx; }
        catch { if (tx is not null) await tx.DisposeAsync(); throw; }
    }

    public static async Task RequireCurrentAsync(DbConnection db, DbTransaction? tx, CrmIdentityRuntime runtime,
        Guid tenant, CancellationToken ct)
    {
        var expected = System.Security.Cryptography.SHA256.HashData(Bytes(Binding(runtime, tenant)));
        var current = await db.ExecuteScalarAsync<byte[]?>(new CommandDefinition(
            "SELECT cp6_identity_source_v1.read_hash(@tenant)", new { tenant }, tx, cancellationToken: ct));
        // 完整宿主要求历史租户先经明确批准登记；旧组件入口维持原来已冻结的适用范围。
        if (current is null)
        {
            if (runtime.Options.RequireSourceRegistration) throw new IdentityReadException("C02_SOURCE_ENROLLMENT_REQUIRED");
            return;
        }
        if (!current.AsSpan().SequenceEqual(expected)) throw new IdentityReadException("C02_SOURCE_NOT_APPROVED");
        if (tx is not null)
            await db.ExecuteAsync(new CommandDefinition("SELECT pg_catalog.set_config(@name,@value,true)",
                new { name = "cp6_identity_source.t_" + tenant.ToString("N"), value = Convert.ToHexString(expected).ToLowerInvariant() },
                tx, cancellationToken: ct));
    }
}
