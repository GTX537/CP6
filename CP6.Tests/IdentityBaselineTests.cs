using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.CrmIdentity.Baselines;
using CP6.Platform.Messaging;
using CP6.Tests.Persistence.Tenancy;

namespace CP6.Tests;

public sealed class IdentityBaselineTests
{
    private static readonly Guid Tenant = TenantStorageContext.A;
    internal static CrmIdentityRuntime Runtime() => IdentityDirectoryPostgresFixture.Runtime();
    internal static IdentityBaselineSourceV1 Source(CrmIdentityRuntime runtime) => new(Tenant,
        runtime.Options.Issuer, runtime.Options.Tenants[Tenant], runtime.ContractBundleSha256, new string('a', 64));
    internal static IdentityRawSnapshotV1 Snapshot(IdentityBaselineSourceV1 source, string? json = null)
    {
        json ??= "{ \"tenantId\":\"" + Tenant.ToString("D") + "\", \"enabled\":true, \"version\":1, \"expiresAtUtc\":null, \"deleted\":false }\n";
        var bytes = Encoding.UTF8.GetBytes(json);
        return new(IdentityBaselineVerifier.Contract, source, "opaque:boundary-1",
            new("tenant:" + Tenant.ToString("D"), IdentityEventContracts.TenantChanged, 1, IdentityEventContracts.Hash(bytes), false),
            Convert.ToBase64String(bytes));
    }

    [Fact]
    public void Complete_baseline_preserves_original_bytes_and_owns_its_result()
    {
        var runtime = Runtime(); var source = Source(runtime); var raw = Snapshot(source);
        var initial = new[] { raw.Item }; var verifier = new IdentityBaselineVerifier(runtime, source, raw.Boundary, 1, initial);
        verifier.Add(raw); var baseline = verifier.Complete(raw.Boundary, 1, initial);
        initial[0] = initial[0] with { Version = 999 };
        Assert.Equal(1, baseline.Items.Single().Item.Version);
        Assert.Equal(raw.PayloadBase64, baseline.Items.Single().PayloadBase64);
        Assert.Equal(source, baseline.Source); Assert.Equal(raw.Boundary, baseline.Boundary);
        Assert.Equal("SOURCE_SNAPSHOT", baseline.Provenance); Assert.Equal(64, baseline.ContentSha256.Length);
        Assert.Throws<NotSupportedException>(() => ((IList<IdentityRawSnapshotV1>)baseline.Items)[0] = raw with { Boundary = "changed" });
    }

