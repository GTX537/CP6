using CP6.Platform.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.ErpIntegration;

/// <summary>Command Inbox and request journal share the ERP SQL connection/transaction with real master/order changes.</summary>
public sealed class ErpIntegrationContext(DbContextOptions<ErpIntegrationContext> options) : DbContext(options)
{
    public const string Schema = "erp_integration";
    public DbSet<ErpInboxReceipt> Inbox => Set<ErpInboxReceipt>();
    public DbSet<ErpIntegrationRequest> Requests => Set<ErpIntegrationRequest>();
    public DbSet<ErpIntegrationAggregate> Aggregates => Set<ErpIntegrationAggregate>();
    public DbSet<ErpOrderBridgeDispatch> OrderBridges => Set<ErpOrderBridgeDispatch>();
    public DbSet<ErpInboxReplayAudit> ReplayAudits => Set<ErpInboxReplayAudit>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.AddCp6TransactionalMessaging(Schema);
        model.Ignore<Cp6InboxMessage>();
        model.Ignore<Cp6InboxAggregateCheckpoint>();
        // The Platform projection Inbox skips <= aggregate checkpoints before the application callback.
        // ERP commands must check the business idempotency key/payload even at the same version.
        // Use an explicit command receipt and domain version guard, while retaining the Platform Outbox.
        model.Entity<ErpInboxReceipt>(e =>
        {
            e.ToTable("CommandInbox", Schema); e.HasKey(x => x.MessageId);
            e.Property(x => x.MessageId).HasMaxLength(128).UseCollation("Latin1_General_100_BIN2");
            e.Property(x => x.EventType).HasMaxLength(200).IsRequired();
            e.Property(x => x.Payload).IsRequired();
            e.Property(x => x.PayloadSha256).HasMaxLength(64).IsUnicode(false).IsRequired();
            e.Property(x => x.LastConflictSha256).HasMaxLength(64).IsUnicode(false);
            e.Property(x => x.ErrorCode).HasMaxLength(128).IsUnicode(false);
            e.Property(x => x.ReplayReasonCode).HasMaxLength(128).IsUnicode(false);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasIndex(x => new { x.TenantId, x.Status, x.RetryAtUtc });
        });
        model.Entity<ErpIntegrationRequest>(e =>
        {
            e.ToTable("Request", Schema); e.HasKey(x => new { x.TenantId, x.Kind, x.AggregateId, x.RequestVersion });
            e.HasIndex(x => new { x.TenantId, x.RequestId }).IsUnique();
            e.Property(x => x.InputSha256).HasMaxLength(64).IsUnicode(false).IsRequired();
            e.Property(x => x.ResultType).HasMaxLength(200).IsRequired();
            e.Property(x => x.ResultDataJson).IsRequired();
            e.Property(x => x.RowVersion).IsRowVersion();
        });
        model.Entity<ErpIntegrationAggregate>(e =>
        {
            e.ToTable("Aggregate", Schema); e.HasKey(x => new { x.TenantId, x.Kind, x.AggregateId });
            e.Property(x => x.RowVersion).IsRowVersion();
        });
        model.Entity<ErpOrderBridgeDispatch>(e =>
        {
            e.ToTable("OrderBridgeDispatch", Schema); e.HasKey(x => new { x.TenantId, x.OrderKey });
            e.Property(x => x.OrderKey).HasMaxLength(20).UseCollation("Latin1_General_100_BIN2");
            e.Property(x => x.LastErrorCode).HasMaxLength(128).IsUnicode(false);
            e.Property(x => x.LeaseOwner).HasMaxLength(64).IsUnicode(false);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasIndex(x => new { x.CompletedAtUtc, x.AvailableAtUtc, x.LeaseExpiresAtUtc });
        });
        model.Entity<ErpInboxReplayAudit>(e =>
        {
            e.ToTable("InboxReplayAudit", Schema); e.HasKey(x => new { x.TenantId, x.OperationId });
            e.Property(x => x.MessageId).HasMaxLength(128).UseCollation("Latin1_General_100_BIN2");
            e.Property(x => x.PayloadSha256).HasMaxLength(64).IsUnicode(false).IsRequired();
            e.Property(x => x.ActorId).HasMaxLength(100).IsRequired();
            e.Property(x => x.ReasonCode).HasMaxLength(128).IsUnicode(false).IsRequired();
            e.Property(x => x.InputRowVersion).HasMaxLength(8).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.MessageId, x.ReplayedAtUtc });
        });
        ErpDeliveryReplayAudit.Configure(model);
    }

    private void GuardAudit()
    {
        ErpDeliveryReplayAudit.Guard(this);
        if (ChangeTracker.Entries<ErpInboxReplayAudit>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("C03_REPLAY_AUDIT_APPEND_ONLY");
    }
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    { GuardAudit(); return base.SaveChanges(acceptAllChangesOnSuccess); }
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
    { GuardAudit(); return base.SaveChangesAsync(acceptAllChangesOnSuccess, ct); }
}

