using CP6.Space.Application;

namespace CP6.Space.UnitTests;

public sealed class SpaceWmsAdapterContractTests
{
    private static readonly Guid TenantId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SiteId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid AttemptId =
        Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid CorrelationId =
        Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly string PlanHash = new('1', 64);

    public static TheoryData<string, SpaceWmsDataSourceKind> AdapterCases =>
        new()
        {
            { "cp6-wms-v1", SpaceWmsDataSourceKind.Real },
            { "space-wms-mock-v1", SpaceWmsDataSourceKind.Simulated },
        };

    [Fact]
    public void Runtime_source_contract_is_query_only_and_shared_by_adapters()
    {
        Assert.True(typeof(ISpaceWmsRuntimeSource).IsAssignableFrom(
            typeof(ISpaceWmsAdapter)));

        var methodNames = typeof(ISpaceWmsRuntimeSource)
            .GetMethods()
            .Select(method => method.Name)
            .ToArray();
        Assert.Contains(nameof(ISpaceWmsRuntimeSource.QueryInventoryAsync),
            methodNames);
        Assert.Contains(nameof(ISpaceWmsRuntimeSource.QueryTasksAsync),
            methodNames);
        Assert.DoesNotContain(nameof(ISpaceWmsAdapter.ApplyBatchAsync),
            methodNames);
        Assert.DoesNotContain(nameof(ISpaceWmsAdapter.PreflightAsync),
            methodNames);

        var query = new SpaceWmsInventoryQuery(
            Context(),
            [LocationId(1)],
            LocateCriteria: new SpaceWmsInventoryLocateCriteria(
                "SKU-01",
                "LOT-01",
                "PALLET-01"));
        var criteria = Assert.IsType<SpaceWmsInventoryLocateCriteria>(
            query.LocateCriteria);
        Assert.Equal("SKU-01", criteria.MaterialNumber);
        Assert.Equal("LOT-01", criteria.LotNumber);
        Assert.Equal("PALLET-01", criteria.ContainerNumber);
    }

    [Theory]
    [MemberData(nameof(AdapterCases))]
    public async Task Cp6_and_mock_adapters_share_the_same_capability_contract(
        string adapterId,
        SpaceWmsDataSourceKind sourceKind)
    {
        ISpaceWmsAdapter adapter =
            new ContractAdapter(adapterId, sourceKind);

        var capabilities = await adapter.GetCapabilitiesAsync(Context());
        var health = await adapter.CheckHealthAsync(Context());
        var locations = await adapter.QueryLocationsAsync(
            new SpaceWmsLocationQuery(Context(), [LocationId(1)]));
        var inventory = await adapter.QueryInventoryAsync(
            new SpaceWmsInventoryQuery(Context(), [LocationId(1)]));
        var tasks = await adapter.QueryTasksAsync(
            new SpaceWmsTaskQuery(Context(), [LocationId(1)]));

        Assert.Equal(adapterId, capabilities.AdapterId);
        Assert.Equal(sourceKind, capabilities.DataSourceKind);
        Assert.Equal(64, capabilities.CapabilityHash.Length);
        Assert.True(capabilities.Capabilities.QueryInventory);
        Assert.True(capabilities.Capabilities.QueryTasks);
        Assert.True(SpaceWmsContract.CanPublish(capabilities, health));
        Assert.Equal(sourceKind, locations.Source.Kind);
        Assert.Equal(sourceKind, inventory.Source.Kind);
        Assert.Equal(sourceKind, tasks.Source.Kind);
    }

    [Fact]
    public async Task Same_operation_key_and_payload_replays_original_result()
    {
        var adapter =
            new ContractAdapter("cp6-wms-v1", SpaceWmsDataSourceKind.Real);
        var batch = Batch();

        var first = await adapter.ApplyBatchAsync(batch);
        var replay = await adapter.ApplyBatchAsync(batch);

        Assert.Same(first, replay);
        Assert.Equal(1, adapter.AppliedBatchCount);
        Assert.Equal(
            SpaceWmsBatchAssessmentKind.Succeeded,
            SpaceWmsContract.AssessBatchResult(batch, replay).Kind);
    }

