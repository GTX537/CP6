using System.Text.Json;
using CP6.Crm.Cutover;
using Xunit;

namespace CP6.Crm.SourceFence.Tests;

public sealed class TargetRollbackSetTests
{
    private static readonly Guid First = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Second = Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static TargetRollbackAnchor[] Anchors() => [new(First, new string('a', 64)), new(Second, new string('b', 64))];

    private static TargetRollbackReceipt Receipt(TargetRollbackAnchor anchor)
    {
        var permit = new TargetRollbackPermit(Guid.Parse("30000000-0000-0000-0000-000000000003"), anchor.OrganizationId,
            anchor.TargetBindingSha256, [new string('c', 64), new string('d', 64)], new string('e', 64));
        return new(permit, permit.Digest(), new("server", "machine", "target_" + anchor.OrganizationId.ToString("N"), First, First, First, First),
            0, new string('f', 64), new DateTimeOffset(2026, 9, 14, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Anchor_has_fixed_canonical_Pascal_bytes_and_digest()
    {
        var set = new TargetRollbackSetAnchor(Anchors());
        set.Validate();
        const string expected = "{\"Targets\":[{\"OrganizationId\":\"10000000-0000-0000-0000-000000000001\",\"TargetBindingSha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"Format\":\"CP6.CRM.TargetRollbackAnchor.v1\"},{\"OrganizationId\":\"20000000-0000-0000-0000-000000000002\",\"TargetBindingSha256\":\"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\",\"Format\":\"CP6.CRM.TargetRollbackAnchor.v1\"}],\"Format\":\"CP6.CRM.TargetRollbackSetAnchor.v1\"}";
        Assert.Equal(expected, JsonSerializer.Serialize(set));
        Assert.Equal("14b8ced0208de9d21c44cd5f836713e8008f78d3ba793bedd189e950f5755fcc", set.Digest());
        Assert.NotEqual(set.Digest(), new TargetRollbackSetAnchor([set.Targets[0], set.Targets[1] with { TargetBindingSha256 = new string('c', 64) }]).Digest());
        Assert.NotEqual(set.Digest(), new TargetRollbackSetAnchor([set.Targets[0], set.Targets[1] with { OrganizationId = Guid.Parse("20000000-0000-0000-0000-000000000003") }]).Digest());
        Assert.NotEqual(set.Digest(), set.Targets[0].Digest());
    }

    [Theory]
    [InlineData("null-list")]
    [InlineData("empty")]
    [InlineData("single")]
    [InlineData("too-many")]
    [InlineData("null-anchor")]
    [InlineData("empty-organization")]
    [InlineData("uppercase-binding")]
    [InlineData("unsorted")]
    [InlineData("duplicate-organization")]
    [InlineData("duplicate-binding")]
    public void Anchor_rejects_incomplete_or_ambiguous_sets(string scenario)
    {
        var targets = Anchors();
        targets = scenario switch
        {
            "null-list" => null!,
            "empty" => [],
            "single" => [targets[0]],
            "too-many" => Enumerable.Repeat(targets[0], 17).ToArray(),
            "null-anchor" => [targets[0], null!],
            "empty-organization" => [targets[0] with { OrganizationId = Guid.Empty }, targets[1]],
            "uppercase-binding" => [targets[0] with { TargetBindingSha256 = new string('A', 64) }, targets[1]],
            "unsorted" => [targets[1], targets[0]],
            "duplicate-organization" => [targets[0], targets[1] with { OrganizationId = First }],
            "duplicate-binding" => [targets[0], targets[1] with { TargetBindingSha256 = targets[0].TargetBindingSha256 }],
            _ => throw new InvalidOperationException()
        };
        Assert.Equal("C04A_TARGET_ROLLBACK_SET_ANCHOR_MISMATCH", Assert.Throws<TargetRollbackException>(() => new TargetRollbackSetAnchor(targets).Validate()).Code);
    }

    [Fact]
    public void Receipt_binds_every_terminal_receipt_and_its_canonical_anchor()
    {
        var targets = Anchors().Select(Receipt).ToArray();
        var set = new TargetRollbackSetReceipt(targets);
        set.Validate();
        Assert.Equal(new TargetRollbackSetAnchor(Anchors()).Digest(), set.Anchor.Digest());
        var roundTrip = JsonSerializer.Deserialize<TargetRollbackSetReceipt>(JsonSerializer.Serialize(set))!;
        roundTrip.Validate();
        Assert.Equal(set.Digest(), roundTrip.Digest());
        Assert.NotEqual(set.Digest(), targets[0].Digest());
        for (var index = 0; index < targets.Length; index++)
        {
            var changed = targets.ToArray();
            changed[index] = changed[index] with { RecordedAtUtc = changed[index].RecordedAtUtc.AddTicks(1) };
            var changedSet = new TargetRollbackSetReceipt(changed);
            changedSet.Validate();
            Assert.NotEqual(set.Digest(), changedSet.Digest());
            Assert.Equal(set.Anchor.Digest(), changedSet.Anchor.Digest());
        }
    }

    [Theory]
    [InlineData("null-list")]
    [InlineData("empty")]
    [InlineData("single")]
    [InlineData("too-many")]
    [InlineData("null-receipt")]
    [InlineData("null-permit")]
    [InlineData("null-identity")]
    [InlineData("permit-hash")]
    [InlineData("protection-hash")]
    [InlineData("generation")]
    [InlineData("non-utc")]
    [InlineData("unsorted")]
    [InlineData("duplicate")]
    public void Receipt_validation_rejects_invalid_individual_proofs_and_sets(string scenario)
    {
        var targets = Anchors().Select(Receipt).ToArray();
        targets = scenario switch
        {
            "null-list" => null!,
            "empty" => [],
            "single" => [targets[0]],
            "too-many" => Enumerable.Repeat(targets[0], 17).ToArray(),
            "null-receipt" => [targets[0], null!],
            "null-permit" => [targets[0], targets[1] with { Permit = null! }],
            "null-identity" => [targets[0], targets[1] with { Target = null! }],
            "permit-hash" => [targets[0], targets[1] with { PermitSha256 = new string('a', 64) }],
            "protection-hash" => [targets[0], targets[1] with { ProtectionSha256 = "bad" }],
            "generation" => [targets[0], targets[1] with { ClosedGeneration = -1 }],
            "non-utc" => [targets[0], targets[1] with { RecordedAtUtc = targets[1].RecordedAtUtc.ToOffset(TimeSpan.FromHours(1)) }],
            "unsorted" => [targets[1], targets[0]],
            "duplicate" => [targets[0], targets[0]],
            _ => throw new InvalidOperationException()
        };
        Assert.Throws<TargetRollbackException>(() => new TargetRollbackSetReceipt(targets).Validate());
    }
}
