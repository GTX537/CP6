using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Crm.Cutover;
using Xunit;

namespace CP6.Crm.SourceFence.Tests;

public sealed class SourceFrozenProofTests
{
    private const string Code = "C04A_SOURCE_FROZEN_PROOF_INVALID";
    private const string Canonical = """
        {"Challenge":"10000000-0000-0000-0000-000000000001","FreezeRunId":"20000000-0000-0000-0000-000000000002","SourceFreezeRequestSha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","TargetSetAnchorSha256":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","SourceIdentitySha256":"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc","ServerName":"machine\\instance","DatabaseName":"source_database","DatabaseGuid":"30000000-0000-0000-0000-000000000003","FrozenScopeSha256":"dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd","Format":"CP6.C04A.SourceFrozenProof.v1","State":"Frozen","Generation":1,"SourceFrozenAtObservation":true,"CompleteWriteFenceVerified":false,"ApprovalIndependentlyVerified":false}
        """;

    private static SourceFrozenProof Proof() => new(Guid.Parse("10000000-0000-0000-0000-000000000001"),
        Guid.Parse("20000000-0000-0000-0000-000000000002"), new string('a', 64), new string('b', 64),
        new string('c', 64), "machine\\instance", "source_database", Guid.Parse("30000000-0000-0000-0000-000000000003"), new string('d', 64));

    [Fact]
    public void Canonical_observation_preserves_identity_challenge_and_truthful_acceptance_flags()
    {
        var proof = Proof();
        proof.Validate();
        Assert.Equal(Canonical, JsonSerializer.Serialize(proof));
        Assert.Equal(proof, SourceFrozenProof.Parse(Canonical));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Canonical))).ToLowerInvariant(), proof.Digest());
        Assert.True(proof.SourceFrozenAtObservation);
        Assert.False(proof.CompleteWriteFenceVerified);
        Assert.False(proof.ApprovalIndependentlyVerified);
        Assert.NotEqual(proof.Digest(), (proof with { Challenge = Guid.NewGuid() }).Digest());
        Assert.NotEqual(proof.Digest(), (proof with { TargetSetAnchorSha256 = new string('e', 64) }).Digest());
        Assert.NotEqual(proof.Digest(), (proof with { SourceIdentitySha256 = new string('f', 64) }).Digest());
    }

    [Theory]
    [InlineData("empty-challenge")]
    [InlineData("empty-run")]
    [InlineData("empty-database-guid")]
    [InlineData("null-request-hash")]
    [InlineData("uppercase-anchor-hash")]
    [InlineData("short-identity-hash")]
    [InlineData("nonhex-scope-hash")]
    [InlineData("null-server")]
    [InlineData("empty-database")]
    [InlineData("whitespace-server")]
    [InlineData("padded-database")]
    [InlineData("long-server")]
    [InlineData("control-database")]
    [InlineData("unpaired-surrogate-server")]
    public void Invalid_bindings_and_names_have_one_fixed_error(string scenario)
    {
        var proof = Proof();
        proof = scenario switch
        {
            "empty-challenge" => proof with { Challenge = Guid.Empty },
            "empty-run" => proof with { FreezeRunId = Guid.Empty },
            "empty-database-guid" => proof with { DatabaseGuid = Guid.Empty },
            "null-request-hash" => proof with { SourceFreezeRequestSha256 = null! },
            "uppercase-anchor-hash" => proof with { TargetSetAnchorSha256 = new string('A', 64) },
            "short-identity-hash" => proof with { SourceIdentitySha256 = new string('a', 63) },
            "nonhex-scope-hash" => proof with { FrozenScopeSha256 = new string('g', 64) },
            "null-server" => proof with { ServerName = null! },
            "empty-database" => proof with { DatabaseName = "" },
            "whitespace-server" => proof with { ServerName = "   " },
            "padded-database" => proof with { DatabaseName = " database " },
            "long-server" => proof with { ServerName = new string('s', 129) },
            "control-database" => proof with { DatabaseName = "database\u0000hidden" },
            "unpaired-surrogate-server" => proof with { ServerName = "server\ud800" },
            _ => throw new InvalidOperationException()
        };
        Assert.Equal(Code, Assert.Throws<TargetRollbackException>(proof.Validate).Code);
        // System.Text.Json replaces unpaired UTF-16 input on serialization. All other
        // invalid typed bindings must also fail when received as canonical JSON.
        if (scenario != "unpaired-surrogate-server")
            Assert.Equal(Code, Assert.Throws<TargetRollbackException>(() => SourceFrozenProof.Parse(JsonSerializer.Serialize(proof))).Code);
    }

    [Theory]
    [InlineData("Format", "changed")]
    [InlineData("State", "Reopened")]
    [InlineData("Generation", "2")]
    [InlineData("SourceFrozenAtObservation", "false")]
    [InlineData("CompleteWriteFenceVerified", "true")]
    [InlineData("ApprovalIndependentlyVerified", "true")]
    public void Getter_only_values_cannot_be_forged_or_omitted(string property, string value)
    {
        var node = JsonNode.Parse(Canonical)!.AsObject();
        node[property] = property is "Format" or "State" ? JsonValue.Create(value) : JsonNode.Parse(value);
        Assert.Equal(Code, Assert.Throws<TargetRollbackException>(() => SourceFrozenProof.Parse(node.ToJsonString())).Code);
        node.Remove(property);
        Assert.Equal(Code, Assert.Throws<TargetRollbackException>(() => SourceFrozenProof.Parse(node.ToJsonString())).Code);
    }

    [Theory]
    [InlineData("unknown-property")]
    [InlineData("duplicate-property")]
    [InlineData("duplicate-getter")]
    [InlineData("wrong-case")]
    [InlineData("whitespace")]
    [InlineData("alternate-escape")]
    [InlineData("null-document")]
    [InlineData("null-string")]
    [InlineData("invalid-json")]
    [InlineData("too-many-ascii-bytes")]
    [InlineData("too-many-utf8-bytes")]
    public void Parse_accepts_only_bounded_canonical_PascalCase_bytes(string scenario)
    {
        var serialized = scenario switch
        {
            "unknown-property" => Canonical.Insert(1, "\"Extra\":true,"),
            "duplicate-property" => Canonical.Insert(1, "\"Challenge\":\"10000000-0000-0000-0000-000000000001\","),
            "duplicate-getter" => Canonical.Insert(1, "\"CompleteWriteFenceVerified\":false,"),
            "wrong-case" => Canonical.Replace("\"Challenge\"", "\"challenge\"", StringComparison.Ordinal),
            "whitespace" => Canonical + "\n",
            "alternate-escape" => Canonical.Replace("source_database", "source_\\u0064atabase", StringComparison.Ordinal),
            "null-document" => "null",
            "null-string" => null!,
            "invalid-json" => "{",
            "too-many-ascii-bytes" => new string(' ', 16385),
            "too-many-utf8-bytes" => new string('\u4e2d', 6000),
            _ => throw new InvalidOperationException()
        };
        Assert.Equal(Code, Assert.Throws<TargetRollbackException>(() => SourceFrozenProof.Parse(serialized)).Code);
    }
}