    [Fact]
    public async Task Same_operation_key_with_different_payload_is_rejected()
    {
        var adapter =
            new ContractAdapter("cp6-wms-v1", SpaceWmsDataSourceKind.Real);
        var first = Batch();
        var differentPayload = SpaceWmsBatch.Create(
            Context(),
            AttemptId,
            1,
            PlanHash,
            [Mutation(1, "A-02", new Dictionary<string, string?>
            {
                ["capacity"] = "2",
            })]);

        await adapter.ApplyBatchAsync(first);
        var conflict = await adapter.ApplyBatchAsync(differentPayload);

        Assert.Equal(1, adapter.AppliedBatchCount);
        Assert.All(
            conflict.Items,
            item => Assert.Equal(
                "WMS_IDEMPOTENCY_CONFLICT",
                item.ErrorCode));
        Assert.Equal(
            SpaceWmsBatchAssessmentKind.FailedNoEffect,
            SpaceWmsContract
                .AssessBatchResult(differentPayload, conflict)
                .Kind);
    }

    [Fact]
    public void Partial_receipts_require_reconciliation()
    {
        var batch = Batch(twoItems: true);
        var response = Result(
            batch,
            SuccessReceipt(batch.Items[0]),
            FailureReceipt(batch.Items[1]));

        var assessment =
            SpaceWmsContract.AssessBatchResult(batch, response);

        Assert.Equal(
            SpaceWmsBatchAssessmentKind.Partial,
            assessment.Kind);
        Assert.True(assessment.RequiresReconciliation);
        Assert.Empty(assessment.ContractViolations);
    }

    [Fact]
    public void Missing_receipt_is_an_uncertain_result()
    {
        var batch = Batch(twoItems: true);
        var response = Result(batch, SuccessReceipt(batch.Items[0]));

        var assessment =
            SpaceWmsContract.AssessBatchResult(batch, response);

        Assert.Equal(
            SpaceWmsBatchAssessmentKind.Uncertain,
            assessment.Kind);
        Assert.Contains(
            "WMS_MISSING_ITEM_RECEIPT",
            assessment.ContractViolations);
    }

    [Fact]
    public void Empty_http_success_cannot_be_treated_as_batch_success()
    {
        var batch = Batch();
        var response = Result(batch);

        var assessment =
            SpaceWmsContract.AssessBatchResult(batch, response);

        Assert.Equal(
            SpaceWmsBatchAssessmentKind.Uncertain,
            assessment.Kind);
        Assert.True(assessment.RequiresReconciliation);
    }

    [Fact]
    public void Mismatched_payload_hash_is_an_uncertain_result()
    {
        var batch = Batch();
        var response = new SpaceWmsBatchResult(
            batch.OperationKey,
            new string('f', 64),
            "op-1",
            [SuccessReceipt(batch.Items[0])],
            DateTimeOffset.UtcNow);

        var assessment =
            SpaceWmsContract.AssessBatchResult(batch, response);

        Assert.Equal(
            SpaceWmsBatchAssessmentKind.Uncertain,
            assessment.Kind);
        Assert.Contains(
            "WMS_PAYLOAD_HASH_MISMATCH",
            assessment.ContractViolations);
    }

    [Fact]
    public void Duplicate_receipts_are_an_uncertain_result()
    {
        var batch = Batch();
        var receipt = SuccessReceipt(batch.Items[0]);
        var response = Result(batch, receipt, receipt);

        var assessment =
            SpaceWmsContract.AssessBatchResult(batch, response);

        Assert.Equal(
            SpaceWmsBatchAssessmentKind.Uncertain,
            assessment.Kind);
        Assert.Contains(
            "WMS_DUPLICATE_ITEM_RECEIPT",
            assessment.ContractViolations);
    }

