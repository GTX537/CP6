using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Domain;

namespace CP6.Tests.Space;

public sealed class SpacePublishPlanTests
{
    [Fact]
    public void Create_accepts_standard_warehouse_publish_plan()
    {
        const int itemCount = 13_029;
        var payloadHash = new string('a', 64);
        var items = Enumerable.Range(1, itemCount)
            .Select(sequenceNo => new SpacePublishPlanItem(
                sequenceNo,
                "Location",
                Guid.Parse($"00000000-0000-0000-0000-{sequenceNo:X12}"),
                Guid.Parse("10000000-0000-0000-0000-000000000001"),
                SpacePublishActions.Create,
                BeforeHash: null,
                AfterHash: payloadHash,
                BeforeCode: null,
                AfterCode: $"F1-STOR-{sequenceNo:D5}-01-01",
                ExternalBindingId: null,
                PayloadHash: payloadHash,
                ImpactCode: SpacePublishImpactCodes.WmsCreateLocation,
                MasterChanged: true,
                GeometryChanged: true,
                ProvenanceChanged: true,
                WmsChanged: true,
                Blocking: false))
            .ToArray();
        var result = new SpacePublishPlanResult(
            payloadHash,
            items,
            new SpacePublishChangeSummary(itemCount, 0, 0, 0, 0, 0),
            new SpacePublishImpactSummary(itemCount, 0, 0, 0, 0, 0, 0));
        var json = JsonSerializer.Serialize(
            result,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.True(json.Length > 4_000_000);

        var plan = SpacePublishPlan.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            baseVersionId: null,
            Guid.NewGuid(),
            payloadHash,
            "cp6-wms-v1",
            payloadHash,
            payloadHash,
            itemCount,
            json);

        Assert.Equal(itemCount, plan.ItemCount);
        Assert.Equal(json, plan.PlanJson);
    }
}
