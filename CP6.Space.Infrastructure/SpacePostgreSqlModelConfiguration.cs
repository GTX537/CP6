using CP6.Core.Persistence;
using CP6.Space.Domain;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.Infrastructure;

internal static class SpacePostgreSqlModelConfiguration
{
    public static void Apply(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var utcDateTimes = new HashSet<(Type EntityType, string Property)>();

        // SaveChanges stamps every mapped tenant entity from the validated UTC clock.
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(SpaceTenantEntity).IsAssignableFrom(entity.ClrType))
            {
                utcDateTimes.Add((entity.ClrType, nameof(SpaceTenantEntity.CreatedAtUtc)));
                utcDateTimes.Add((entity.ClrType, nameof(SpaceTenantEntity.ModifiedAtUtc)));
            }
        }

        // Domain writers validate UTC or copy an already validated event timestamp.
        // Property-to-writer evidence is maintained in WP2-SPACE-MAPPING.md.
        Add<SpaceAiBudgetReservation>(utcDateTimes,
            nameof(SpaceAiBudgetReservation.ExpiresAtUtc));
        Add<SpaceAiTenantPolicyConfiguration>(utcDateTimes,
            nameof(SpaceAiTenantPolicyConfiguration.UpdatedAtUtc));
        Add<SpaceAiUsageRecord>(utcDateTimes,
            nameof(SpaceAiUsageRecord.RecordedAtUtc),
            nameof(SpaceAiUsageRecord.ArchivedAtUtc));
        Add<SpaceAsset>(utcDateTimes, nameof(SpaceAsset.CreatedAtUtc));
        Add<SpaceAssetVersion>(utcDateTimes, nameof(SpaceAssetVersion.CreatedAtUtc));
        Add<SpaceCadParsePreparation>(utcDateTimes,
            nameof(SpaceCadParsePreparation.ExpiresAtUtc));
        Add<SpaceCadSiteProviderCertification>(utcDateTimes,
            nameof(SpaceCadSiteProviderCertification.ExpiresAtUtc),
            nameof(SpaceCadSiteProviderCertification.ValidFromUtc));
        Add<SpaceCadSiteProviderConfiguration>(utcDateTimes,
            nameof(SpaceCadSiteProviderConfiguration.ApprovedAtUtc));
        Add<SpaceDeviceAlarmState>(utcDateTimes,
            nameof(SpaceDeviceAlarmState.OccurredAtUtc),
            nameof(SpaceDeviceAlarmState.ReceivedAtUtc));
        Add<SpaceDeviceCurrentState>(utcDateTimes,
            nameof(SpaceDeviceCurrentState.OperatingStateOccurredAtUtc),
            nameof(SpaceDeviceCurrentState.OperatingStateReceivedAtUtc),
            nameof(SpaceDeviceCurrentState.PositionOccurredAtUtc),
            nameof(SpaceDeviceCurrentState.PositionReceivedAtUtc));
        Add<SpaceDeviceEvent>(utcDateTimes,
            nameof(SpaceDeviceEvent.OccurredAtUtc),
            nameof(SpaceDeviceEvent.ReceivedAtUtc));
        Add<SpaceDispatchRecommendation>(utcDateTimes,
            nameof(SpaceDispatchRecommendation.GeneratedAtUtc));
        Add<SpaceEditLease>(utcDateTimes,
            nameof(SpaceEditLease.AcquiredAtUtc),
            nameof(SpaceEditLease.ExpiresAtUtc),
            nameof(SpaceEditLease.LastRenewedAtUtc));
        Add<SpaceEditLeaseTakeoverAudit>(utcDateTimes,
            nameof(SpaceEditLeaseTakeoverAudit.TakenOverAtUtc));
        Add<SpaceElementCommandBatch>(utcDateTimes,
            nameof(SpaceElementCommandBatch.AppliedAtUtc));
        Add<SpaceElementRevision>(utcDateTimes,
            nameof(SpaceElementRevision.ManualCorrectionUpdatedAtUtc));
        Add<SpaceExternalGrant>(utcDateTimes,
            nameof(SpaceExternalGrant.ValidFromUtc),
            nameof(SpaceExternalGrant.ValidToUtc));
        Add<SpaceExternalMembership>(utcDateTimes,
            nameof(SpaceExternalMembership.ValidFromUtc),
            nameof(SpaceExternalMembership.AcceptedAtUtc),
            nameof(SpaceExternalMembership.ValidToUtc));
        Add<SpaceFile>(utcDateTimes,
            nameof(SpaceFile.ContentDeletedAtUtc),
            nameof(SpaceFile.DeletionRequestedAtUtc),
            nameof(SpaceFile.RetainUntilUtc));
        Add<SpaceGenerationProposal>(utcDateTimes,
            nameof(SpaceGenerationProposal.PayloadPurgedAtUtc));
        Add<SpaceGenerationRun>(utcDateTimes,
            nameof(SpaceGenerationRun.ApplyPreparedAtUtc),
            nameof(SpaceGenerationRun.CancelRequestedAtUtc),
            nameof(SpaceGenerationRun.CancelledAtUtc),
            nameof(SpaceGenerationRun.PayloadPurgedAtUtc),
            nameof(SpaceGenerationRun.RetentionHoldUntilUtc),
            nameof(SpaceGenerationRun.ReviewCompletedAtUtc));
        Add<SpaceHistoricalRepublish>(utcDateTimes,
            nameof(SpaceHistoricalRepublish.RequestedAtUtc));
        Add<SpaceIdempotencyRecord>(utcDateTimes,
            nameof(SpaceIdempotencyRecord.ReplayUntilUtc),
            nameof(SpaceIdempotencyRecord.RetainUntilUtc));
        Add<SpaceJob>(utcDateTimes,
            nameof(SpaceJob.NextAttemptAtUtc),
            nameof(SpaceJob.RequestedAtUtc),
            nameof(SpaceJob.CancellationRequestedAtUtc),
            nameof(SpaceJob.FinishedAtUtc),
            nameof(SpaceJob.LockExpiresAtUtc),
            nameof(SpaceJob.LockedAtUtc),
            nameof(SpaceJob.StartedAtUtc));
        Add<SpaceJobAttempt>(utcDateTimes,
            nameof(SpaceJobAttempt.StartedAtUtc),
            nameof(SpaceJobAttempt.FinishedAtUtc));
        Add<SpaceJobStep>(utcDateTimes,
            nameof(SpaceJobStep.StartedAtUtc),
            nameof(SpaceJobStep.FinishedAtUtc));
        Add<SpaceModelIssue>(utcDateTimes,
            nameof(SpaceModelIssue.AcknowledgedAtUtc),
            nameof(SpaceModelIssue.PayloadPurgedAtUtc));
        Add<SpaceModelVersion>(utcDateTimes,
            nameof(SpaceModelVersion.PublishedAtUtc));
        Add<SpacePersonnelCurrentState>(utcDateTimes,
            nameof(SpacePersonnelCurrentState.PositionOccurredAtUtc),
            nameof(SpacePersonnelCurrentState.PositionReceivedAtUtc),
            nameof(SpacePersonnelCurrentState.WorkStateOccurredAtUtc),
            nameof(SpacePersonnelCurrentState.WorkStateReceivedAtUtc));
        Add<SpacePersonnelEvent>(utcDateTimes,
            nameof(SpacePersonnelEvent.OccurredAtUtc),
            nameof(SpacePersonnelEvent.ReceivedAtUtc));
        Add<SpacePublishAttempt>(utcDateTimes,
            nameof(SpacePublishAttempt.QueuedAtUtc),
            nameof(SpacePublishAttempt.StartedAtUtc),
            nameof(SpacePublishAttempt.FinishedAtUtc),
            nameof(SpacePublishAttempt.LastRetriedAtUtc),
            nameof(SpacePublishAttempt.RuntimeActivatedAtUtc),
            nameof(SpacePublishAttempt.WmsCommittedAtUtc));
        Add<SpacePublishAuditEvent>(utcDateTimes,
            nameof(SpacePublishAuditEvent.OccurredAtUtc));
        Add<SpacePublishBatch>(utcDateTimes,
            nameof(SpacePublishBatch.ObservedAtUtc));
        Add<SpacePutawayRecommendation>(utcDateTimes,
            nameof(SpacePutawayRecommendation.GeneratedAtUtc));
        Add<SpaceRackGenerationProfile>(utcDateTimes,
            nameof(SpaceRackGenerationProfile.CreatedAtUtc));
        Add<SpaceRackGenerationProfileVersion>(utcDateTimes,
            nameof(SpaceRackGenerationProfileVersion.CreatedAtUtc));
        Add<SpaceTenantAiWorkSlot>(utcDateTimes,
            nameof(SpaceTenantAiWorkSlot.LeaseExpiresAtUtc));
        Add<SpaceValidationRun>(utcDateTimes,
            nameof(SpaceValidationRun.RequestedAtUtc),
            nameof(SpaceValidationRun.FinishedAtUtc),
            nameof(SpaceValidationRun.StartedAtUtc));
        Add<SpaceWmsAdoption>(utcDateTimes,
            nameof(SpaceWmsAdoption.LastObservedAtUtc),
            nameof(SpaceWmsAdoption.BoundAtUtc));
        Add<SpaceWmsReceipt>(utcDateTimes,
            nameof(SpaceWmsReceipt.ReceivedAtUtc));

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                if ((property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?)) &&
                    !utcDateTimes.Contains((entity.ClrType, property.Name)))
                {
                    throw new InvalidOperationException(
                        $"Space PostgreSQL DateTime requires an explicit writer classification: {entity.ClrType.Name}.{property.Name}.");
                }
            }
        }

        PostgreSqlModelConfiguration.Apply(modelBuilder, utcDateTimes, PostgreSqlFixedTextDomainsV1.Space);
    }

    private static void Add<T>(
        HashSet<(Type EntityType, string Property)> properties,
        params string[] names)
    {
        foreach (var name in names)
        {
            properties.Add((typeof(T), name));
        }
    }
}