    [Fact]
    public void Success_without_hash_or_external_version_is_uncertain()
    {
        var batch = Batch();
        var item = batch.Items[0];
        var response = Result(
            batch,
            new SpaceWmsItemReceipt(
                item.LogicalId,
                item.LocationCode,
                item.Action,
                SpaceWmsItemOutcome.Applied,
                item.LogicalId.ToString("D"),
                null,
                null,
                null));

        var assessment =
            SpaceWmsContract.AssessBatchResult(batch, response);

        Assert.Equal(
            SpaceWmsBatchAssessmentKind.Uncertain,
            assessment.Kind);
        Assert.Contains(
            "WMS_SUCCESS_EVIDENCE_MISSING",
            assessment.ContractViolations);
    }

    [Fact]
    public void All_failed_items_are_classified_as_no_effect()
    {
        var batch = Batch(twoItems: true);
        var response = Result(
            batch,
            FailureReceipt(batch.Items[0]),
            FailureReceipt(batch.Items[1]));

        var assessment =
            SpaceWmsContract.AssessBatchResult(batch, response);

        Assert.Equal(
            SpaceWmsBatchAssessmentKind.FailedNoEffect,
            assessment.Kind);
        Assert.False(assessment.RequiresReconciliation);
    }

    [Fact]
    public void Operation_key_is_stable_and_scoped_to_batch_identity()
    {
        var key = SpaceWmsContract.CreateOperationKey(
            TenantId,
            SiteId,
            AttemptId,
            7);

        Assert.Equal(
            $"space:{TenantId:D}:{SiteId:D}:{AttemptId:D}:7",
            key);
    }

    [Fact]
    public void Operation_key_scope_rejects_another_site()
    {
        var otherSiteId =
            Guid.Parse("99999999-9999-9999-9999-999999999999");
        var foreignKey = SpaceWmsContract.CreateOperationKey(
            TenantId,
            otherSiteId,
            AttemptId,
            1);

        var error = Assert.Throws<InvalidOperationException>(
            () => SpaceWmsContract.ValidateOperationKeyScope(
                Context(),
                foreignKey));

        Assert.Equal("SPACE_WMS_OPERATION_SCOPE_DENIED", error.Message);
    }

    [Fact]
    public void Mutation_hash_is_independent_of_attribute_insertion_order()
    {
        var first = Mutation(
            1,
            "A-01",
            new Dictionary<string, string?>
            {
                ["capacity"] = "100",
                ["temperature"] = "ambient",
            });
        var second = Mutation(
            1,
            "A-01",
            new Dictionary<string, string?>
            {
                ["temperature"] = "ambient",
                ["capacity"] = "100",
            });

        Assert.Equal(first.PayloadHash, second.PayloadHash);
        Assert.Equal(
            ["capacity", "temperature"],
            first.Attributes.Keys);
    }

    [Fact]
    public void Batch_hash_is_stable_after_sequence_sorting()
    {
        var first = SpaceWmsBatch.Create(
            Context(),
            AttemptId,
            1,
            PlanHash,
            [Mutation(2, "A-02"), Mutation(1, "A-01")]);
        var second = SpaceWmsBatch.Create(
            Context(),
            AttemptId,
            1,
            PlanHash,
            [Mutation(1, "A-01"), Mutation(2, "A-02")]);

        Assert.Equal(first.PayloadHash, second.PayloadHash);
        Assert.Equal([1, 2], first.Items.Select(item => item.SequenceNo));
    }

