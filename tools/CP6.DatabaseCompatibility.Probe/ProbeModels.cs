using System.Data.Common;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.DatabaseCompatibility.Probe;

internal class TokenRow
{
    public Guid Id { get; set; }
    public string Value { get; set; } = "";
    public string? LeaseOwner { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

internal sealed class ApplicationTokenRow : TokenRow { }

internal sealed class BusinessRecord
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Value { get; set; } = "";
    public byte[] RowVersion { get; set; } = [];
}

internal sealed class OutboxRecord
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid BusinessId { get; set; }
    public string Value { get; set; } = "";
    public byte[] RowVersion { get; set; } = [];
}

internal sealed class DirectoryRecord
{
    public Guid TenantId { get; set; }
    public long Position { get; set; }
    public long EventVersion { get; set; }
    public string Value { get; set; } = "";
    public bool IsDeleted { get; set; }
}

internal sealed class TenantGeneration
{
    public Guid TenantId { get; set; }
    public long Generation { get; set; }
}

internal abstract class ProbeContext : DbContext
{
    protected ProbeContext(DbConnection connection, ProbeProvider provider, string schema, IInterceptor? interceptor = null) : base(Options(connection, provider, interceptor))
    {
        Provider = provider;
        Schema = schema;
    }

    internal ProbeProvider Provider { get; }
    internal string Schema { get; }

    private static DbContextOptions Options(DbConnection connection, ProbeProvider provider, IInterceptor? interceptor)
    {
        var builder = new DbContextOptionsBuilder().ReplaceService<IModelCacheKeyFactory, ProbeModelCacheKeyFactory>();
        if (provider == ProbeProvider.PostgreSql) builder.UseNpgsql(connection);
        else builder.UseSqlServer(connection);
        if (interceptor is not null) builder.AddInterceptors(interceptor);
        return builder.Options;
    }

    protected void Token<TEntity>(ModelBuilder builder, string table) where TEntity : class
    {
        var entity = builder.Entity<TEntity>().ToTable(table, Schema);
        entity.HasKey("Id");
        var property = entity.Property<byte[]>("RowVersion");
        if (Provider == ProbeProvider.SqlServer) property.IsRowVersion();
        else
        {
            property.HasColumnType("bytea").IsConcurrencyToken().ValueGeneratedOnAddOrUpdate();
            property.Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Ignore);
            property.Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        }
    }
}

internal sealed class ProbeModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) => context is ProbeContext probe
        ? (context.GetType(), probe.Provider, probe.Schema, designTime) : (object)(context.GetType(), designTime);
}

internal sealed class DatabaseTokenContext(DbConnection connection, ProbeProvider provider, string schema) : ProbeContext(connection, provider, schema)
{
    public DbSet<TokenRow> Rows => Set<TokenRow>();
    protected override void OnModelCreating(ModelBuilder modelBuilder) => Token<TokenRow>(modelBuilder, "DatabaseTokenRows");
}

internal sealed class ApplicationTokenContext(DbConnection connection, string schema) : ProbeContext(connection, ProbeProvider.PostgreSql, schema)
{
    public DbSet<ApplicationTokenRow> Rows => Set<ApplicationTokenRow>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApplicationTokenRow>().ToTable("ApplicationTokenRows", Schema).HasKey(row => row.Id);
        modelBuilder.Entity<ApplicationTokenRow>().Property(row => row.RowVersion).HasColumnType("bytea").IsConcurrencyToken().ValueGeneratedNever();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Deliberately test the common SaveChanges-only candidate against actual bypass writers.
        foreach (var entry in ChangeTracker.Entries<ApplicationTokenRow>().Where(entry => entry.State is EntityState.Added or EntityState.Modified))
            entry.Entity.RowVersion = RandomNumberGenerator.GetBytes(8);
        return base.SaveChangesAsync(cancellationToken);
    }
}

internal sealed class BusinessContext(DbConnection connection, ProbeProvider provider, string schema) : ProbeContext(connection, provider, schema)
{
    public DbSet<BusinessRecord> Records => Set<BusinessRecord>();
    protected override void OnModelCreating(ModelBuilder modelBuilder) => Token<BusinessRecord>(modelBuilder, "BusinessRecords");
}

internal sealed class OutboxContext(DbConnection connection, ProbeProvider provider, string schema) : ProbeContext(connection, provider, schema)
{
    public DbSet<OutboxRecord> Records => Set<OutboxRecord>();
    protected override void OnModelCreating(ModelBuilder modelBuilder) => Token<OutboxRecord>(modelBuilder, "OutboxRecords");
}
