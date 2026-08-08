using CP6.Space.Domain;

namespace CP6.Space.UnitTests;

public sealed class SpaceElementCommandTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid VersionId = Guid.NewGuid();
    private static readonly Guid FloorLogicalId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    [Fact]
    public void Batch_and_command_keep_auditable_before_after_values()
    {
        var batch = SpaceElementCommandBatch.Create(
            TenantId,
            Guid.NewGuid(),
            VersionId,
            FloorLogicalId,
            Guid.NewGuid(),
            expectedFloorRevision: 4,
            new string('a', 64),
            ActorId,
            DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Utc));

        var command = SpaceElementCommandRecord.Create(
            TenantId,
            Guid.NewGuid(),
            batch,
            sequenceNo: 0,
            "UpdateProperties",
            Guid.NewGuid(),
            """{"x":20}""",
            """{"x":10}""",
            """{"x":20}""");
        batch.Complete(
            resultFloorRevision: 5,
            resultVersionContentRevision: 12,
            """{"floorRevision":5}""");

        Assert.Equal(5, batch.ResultFloorRevision);
        Assert.Equal(12, batch.ResultVersionContentRevision);
        Assert.Equal(batch.Id, command.CommandBatchId);
        Assert.Equal("""{"x":10}""", command.BeforeJson);
        Assert.Equal("""{"x":20}""", command.AfterJson);
    }

    [Fact]
    public void Batch_completion_is_one_way_and_requires_revision_progress()
    {
        var batch = SpaceElementCommandBatch.Create(
            TenantId,
            Guid.NewGuid(),
            VersionId,
            FloorLogicalId,
            Guid.NewGuid(),
            expectedFloorRevision: 4,
            new string('b', 64),
            ActorId,
            DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Utc));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            batch.Complete(4, 1, "{}"));

        batch.Complete(5, 1, "{}");

        Assert.Throws<InvalidOperationException>(() =>
            batch.Complete(6, 2, "{}"));
    }

    [Fact]
    public void Attribute_removal_uses_soft_delete_semantics()
    {
        var element = SpaceElementRevision.Create(
            TenantId,
            VersionId,
            Guid.NewGuid(),
            FloorLogicalId,
            SpaceElementTypes.Column,
            """{"schemaVersion":1,"kind":"box","width":400,"height":5000,"depth":400}""");
        var attribute = SpaceElementAttribute.Create(
            TenantId,
            element,
            SpaceElementAttributeNamespaces.Design,
            "label",
            SpaceElementAttributeValueTypes.String,
            "Column A");

        attribute.Remove();

        Assert.True(attribute.IsDeleted);
    }
}