    [Fact]
    public void Capability_hash_is_stable_and_changes_with_capabilities()
    {
        var observed = DateTimeOffset.Parse("2026-07-27T10:00:00Z");
        var first = Snapshot(
            "cp6-wms-v1",
            SpaceWmsDataSourceKind.Real,
            Capabilities(batchMaxSize: 500),
            observed);
        var laterObservation = Snapshot(
            "cp6-wms-v1",
            SpaceWmsDataSourceKind.Real,
            Capabilities(batchMaxSize: 500),
            observed.AddMinutes(5));
        var changed = Snapshot(
            "cp6-wms-v1",
            SpaceWmsDataSourceKind.Real,
            Capabilities(batchMaxSize: 100),
            observed);

        Assert.Equal(first.CapabilityHash, laterObservation.CapabilityHash);
        Assert.NotEqual(first.CapabilityHash, changed.CapabilityHash);
    }

    [Fact]
    public void Certified_idempotent_fails_closed_without_status_query()
    {
        var incomplete = Capabilities(
            batchMaxSize: 500,
            reliableOperationStatus: false);

        var error = Assert.Throws<ArgumentException>(() =>
            Snapshot(
                "broken",
                SpaceWmsDataSourceKind.Real,
                incomplete,
                DateTimeOffset.UtcNow));

        Assert.Contains(
            "reliable status",
            error.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Capability_compatibility_blocks_batch_limit_and_bad_codes()
    {
        var batch = SpaceWmsBatch.Create(
            Context(),
            AttemptId,
            1,
            PlanHash,
            [Mutation(1, "bad_code"), Mutation(2, "A-02")]);
        var snapshot = Snapshot(
            "cp6-wms-v1",
            SpaceWmsDataSourceKind.Real,
            Capabilities(batchMaxSize: 1),
            DateTimeOffset.UtcNow);

        var issues =
            SpaceWmsContract.CheckCompatibility(batch, snapshot);

        Assert.Contains(
            issues,
            issue => issue.Code == "SPACE_WMS_BATCH_LIMIT_EXCEEDED");
        Assert.Contains(
            issues,
            issue =>
                issue.LogicalId == LocationId(1) &&
                issue.Code == "SPACE_WMS_LOCATION_CODE_UNSUPPORTED");
        Assert.All(issues, issue => Assert.True(issue.Blocking));
    }

    [Fact]
    public void Preview_only_and_unhealthy_adapters_cannot_publish()
    {
        var preview = SpaceWmsCapabilitySnapshot.Create(
            "preview",
            SpaceWmsDataSourceKind.Real,
            SpaceWmsCertificationLevel.PreviewOnly,
            Capabilities(batchMaxSize: 500),
            DateTimeOffset.UtcNow);
        var certified = Snapshot(
            "cp6-wms-v1",
            SpaceWmsDataSourceKind.Real,
            Capabilities(batchMaxSize: 500),
            DateTimeOffset.UtcNow);
        var healthy = Health("preview", SpaceWmsHealthState.Healthy);
        var unavailable =
            Health("cp6-wms-v1", SpaceWmsHealthState.Unavailable);

        Assert.False(SpaceWmsContract.CanPublish(preview, healthy));
        Assert.False(SpaceWmsContract.CanPublish(certified, unavailable));
    }

    [Fact]
    public void Unavailable_source_is_explicit_and_never_simulated()
    {
        var source = new SpaceWmsSourceMetadata(
            SpaceWmsDataSourceKind.Unavailable,
            "WMS_UNCONFIGURED",
            DateTimeOffset.UtcNow);

        Assert.False(source.IsAvailable);
        Assert.False(source.IsSimulated);
    }

    private static SpaceWmsContext Context() =>
        new(TenantId, SiteId, "WH-01", CorrelationId);

    private static SpaceWmsBatch Batch(bool twoItems = false)
    {
        var items = new List<SpaceWmsLocationMutation>
        {
            Mutation(1, "A-01"),
        };
        if (twoItems)
            items.Add(Mutation(2, "A-02"));
        return SpaceWmsBatch.Create(
            Context(),
            AttemptId,
            1,
            PlanHash,
            items);
    }

    private static SpaceWmsLocationMutation Mutation(
        int sequenceNo,
        string code,
        IReadOnlyDictionary<string, string?>? attributes = null) =>
        SpaceWmsLocationMutation.Create(
            sequenceNo,
            LocationId(sequenceNo),
            code,
            SpaceWmsLocationAction.Create,
            new SpaceWmsLocationPath(
                "SITE-01",
                1,
                "ZONE-01",
                "AISLE-01",
                "RACK-01",
                sequenceNo,
                1,
                1),
            attributes);

    private static Guid LocationId(int value) =>
        Guid.Parse($"aaaaaaaa-aaaa-aaaa-aaaa-{value:D12}");

    private static SpaceWmsBatchResult Result(
        SpaceWmsBatch batch,
        params SpaceWmsItemReceipt[] receipts) =>
        new(
            batch.OperationKey,
            batch.PayloadHash,
            "external-operation-1",
            receipts,
            DateTimeOffset.UtcNow);

    private static SpaceWmsItemReceipt SuccessReceipt(
        SpaceWmsLocationMutation item) =>
        new(
            item.LogicalId,
            item.LocationCode,
            item.Action,
            SpaceWmsItemOutcome.Applied,
            item.LogicalId.ToString("D"),
            "1",
            new string('a', 64),
            null);

    private static SpaceWmsItemReceipt FailureReceipt(
        SpaceWmsLocationMutation item) =>
        new(
            item.LogicalId,
            item.LocationCode,
            item.Action,
            SpaceWmsItemOutcome.NotApplied,
            null,
            null,
            null,
            "WMS_REJECTED");

    private static SpaceWmsCapabilities Capabilities(
        int batchMaxSize,
        bool reliableOperationStatus = true) =>
        new(
            AtomicStaging: false,
            IdempotentUpsert: true,
            IdempotentDisable: true,
            RenameLocation: false,
            QueryByLogicalId: true,
            QueryBlockingReferences: true,
            QueryInventory: true,
            QueryTasks: true,
            ReliableOperationStatus: reliableOperationStatus,
            ReadBackHash: true,
            BatchMaxSize: batchMaxSize,
            AllowedCodePattern: "^[A-Z0-9-]+$",
            CodeMaxLength: 50);

    private static SpaceWmsCapabilitySnapshot Snapshot(
        string adapterId,
        SpaceWmsDataSourceKind sourceKind,
        SpaceWmsCapabilities capabilities,
        DateTimeOffset observedAtUtc) =>
        SpaceWmsCapabilitySnapshot.Create(
            adapterId,
            sourceKind,
            SpaceWmsCertificationLevel.CertifiedIdempotent,
            capabilities,
            observedAtUtc);

    private static SpaceWmsHealth Health(
        string adapterId,
        SpaceWmsHealthState state) =>
        new(
            adapterId,
            state,
            DateTimeOffset.UtcNow,
            TimeSpan.FromMilliseconds(1));

    private sealed class ContractAdapter(
        string adapterId,
        SpaceWmsDataSourceKind sourceKind) : ISpaceWmsAdapter
    {
        private readonly Dictionary<string, SpaceWmsBatchResult> _results =
            new(StringComparer.Ordinal);

        public int AppliedBatchCount { get; private set; }
        public string RuntimeAdapterId => adapterId;
        public string RuntimeDataSourceId => adapterId;
        public SpaceWmsDataSourceKind RuntimeDataSourceKind => sourceKind;

        public Task<SpaceWmsCapabilitySnapshot> GetCapabilitiesAsync(
            SpaceWmsContext context,
            CancellationToken ct = default) =>
            Task.FromResult(Snapshot(
                adapterId,
                sourceKind,
                Capabilities(batchMaxSize: 500),
                DateTimeOffset.UtcNow));

        public Task<SpaceWmsHealth> CheckHealthAsync(
            SpaceWmsContext context,
            CancellationToken ct = default) =>
            Task.FromResult(Health(
                adapterId,
                SpaceWmsHealthState.Healthy));

        public Task<SpaceWmsPreflightResult> PreflightAsync(
            SpaceWmsPreflightRequest request,
            CancellationToken ct = default) =>
            Task.FromResult(new SpaceWmsPreflightResult(
                request.CapabilityHash,
                [],
                DateTimeOffset.UtcNow));

        public Task<SpaceWmsBatchResult> ApplyBatchAsync(
            SpaceWmsBatch batch,
            CancellationToken ct = default)
        {
            if (_results.TryGetValue(
                    batch.OperationKey,
                    out var existing))
            {
                if (string.Equals(
                        existing.PayloadHash,
                        batch.PayloadHash,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(existing);
                }
                return Task.FromResult(new SpaceWmsBatchResult(
                    batch.OperationKey,
                    batch.PayloadHash,
                    null,
                    batch.Items.Select(item =>
                        new SpaceWmsItemReceipt(
                            item.LogicalId,
                            item.LocationCode,
                            item.Action,
                            SpaceWmsItemOutcome.NotApplied,
                            null,
                            null,
                            null,
                            "WMS_IDEMPOTENCY_CONFLICT")).ToArray(),
                    DateTimeOffset.UtcNow));
            }

            AppliedBatchCount++;
            var result = new SpaceWmsBatchResult(
                batch.OperationKey,
                batch.PayloadHash,
                $"operation-{AppliedBatchCount}",
                batch.Items.Select(SuccessReceipt).ToArray(),
                DateTimeOffset.UtcNow);
            _results.Add(batch.OperationKey, result);
            return Task.FromResult(result);
        }

        public Task<SpaceWmsOperationStatus> GetOperationStatusAsync(
            SpaceWmsOperationQuery request,
            CancellationToken ct = default)
        {
            var found = _results.TryGetValue(
                request.OperationKey,
                out var result);
            return Task.FromResult(new SpaceWmsOperationStatus(
                request.OperationKey,
                request.PayloadHash,
                found
                    ? SpaceWmsOperationState.Applied
                    : SpaceWmsOperationState.FailedNoEffect,
                true,
                DateTimeOffset.UtcNow,
                result?.ExternalOperationId));
        }

        public Task<SpaceWmsReadBackResult> ReadBackAsync(
            SpaceWmsReadBackRequest request,
            CancellationToken ct = default) =>
            Task.FromResult(new SpaceWmsReadBackResult(
                Source(),
                request.LogicalIds.Select(Location).ToArray(),
                new string('b', 64)));

        public Task<SpaceWmsBlockingReferences>
            GetBlockingReferencesAsync(
                SpaceWmsBlockingReferencesRequest request,
                CancellationToken ct = default) =>
            Task.FromResult(new SpaceWmsBlockingReferences(Source(), []));

        public Task<SpaceWmsLocationResult> QueryLocationsAsync(
            SpaceWmsLocationQuery request,
            CancellationToken ct = default) =>
            Task.FromResult(new SpaceWmsLocationResult(
                Source(),
                request.LogicalIds.Select(Location).ToArray()));

        public Task<SpaceWmsInventoryResult> QueryInventoryAsync(
            SpaceWmsInventoryQuery request,
            CancellationToken ct = default) =>
            Task.FromResult(new SpaceWmsInventoryResult(Source(), []));

        public Task<SpaceWmsTaskResult> QueryTasksAsync(
            SpaceWmsTaskQuery request,
            CancellationToken ct = default) =>
            Task.FromResult(new SpaceWmsTaskResult(Source(), []));

        private SpaceWmsSourceMetadata Source() =>
            new(sourceKind, adapterId, DateTimeOffset.UtcNow);

        private static SpaceWmsLocationState Location(Guid logicalId) =>
            new(
                logicalId,
                "A-01",
                logicalId.ToString("D"),
                true,
                "1",
                new string('a', 64));
    }
}