public enum ErpRequestKind { BusinessPartner = 1, Order = 2 }
public enum ErpInboxStatus { Processing = 0, Processed = 1, DeadLettered = 2 }

public sealed class ErpInboxReceipt
{
    public string MessageId { get; set; } = "";
    public Guid TenantId { get; set; }
    public string EventType { get; set; } = "";
    public Guid AggregateId { get; set; }
    public int AggregateVersion { get; set; }
    public byte[] Payload { get; set; } = [];
    public string PayloadSha256 { get; set; } = "";
    public ErpInboxStatus Status { get; set; }
    public int AttemptCount { get; set; }
    public int ConflictCount { get; set; }
    public string? LastConflictSha256 { get; set; }
    public string? ErrorCode { get; set; }
    public DateTimeOffset ReceivedAtUtc { get; set; }
    public DateTimeOffset? ProcessedAtUtc { get; set; }
    public DateTimeOffset? RetryAtUtc { get; set; }
    public DateTimeOffset? ReplayedAtUtc { get; set; }
    public string? ReplayReasonCode { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>Records command identity and published result, never substitutes for ERP master data.</summary>
public sealed class ErpIntegrationRequest
{
    public Guid TenantId { get; set; }
    public ErpRequestKind Kind { get; set; }
    public Guid AggregateId { get; set; }
    public int RequestVersion { get; set; }
    public Guid RequestId { get; set; }
    public Guid AccountId { get; set; }
    public string InputSha256 { get; set; } = "";
    public bool Terminal { get; set; }
    public bool Succeeded { get; set; }
    public int ResultVersion { get; set; }
    public string ResultType { get; set; } = "";
    public string ResultDataJson { get; set; } = "{}";
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class ErpIntegrationAggregate
{
    public Guid TenantId { get; set; }
    public ErpRequestKind Kind { get; set; }
    public Guid AggregateId { get; set; }
    public int LastRequestVersion { get; set; }
    public int LastAggregateVersion { get; set; }
    public int LastResultVersion { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>Post-commit work; no WMS/MES hook executes inside the ERP command transaction.</summary>
public sealed class ErpOrderBridgeDispatch
{
    public Guid TenantId { get; set; }
    public string OrderKey { get; set; } = "";
    public int AttemptCount { get; set; }
    public DateTimeOffset AvailableAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string? LastErrorCode { get; set; }
    public string? LeaseOwner { get; set; }
    public DateTimeOffset? LeaseExpiresAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class ErpInboxReplayAudit
{
    public Guid TenantId { get; set; }
    public Guid OperationId { get; set; }
    public string MessageId { get; set; } = "";
    public string PayloadSha256 { get; set; } = "";
    public byte[] InputRowVersion { get; set; } = [];
    public string ActorId { get; set; } = "";
    public string ReasonCode { get; set; } = "";
    public int PreviousAttemptCount { get; set; }
    public DateTimeOffset ReplayedAtUtc { get; set; }
}
