using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;

namespace CP6.Space.UnitTests;

public sealed class SpaceAiProposalPatchPolicyTests
{
    [Fact]
    public void Modify_canonicalizes_values_and_locks_exactly_the_patch_paths()
    {
        var result = SpaceAiProposalPatchPolicyV1.Apply(
            "Rack",
            """{"points":[]}""",
            """{"rackType":"Selective","name":"AI Rack"}""",
            """{"zoneSourceKey":"zone-1","aisleSourceKey":"aisle-1"}""",
            [
                Operation("/attributes/name", " Human Rack "),
                Operation("/relations/zoneSourceKey", "zone-2"),
            ],
            ["/relations/zoneSourceKey", "/attributes/name"]);

        Assert.Equal(
            """{"name":"Human Rack","rackType":"Selective"}""",
            result.AttributesJson);
        Assert.Equal(
            """["/attributes/name","/relations/zoneSourceKey"]""",
            result.LockedFieldsJson);
        Assert.Contains("Human Rack", result.FinalSnapshotJson);
    }

    [Theory]
    [InlineData("add", "/attributes/name", "Rack")]
    [InlineData("replace", "/geometry/x", "1")]
    [InlineData("replace", "/attributes/rackType", "Untrusted")]
    public void Modify_rejects_non_replace_denied_paths_and_unknown_enums(
        string op,
        string path,
        string value)
    {
        var operation = Operation(path, value, op);

        Assert.Throws<SpaceProposalPatchException>(() =>
            SpaceAiProposalPatchPolicyV1.Apply(
                "Rack",
                "{}",
                """{"name":"Rack","rackType":"Selective"}""",
                """{"zoneSourceKey":"zone-1","aisleSourceKey":"aisle-1"}""",
                [operation],
                [path]));
    }

    [Fact]
    public void Modify_rejects_lock_sets_that_do_not_match_patch_paths()
    {
        Assert.Throws<SpaceProposalPatchException>(() =>
            SpaceAiProposalPatchPolicyV1.Apply(
                "Floor",
                "{}",
                """{"name":"Floor 1"}""",
                "{}",
                [Operation("/attributes/name", "Floor A")],
                []));
    }

    private static SpaceAiProposalPatchOperationDto Operation(
        string path,
        string value,
        string op = "replace")
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value));
        return new SpaceAiProposalPatchOperationDto(
            op,
            path,
            document.RootElement.Clone());
    }
}
