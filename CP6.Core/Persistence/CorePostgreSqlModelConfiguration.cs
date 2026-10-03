using CP6.Entity.DomainModels.Integration;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Space;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wf;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Persistence;

/// <summary>Only properties with an audited UTC writer are instants; other Core DateTimes retain their wall-clock values.</summary>
internal static class CorePostgreSqlModelConfiguration
{
    // Writer evidence and the deliberately preserved local/mixed columns are recorded in WP2-CORE-MAPPING.md.
    private static readonly IReadOnlySet<(Type EntityType, string Property)> UtcDateTimes =
        new HashSet<(Type, string)>
        {
            (typeof(IntegrationEvent), nameof(IntegrationEvent.NextRetryAt)),
            (typeof(IntegrationEvent), nameof(IntegrationEvent.OccurredAtUtc)),
            (typeof(IntegrationEvent), nameof(IntegrationEvent.DeadLetterNotifiedAtUtc)),
            (typeof(IntegrationEvent), nameof(IntegrationEvent.DeadLetterNotificationLeaseUntilUtc)),
            (typeof(Space_AuditEvent), nameof(Space_AuditEvent.OccurredAtUtc)),
            (typeof(Sys_BrowserSession), nameof(Sys_BrowserSession.LoggedOutAtUtc)),
            (typeof(Wf_FlowDefVersion), nameof(Wf_FlowDefVersion.PublishedAtUtc)),
            (typeof(Wf_FormDefVersion), nameof(Wf_FormDefVersion.PublishedAtUtc)),
            (typeof(Wf_FormData), nameof(Wf_FormData.SubmittedAtUtc)),
            (typeof(Wf_FormDraft), nameof(Wf_FormDraft.SubmittedAtUtc)),
            (typeof(Wf_FlowTrigger), nameof(Wf_FlowTrigger.LastFiredUtc)),
            (typeof(Wf_FlowTrigger), nameof(Wf_FlowTrigger.NextDueUtc)),
            (typeof(Wf_TriggerFire), nameof(Wf_TriggerFire.FiredUtc)),
            (typeof(Wf_Notification), nameof(Wf_Notification.NextAttemptAtUtc)),
            (typeof(Wf_Notification), nameof(Wf_Notification.DispatchedAtUtc)),
            (typeof(Wf_Notification), nameof(Wf_Notification.ReadAt)),
            (typeof(Wf_ServiceJob), nameof(Wf_ServiceJob.DueAtUtc)),
            (typeof(Wf_ServiceJob), nameof(Wf_ServiceJob.NextAttemptAtUtc)),
            (typeof(Wf_ServiceJob), nameof(Wf_ServiceJob.LockedAtUtc)),
            (typeof(Wf_ServiceJob), nameof(Wf_ServiceJob.LockExpiresAtUtc)),
            (typeof(Wf_ServiceJob), nameof(Wf_ServiceJob.CompletedAtUtc)),
            (typeof(ClientDevice), nameof(ClientDevice.ActivatedAt)),
            (typeof(ClientDevice), nameof(ClientDevice.LastSeenAt)),
            (typeof(ClientDevice), nameof(ClientDevice.FullAuthExpiresAt)),
            (typeof(ClientDevice), nameof(ClientDevice.DisabledAt)),
            (typeof(DeviceActivation), nameof(DeviceActivation.ExpiresAt)),
            (typeof(DeviceActivation), nameof(DeviceActivation.ConsumedAt)),
            (typeof(LabelJob), nameof(LabelJob.RequestedAt)),
            (typeof(LabelJob), nameof(LabelJob.CompletedAt)),
            (typeof(MobileTaskEvent), nameof(MobileTaskEvent.OccurredAt)),
            (typeof(MobileTaskScanLog), nameof(MobileTaskScanLog.ScannedAt)),
            (typeof(MobileTaskScanLog), nameof(MobileTaskScanLog.RetainUntil)),
            (typeof(TaskCommandReceipt), nameof(TaskCommandReceipt.CompletedAt)),
            (typeof(StockSerialTransaction), nameof(StockSerialTransaction.OccurredAt)),
            (typeof(LpnEvent), nameof(LpnEvent.OccurredAt)),
            (typeof(ProductMaster), nameof(ProductMaster.SerialTrackingLockedAt)),
            (typeof(MaterialShortage), nameof(MaterialShortage.ResolvedAt)),
            (typeof(SpaceDispatchApprovalRequest), nameof(SpaceDispatchApprovalRequest.RequestedAtUtc)),
            (typeof(SpaceDispatchApprovalRequest), nameof(SpaceDispatchApprovalRequest.DecidedAtUtc)),
            (typeof(SpaceDispatchApprovalRequest), nameof(SpaceDispatchApprovalRequest.AppliedAtUtc)),
            (typeof(SpaceDispatchApprovalRequest), nameof(SpaceDispatchApprovalRequest.CompensatedAtUtc)),
            (typeof(SpaceDispatchExecutionAction), nameof(SpaceDispatchExecutionAction.RequestedAtUtc)),
            (typeof(SpaceWmsOperation), nameof(SpaceWmsOperation.ObservedAtUtc)),
            (typeof(WmsFeatureFlagChange), nameof(WmsFeatureFlagChange.RequestedAtUtc)),
            (typeof(WmsFeatureFlagChange), nameof(WmsFeatureFlagChange.DecidedAtUtc)),
            (typeof(WmsFeatureFlagChange), nameof(WmsFeatureFlagChange.AppliedAtUtc)),
        };

    public static void Apply(ModelBuilder modelBuilder) => PostgreSqlModelConfiguration.Apply(modelBuilder, UtcDateTimes, PostgreSqlFixedTextDomainsV1.Core);
}
