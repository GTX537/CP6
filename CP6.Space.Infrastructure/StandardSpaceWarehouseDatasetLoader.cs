using System.Globalization;
using CP6.Space.Application;

namespace CP6.Space.Infrastructure;

public sealed class StandardSpaceWarehouseDatasetLoader(
    StandardSpaceWmsSimulator simulator) :
    ISpaceStandardWarehouseDatasetLoader
{
    public async Task<SpaceStandardWarehouseLoadResult> LoadAsync(
        SpaceWmsContext context,
        SpaceStandardWarehouseDataset dataset,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        SpaceWmsContract.ValidateContext(context);
        if (!StringComparer.Ordinal.Equals(
                context.WarehouseCode.Trim(),
                dataset.WarehouseCode))
        {
            throw new InvalidOperationException(
                "SPACE_STANDARD_DATASET_WAREHOUSE_MISMATCH");
        }

        simulator.Reset(context);
        simulator.ConfigureFault(
            context,
            SpaceWmsSimulatorFaultProfile.None);

        try
        {
            var capabilities =
                await simulator.GetCapabilitiesAsync(context, ct);
            var batchSize = capabilities.Capabilities.BatchMaxSize;
            if (batchSize < 1)
            {
                throw new InvalidOperationException(
                    "The standard WMS simulator reported an invalid batch size.");
            }

            var publishAttemptId =
                SpaceStandardWarehouseDatasetGenerator.CreateDeterministicId(
                    $"publish:{dataset.DatasetVersion}:{dataset.ContentSha256}");
            var batchCount = 0;
            foreach (var chunk in dataset.Locations.Chunk(batchSize))
            {
                ct.ThrowIfCancellationRequested();
                batchCount++;
                var mutations = chunk
                    .Select((location, index) =>
                        SpaceWmsLocationMutation.Create(
                            index + 1,
                            location.LogicalId,
                            location.Code,
                            SpaceWmsLocationAction.Create,
                            new SpaceWmsLocationPath(
                                location.FloorCode,
                                location.FloorLevel,
                                location.ZoneCode,
                                location.AisleCode,
                                location.RackCode,
                                location.Column,
                                location.Level,
                                location.Depth),
                            new Dictionary<string, string?>(
                                StringComparer.Ordinal)
                            {
                                ["datasetVersion"] = dataset.DatasetVersion,
                                ["dataSource"] = "simulated",
                                ["expectedId"] = location.ExpectedId,
                                ["zoneType"] = location.ZoneType,
                                ["xMm"] = location.Xmm.ToString(
                                    CultureInfo.InvariantCulture),
                                ["yMm"] = location.Ymm.ToString(
                                    CultureInfo.InvariantCulture),
                                ["zMm"] = location.Zmm.ToString(
                                    CultureInfo.InvariantCulture),
                            }))
                    .ToArray();
                var preflight = await simulator.PreflightAsync(
                    new SpaceWmsPreflightRequest(
                        context,
                        publishAttemptId,
                        dataset.ContentSha256,
                        capabilities.CapabilityHash,
                        mutations),
                    ct);
                if (!preflight.CanApply)
                {
                    var codes = string.Join(
                        ",",
                        preflight.Issues
                            .Select(issue => issue.Code)
                            .Distinct());
                    throw new InvalidOperationException(
                        "The standard warehouse batch failed preflight: "
                        + $"{codes}.");
                }

                var batch = SpaceWmsBatch.Create(
                    context,
                    publishAttemptId,
                    batchCount,
                    dataset.ContentSha256,
                    mutations);
                var result = await simulator.ApplyBatchAsync(batch, ct);
                var assessment =
                    SpaceWmsContract.AssessBatchResult(batch, result);
                if (assessment.Kind !=
                    SpaceWmsBatchAssessmentKind.Succeeded)
                {
                    throw new InvalidOperationException(
                        "The standard warehouse batch was not applied "
                        + $"atomically: {assessment.Kind}.");
                }
            }

            simulator.SeedInventory(context, dataset.Inventory);
            simulator.SeedTasks(context, dataset.TaskLines);
            simulator.SeedOutboundMovements(
                context,
                CreateOutboundMovements(dataset));
            return new SpaceStandardWarehouseLoadResult(
                dataset.DatasetVersion,
                dataset.ContentSha256,
                batchCount,
                dataset.Counts.Locations,
                dataset.Counts.StockRecords,
                dataset.Counts.PickTasks,
                dataset.Counts.PickTaskLines);
        }
        catch
        {
            simulator.Reset(context);
            throw;
        }
    }

    private static IReadOnlyList<SpaceWmsOutboundMovement> CreateOutboundMovements(
        SpaceStandardWarehouseDataset dataset)
    {
        var lastCompleteDay = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        var movements = new List<SpaceWmsOutboundMovement>();
        for (var index = 0; index < dataset.Skus.Count; index++)
        {
            var sku = dataset.Skus[index];
            var movementCount = 1 + (index % 3);
            for (var movement = 0; movement < movementCount; movement++)
            {
                movements.Add(new SpaceWmsOutboundMovement(
                    $"STD-OUT-{index + 1:0000}-{movement + 1:00}",
                    sku.MaterialNumber,
                    lastCompleteDay.AddDays(-((index * 7 + movement) % 89)),
                    (dataset.Skus.Count - index) * (movement + 1)));
            }
        }
        return movements;
    }
}
