using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace CP6.Core.Persistence.Tenancy;

internal sealed class RmaTenantReadContext(DbContextOptions<RmaTenantReadContext> options, TenantStorageBindingV1 binding)
    : DbContext(options)
{
    internal const string Provider = "Npgsql.EntityFrameworkCore.PostgreSQL";
    internal TenantStorageBindingV1 Binding { get; } = binding;
    internal bool UsesV2 => Binding.ModelVersion == RmaTenantStorageV1.CoreReadProfileV2;
    internal DbSet<RmaTenantHead> Heads => Set<RmaTenantHead>();
    internal DbSet<RmaTenantVersion> Versions => Set<RmaTenantVersion>();

    internal static DbContextOptionsBuilder<RmaTenantReadContext> Options(NpgsqlConnection connection) =>
        new DbContextOptionsBuilder<RmaTenantReadContext>().UseNpgsql(connection, contextOwnsConnection: false)
            .ReplaceService<IModelCacheKeyFactory, RmaTenantReadModelCacheKey>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        var heads = model.Entity<RmaTenantHead>();
        if (UsesV2)
            heads.ToView("application_heads_v2", Binding.SchemaName);
        else
            heads.ToTable("heads", Binding.SchemaName, table => table.ExcludeFromMigrations());
        heads.HasKey(row => new { row.TenantId, row.Id });
        heads.Property(row => row.TenantId).HasColumnName("tenant_id");
        heads.Property(row => row.Id).HasColumnName("id");
        heads.Property(row => row.ActorId).HasColumnName("actor_id");
        heads.Property(row => row.OutboundId).HasColumnName("outbound_id");
        heads.Property(row => row.Version).HasColumnName("version");

        var versions = model.Entity<RmaTenantVersion>();
        if (UsesV2)
            versions.ToView("application_versions_v2", Binding.SchemaName);
        else
            versions.ToTable("versions", Binding.SchemaName, table => table.ExcludeFromMigrations());
        versions.HasKey(row => new { row.TenantId, row.Id, row.Version });
        versions.Property(row => row.TenantId).HasColumnName("tenant_id");
        versions.Property(row => row.Id).HasColumnName("id");
        versions.Property(row => row.Version).HasColumnName("version");
        versions.Property(row => row.State).HasColumnName("state");
        versions.Property(row => row.Payload).HasColumnName("payload").HasColumnType("jsonb");
    }
}

internal sealed class RmaTenantReadModelCacheKey : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime)
    {
        var storage = (RmaTenantReadContext)context;
        var binding = storage.Binding;
        return (context.GetType(), RmaTenantReadContext.Provider, binding.LayoutVersion, binding.ModelVersion,
            binding.SchemaName, designTime);
    }
}

internal sealed class RmaTenantHead
{
    public Guid TenantId { get; init; }
    public Guid Id { get; init; }
    public Guid ActorId { get; init; }
    public Guid OutboundId { get; init; }
    public int Version { get; init; }
}

internal sealed class RmaTenantVersion
{
    public Guid TenantId { get; init; }
    public Guid Id { get; init; }
    public int Version { get; init; }
    public string State { get; init; } = null!;
    public string Payload { get; init; } = null!;
}
