using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.ErpIntegration;

/// <summary>One immutable operator decision; original delivery evidence stays on its own durable record.</summary>
public sealed class ErpDeliveryReplayAudit
{
    public Guid TenantId { get; set; }
    public Guid OperationId { get; set; }
    public string Kind { get; set; } = "";
    public string TargetId { get; set; } = "";
    public string? MessageId { get; set; }
    public string PayloadSha256 { get; set; } = "";
    public byte[] InputRowVersion { get; set; } = [];
    public string ActorId { get; set; } = "";
    public string ReasonCode { get; set; } = "";
    public int PreviousAttemptCount { get; set; }
    public DateTimeOffset ReplayedAtUtc { get; set; }

    public static void Configure(ModelBuilder model)
    {
        model.Entity<ErpDeliveryReplayAudit>(e =>
        {
            e.ToTable("DeliveryReplayAudit", ErpIntegrationContext.Schema);
            e.HasKey(x => new { x.TenantId, x.OperationId });
            e.Property(x => x.Kind).HasMaxLength(16).IsUnicode(false).IsRequired();
            e.Property(x => x.TargetId).HasMaxLength(128).UseCollation("Latin1_General_100_BIN2").IsRequired();
            e.Property(x => x.MessageId).HasMaxLength(128).UseCollation("Latin1_General_100_BIN2");
            e.Property(x => x.PayloadSha256).HasMaxLength(64).IsUnicode(false).IsRequired();
            e.Property(x => x.InputRowVersion).HasMaxLength(8).IsRequired();
            e.Property(x => x.ActorId).HasMaxLength(100).IsRequired();
            e.Property(x => x.ReasonCode).HasMaxLength(128).IsUnicode(false).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.Kind, x.TargetId, x.ReplayedAtUtc });
        });
    }

    public static void Guard(DbContext context)
    {
        if (context.ChangeTracker.Entries<ErpDeliveryReplayAudit>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("C03_DELIVERY_REPLAY_AUDIT_APPEND_ONLY");
    }
}
