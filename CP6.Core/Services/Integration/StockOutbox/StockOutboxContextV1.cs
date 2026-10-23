using CP6.Platform.EntityFramework;
using CP6.Platform.EntityFramework.OutboxV2;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Integration.StockOutbox;

/// <summary>New Main technical queue. Existing C02/ERP models and opaque tokens keep their original contract.</summary>
public sealed class StockOutboxContextV1(DbContextOptions<StockOutboxContextV1> options) : DbContext(options)
{
    public const string Schema = "cp6_stock_outbox_v1";
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.UseCollation("C");
        model.AddPostgreSqlOutboxV2(Schema);
        model.Ignore<Cp6InboxMessage>();
        model.Ignore<Cp6InboxAggregateCheckpoint>();
        var authority = model.Entity<StockOutboxAuthorityV1>();
        authority.ToTable("Authority", Schema).HasKey(a => a.RootId);
        authority.HasIndex(a => a.TenantId).IsUnique();
        authority.Property(a => a.Version).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
        var source = model.Entity<StockOutboxSourceV1>();
        source.ToTable("Source", Schema).HasKey(s => s.NativeId);
        source.HasIndex(s => new { s.TenantId, s.OperationId }).IsUnique();
        source.HasOne<Cp6OutboxMessage>().WithOne().HasForeignKey<StockOutboxSourceV1>(s => s.NativeId).OnDelete(DeleteBehavior.Restrict);
        var run = model.Entity<StockOutboxRunV1>();
        run.ToTable("DispatchRun", Schema).HasKey(r => new { r.RootId, r.RequestId });
        run.HasIndex(r => r.RootId).IsUnique().HasFilter("\"CompletedAtUtc\" IS NULL");
        run.HasOne<OutboxRouteControlV2>().WithMany().HasForeignKey(r => r.RootId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StockOutboxAuthorityV1
{
    public string RootId { get; set; } = "";
    public Guid TenantId { get; set; }
    public bool Enabled { get; set; } = true;
    public string PolicyVersion { get; set; } = StockOutboxProtocolV1.PolicyVersion;
    public string PublisherReference { get; set; } = StockOutboxProtocolV1.Publisher;
    public uint Version { get; set; }
}

internal sealed class StockOutboxSourceV1
{
    public Guid NativeId { get; set; }
    public Guid TenantId { get; set; }
    public Guid OperationId { get; set; }
    public string SourceOwner { get; set; } = StockOutboxProtocolV1.SourceOwner;
    public string ResultSha256 { get; set; } = "";
}

internal sealed class StockOutboxRunV1
{
    public string RootId { get; set; } = "";
    public Guid RequestId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
}