    [Theory]
    [InlineData("tenant.changed")]
    [InlineData("user.changed")]
    [InlineData("department.changed")]
    [InlineData("permission.changed")]
    [InlineData("token.revoked")]
    [InlineData("user.changed", 2)]
    [InlineData("permission.changed", 2)]
    [InlineData("token.revoked", 2)]
    public void All_pinned_identity_types_preserve_snapshot_only_provenance(string name, int major = 1)
    {
        var runtime = Runtime(); var source = Source(runtime);
        var bundle = Cp6ContractBundle.Load(Path.Combine(AppContext.BaseDirectory, "contracts/events/platform"));
        var entry = bundle.Entries.Single(x => x.EventType == "com.gtx537.platform." + name + ".v" + major);
        var example = JsonNode.Parse(File.ReadAllBytes(bundle.GetAssetPath(entry.Examples.Single(x => x.Name == "valid").Path)))!;
        var data = example["data"]!; var aggregate = example["aggregateid"]!.GetValue<string>();
        if (name == "token.revoked")
        { data["issuer"] = source.Issuer; aggregate = IdentityEventContracts.TokenAggregate(source.Issuer, data["jti"]!.GetValue<string>()); }
        var bytes = Encoding.UTF8.GetBytes(" \n" + data.ToJsonString() + " \n");
        var item = new IdentityVersion(aggregate, entry.EventType, data["version"]!.GetValue<int>(), IdentityEventContracts.Hash(bytes), data["deleted"]?.GetValue<bool>() == true);
        var raw = new IdentityRawSnapshotV1(IdentityBaselineVerifier.Contract, source, "1", item, Convert.ToBase64String(bytes));
        var result = Build(runtime, raw); Assert.Equal(raw, result.Items.Single()); Assert.Equal("SOURCE_SNAPSHOT", result.Provenance);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("contract")]
    [InlineData("boundary")]
    [InlineData("version")]
    [InlineData("hash")]
    [InlineData("noncanonical-base64")]
    [InlineData("invalid-base64")]
    [InlineData("null-key")]
    public void Mismatched_or_malformed_snapshot_poison_the_candidate(string scenario)
    {
        var runtime = Runtime(); var raw = Snapshot(Source(runtime));
        var verifier = New(runtime, raw);
        var bad = scenario switch
        {
            "source" => raw with { Source = raw.Source with { StorageIdentitySha256 = new string('b', 64) } },
            "contract" => raw with { Contract = "unknown-v2" },
            "boundary" => raw with { Boundary = "2" },
            "version" => raw with { Item = raw.Item with { Version = 2 } },
            "hash" => raw with { PayloadBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("{}")) },
            "noncanonical-base64" => raw with { PayloadBase64 = raw.PayloadBase64 + "\n" },
            "invalid-base64" => raw with { PayloadBase64 = "!not-base64!" },
            _ => raw with { Item = raw.Item with { AggregateId = null! } }
        };
        Assert.Throws<IdentityBaselineException>(() => verifier.Add(bad));
        Assert.Throws<IdentityBaselineException>(() => verifier.Add(raw));
        Assert.Throws<IdentityBaselineException>(() => verifier.Complete(raw.Boundary, 1, [raw.Item]));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("tenant")]
    [InlineData("version")]
    [InlineData("tombstone")]
    [InlineData("sensitive")]
    [InlineData("utf8")]
    public void Valid_hash_cannot_hide_invalid_payload_semantics(string scenario)
    {
        var runtime = Runtime(); var original = Snapshot(Source(runtime)); var json = Encoding.UTF8.GetString(Convert.FromBase64String(original.PayloadBase64));
        json = scenario switch
        {
            "duplicate" => json.Replace("\"version\":1", "\"version\":1,\"version\":1"),
            "tenant" => json.Replace(Tenant.ToString("D"), TenantStorageContext.B.ToString("D")),
            "version" => json.Replace("\"version\":1", "\"version\":2"),
            "tombstone" => json.Replace("\"deleted\":false", "\"deleted\":true"),
            "sensitive" => json.Replace("\"deleted\":false", "\"deleted\":false,\"extra\":{\"password\":\"not-for-publication\"}"),
            _ => json
        };
        var bytes = scenario == "utf8" ? new byte[] { 0xff } : Encoding.UTF8.GetBytes(json);
        var raw = original with { Item = original.Item with { PayloadSha256 = IdentityEventContracts.Hash(bytes) }, PayloadBase64 = Convert.ToBase64String(bytes) };
        Assert.Throws<IdentityBaselineException>(() => Build(runtime, raw));
    }

    [Fact]
    public void Missing_repeated_unknown_or_changed_directory_cannot_complete()
    {
        var runtime = Runtime(); var raw = Snapshot(Source(runtime));
        Assert.Throws<IdentityBaselineException>(() => new IdentityBaselineVerifier(runtime, raw.Source, raw.Boundary, 2, [raw.Item]));
        Assert.Throws<IdentityBaselineException>(() => new IdentityBaselineVerifier(runtime, raw.Source, raw.Boundary, 2, [raw.Item, raw.Item]));
        Assert.Throws<IdentityBaselineException>(() => new IdentityBaselineVerifier(runtime, raw.Source, raw.Boundary, 1, [raw.Item with { EventType = "unknown.v9" }]));
        Assert.Throws<IdentityBaselineException>(() => New(runtime, raw).Complete(raw.Boundary, 1, [raw.Item]));
        var repeated = New(runtime, raw); repeated.Add(raw); Assert.Throws<IdentityBaselineException>(() => repeated.Add(raw));
        var changed = New(runtime, raw); changed.Add(raw);
        Assert.Throws<IdentityBaselineException>(() => changed.Complete(raw.Boundary, 1, [raw.Item with { Version = 2 }]));
        var moved = New(runtime, raw); moved.Add(raw);
        Assert.Throws<IdentityBaselineException>(() => moved.Complete("different-opaque-boundary", 1, [raw.Item]));
    }

    [Fact]
    public void Digest_is_order_independent_and_binds_exact_bytes_and_source()
    {
        var runtime = Runtime(); var first = Snapshot(Source(runtime));
        var text = "{\"tenantId\":\"" + Tenant.ToString("D") + "\",\"subjectId\":\"role:777\",\"enabled\":true,\"version\":1,\"grants\":[],\"deleted\":false}";
        var bytes = Encoding.UTF8.GetBytes(text);
        var second = first with { Item = new("permission:role:777", IdentityEventContracts.PermissionChanged, 1, IdentityEventContracts.Hash(bytes), false), PayloadBase64 = Convert.ToBase64String(bytes) };
        VerifiedIdentityBaseline Capture(IdentityRawSnapshotV1[] input)
        {
            var verifier = new IdentityBaselineVerifier(runtime, first.Source, first.Boundary, 2, input.Select(x => x.Item));
            foreach (var row in input) verifier.Add(row);
            return verifier.Complete(first.Boundary, 2, input.Reverse().Select(x => x.Item));
        }
        Assert.Equal(Capture([first, second]).ContentSha256, Capture([second, first]).ContentSha256);
        var compact = Snapshot(first.Source, JsonNode.Parse(Convert.FromBase64String(first.PayloadBase64))!.ToJsonString());
        Assert.NotEqual(Build(runtime, first).ContentSha256, Build(runtime, compact).ContentSha256);
        Assert.NotEqual(Build(runtime, first).ContentSha256, Build(runtime, first with { Source = first.Source with { StorageIdentitySha256 = new string('b', 64) } }).ContentSha256);
    }

    [Fact]
    public void Limits_cancellation_and_explicit_empty_source_are_whole_result_boundaries()
    {
        var runtime = Runtime(); var raw = Snapshot(Source(runtime));
        var limited = new IdentityBaselineVerifier(runtime, raw.Source, raw.Boundary, 1, [raw.Item], new(MaximumPayloadBytes: 1));
        Assert.Equal("C02_BASELINE_LIMIT_EXCEEDED", Assert.Throws<IdentityBaselineException>(() => limited.Add(raw)).Message);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var cancelled = New(runtime, raw); Assert.ThrowsAny<OperationCanceledException>(() => cancelled.Add(raw, cancellation.Token));
        Assert.Throws<IdentityBaselineException>(() => cancelled.Complete(raw.Boundary, 1, [raw.Item]));
        var atCompletion = New(runtime, raw); atCompletion.Add(raw);
        Assert.ThrowsAny<OperationCanceledException>(() => atCompletion.Complete(raw.Boundary, 1, [raw.Item], cancellation.Token));
        Assert.Throws<IdentityBaselineException>(() => atCompletion.Complete(raw.Boundary, 1, [raw.Item]));
        var empty = new IdentityBaselineVerifier(runtime, raw.Source, raw.Boundary, 0, []).Complete(raw.Boundary, 0, []);
        Assert.Empty(empty.Items); // Complete empty directory is not authorization or proof of subject dependencies.
    }

    [Fact]
    public void Validation_envelope_does_not_change_the_original_payload_size_boundary()
    {
        var runtime = Runtime(); var raw = Snapshot(Source(runtime));
        var original = Convert.FromBase64String(raw.PayloadBase64);
        var bytes = original.Concat(Enumerable.Repeat((byte)' ', IdentityBaselineVerifier.MaximumItemPayloadBytes - original.Length)).ToArray();
        var maximum = raw with { PayloadBase64 = Convert.ToBase64String(bytes), Item = raw.Item with { PayloadSha256 = IdentityEventContracts.Hash(bytes) } };
        Assert.Equal(maximum.PayloadBase64, Build(runtime, maximum).Items.Single().PayloadBase64);
        var excessive = bytes.Append((byte)' ').ToArray();
        Assert.Throws<IdentityBaselineException>(() => Build(runtime, raw with
        { PayloadBase64 = Convert.ToBase64String(excessive), Item = raw.Item with { PayloadSha256 = IdentityEventContracts.Hash(excessive) } }));
    }

    private static IdentityBaselineVerifier New(CrmIdentityRuntime runtime, IdentityRawSnapshotV1 raw)
        => new(runtime, raw.Source, raw.Boundary, 1, [raw.Item]);
    private static VerifiedIdentityBaseline Build(CrmIdentityRuntime runtime, IdentityRawSnapshotV1 raw)
    { var verifier = New(runtime, raw); verifier.Add(raw); return verifier.Complete(raw.Boundary, 1, [raw.Item]); }

    [Fact]
    public void Opaque_boundary_accepts_valid_unicode_without_normalization_and_rejects_broken_encoding()
    {
        var runtime = Runtime(); var raw = Snapshot(Source(runtime)) with { Boundary = "opaque:\U0001F600:e\u0301" };
        Assert.Equal(raw.Boundary, Build(runtime, raw).Boundary);
        Assert.Throws<IdentityBaselineException>(() => New(runtime, raw with { Boundary = "broken:\ud800" }));
    }
}
