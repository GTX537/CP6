using System.Text.Json;
using System.Text;
using CP6.WebApi.Configuration;
using CP6.WebApi.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CP6.Tests.Platform;

public class PlatformHostIdentityTests
{
    private const string Source = "1277da5e0a20c6d96f0a95d0e9b4f20e0008332a";
    private static readonly string Hash = new('a', 64);

    [Fact]
    public void Complete_matching_observation_does_not_qualify_runtime_or_business()
    {
        var report = Observer(Config(), Facts()).Capture();
        Assert.Equal("Matched", report.IdentityStatus);
        Assert.All(report.Modules, m => Assert.Equal("Matched", m.Status));
        Assert.Equal("Unknown", report.RuntimeQualification);
        Assert.Equal("Unknown", report.BusinessAcceptance);
        Assert.Equal("Unknown", report.TrustState);
        Assert.Equal(Environment.ProcessId, report.ProcessId);
    }

    [Fact]
    public void Package_declaration_and_clr_information_version_are_independent()
    {
        var facts = Facts();
        facts[0] = facts[0] with { AssemblyVersion = "0.8.0.0", InformationalVersion = "0.8.0-alpha.2+" + Source };
        var report = Observer(Config(), facts).Capture();
        Assert.Equal("Matched", report.IdentityStatus);
        Assert.Equal("0.8.0-alpha.2", report.Modules[0].Observed!.InformationalVersion);
        Assert.Equal("0.10.3", report.Modules[0].Observed!.DeclaredPackageVersion);
        Assert.Equal("Unknown", report.TrustState);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Password=private")]
    public void Unavailable_package_declaration_is_unknown_even_when_clr_version_matches(string? declaration)
    {
        var facts = Facts();
        facts[0] = facts[0] with { DeclaredPackageVersion = declaration };
        var report = Observer(Config(), facts).Capture();
        Assert.Equal("Unknown", report.IdentityStatus);
        Assert.Contains("PACKAGE_DECLARATION_UNAVAILABLE", report.Modules[0].ReasonCodes);
        Assert.Null(report.Modules[0].Observed!.DeclaredPackageVersion);
    }

    private const string Dependencies = """
        {
          "runtimeTarget": { "name": ".NETCoreApp,Version=v8.0" },
          "targets": { ".NETCoreApp,Version=v8.0": {
            "CP6.Platform.Abstractions/0.10.3": {
              "runtime": { "lib/net8.0/CP6.Platform.Abstractions.dll": { "assemblyVersion": "0.8.0.0" } }
            }
          } },
          "libraries": { "CP6.Platform.Abstractions/0.10.3": { "type": "package" } }
        }
        """;

    [Fact]
    public void Deps_reader_selects_package_declaration_for_the_runtime_asset()
    {
        var declarations = PlatformHostIdentityDependencies.Parse(Encoding.UTF8.GetBytes(Dependencies));
        Assert.Equal("0.10.3", declarations["CP6.Platform.Abstractions"]);
        Assert.Single(declarations);
        Assert.True(PlatformHostIdentityDependencies.IsAppBaseAsset("CP6.Platform.Abstractions",
            Path.Combine(AppContext.BaseDirectory, "CP6.Platform.Abstractions.dll")));
        Assert.False(PlatformHostIdentityDependencies.IsAppBaseAsset("CP6.Platform.Abstractions",
            Path.Combine(AppContext.BaseDirectory, "foreign", "CP6.Platform.Abstractions.dll")));
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("missing-target")]
    [InlineData("not-package")]
    [InlineData("different-asset")]
    [InlineData("traversal")]
    [InlineData("duplicate-key")]
    [InlineData("ambiguous-owner")]
    public void Invalid_or_ambiguous_deps_cannot_supply_a_package_declaration(string invalid)
    {
        var input = invalid switch
        {
            "malformed" => "{",
            "missing-target" => Dependencies.Replace("\"runtimeTarget\"", "\"missing\""),
            "not-package" => Dependencies.Replace("\"package\"", "\"project\""),
            "different-asset" => Dependencies.Replace("Abstractions.dll", "Other.dll"),
            "traversal" => Dependencies.Replace("lib/net8.0/", "../"),
            "duplicate-key" => Dependencies.Replace("\"type\": \"package\"", "\"type\": \"package\", \"type\": \"package\""),
            _ => Dependencies.Replace("\"targets\": { \".NETCoreApp,Version=v8.0\": {", """
                "targets": { ".NETCoreApp,Version=v8.0": {
                  "Other/1.0.0": { "runtime": { "lib/net8.0/CP6.Platform.Abstractions.dll": {} } },
                """)
        };
        Assert.Empty(PlatformHostIdentityDependencies.Parse(Encoding.UTF8.GetBytes(input)));
    }

    [Fact]
    public void Declarations_do_not_load_missing_assemblies()
    {
        var report = Observer(Config(), []).Capture();
        Assert.Equal("Unknown", report.IdentityStatus);
        Assert.All(report.Modules, m => Assert.Contains("ASSEMBLY_NOT_LOADED", m.ReasonCodes));
    }

    [Fact]
    public void Missing_manifest_member_is_unknown_even_with_matching_loaded_assembly()
    {
        var config = Config();
        config["PlatformHostIdentity:ExpectedModules:0:SourceSha"] = null;
        var report = Observer(config, Facts()).Capture();
        Assert.Equal("Unknown", report.IdentityStatus);
        Assert.Contains("EXPECTED_IDENTITY_INVALID", report.Modules[0].ReasonCodes);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("source")]
    [InlineData("hash")]
    [InlineData("mvid")]
    [InlineData("foreign")]
    [InlineData("duplicate")]
    [InlineData("metadata-conflict")]
    public void Conflicting_loaded_identity_fails_closed(string conflict)
    {
        var facts = Facts().ToList();
        facts[0] = conflict switch
        {
            "version" => facts[0] with { DeclaredPackageVersion = "0.10.2" },
            "source" => facts[0] with { InformationalVersion = "0.10.3+" + new string('b', 40) },
            "hash" => facts[0] with { AssemblySha256 = new string('b', 64) },
            "mvid" => facts[0] with { BackingMvid = Guid.NewGuid() },
            "foreign" => facts[0] with { DefaultLoadContext = false },
            "metadata-conflict" => facts[0] with { SourceRevision = new string('b', 40) },
            _ => facts[0]
        };
        if (conflict == "duplicate") facts.Add(facts[0]);
        Assert.Equal("Mismatch", Observer(Config(), facts).Capture().IdentityStatus);
    }

    [Fact]
    public void Unexpected_platform_assembly_is_mismatch_without_echoing_its_name()
    {
        var facts = Facts().Append(Facts()[0] with { AssemblyName = "CP6.Platform.SecretPassword" });
        var report = Observer(Config(), facts).Capture();
        Assert.Equal("Mismatch", report.IdentityStatus);
        Assert.Contains("UNEXPECTED_PLATFORM_ASSEMBLY", report.ReasonCodes);
        Assert.DoesNotContain("SecretPassword", JsonSerializer.Serialize(report));
    }

    [Fact]
    public void Unsafe_metadata_and_configuration_are_never_returned()
    {
        const string secret = "Password=do-not-return;Host=private";
        var config = Config();
        config["PlatformHostIdentity:ExpectedModules:0:SourceSha"] = secret;
        var facts = Facts();
        facts[0] = facts[0] with { InformationalVersion = secret, SourceRevision = secret };
        var report = Observer(config, facts).Capture();
        Assert.Equal("Unknown", report.IdentityStatus);
        Assert.DoesNotContain(secret, JsonSerializer.Serialize(report));
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("loaded-mvid")]
    [InlineData("backing-mvid")]
    [InlineData("read-failure")]
    [InlineData("version")]
    [InlineData("source-suffix")]
    public void Incomplete_or_unreadable_identity_remains_unknown(string missing)
    {
        var facts = Facts();
        facts[0] = missing switch
        {
            "hash" => facts[0] with { AssemblySha256 = null },
            "loaded-mvid" => facts[0] with { LoadedMvid = Guid.Empty },
            "backing-mvid" => facts[0] with { BackingMvid = null },
            "read-failure" => facts[0] with { ReadErrorCode = "private-path-and-error-message" },
            "version" => facts[0] with { InformationalVersion = new string('a', 1024) },
            _ => facts[0] with { InformationalVersion = "0.10.3+invalid", SourceRevision = Source }
        };
        var report = Observer(Config(), facts).Capture();
        Assert.Equal("Unknown", report.IdentityStatus);
        Assert.False(report.Rollback.Allowed);
        Assert.DoesNotContain("private-path-and-error-message", JsonSerializer.Serialize(report));
    }

    [Fact]
    public void Unsafe_assembly_version_and_hash_are_omitted_from_observed_output()
    {
        const string secret = "Password=private;Host=private";
        var facts = Facts();
        facts[0] = facts[0] with { AssemblyVersion = secret, AssemblySha256 = secret };
        var report = Observer(Config(), facts).Capture();
        Assert.Equal("Unknown", report.IdentityStatus);
        Assert.Null(report.Modules[0].Observed!.AssemblyVersion);
        Assert.Null(report.Modules[0].Observed!.AssemblySha256);
        Assert.DoesNotContain(secret, JsonSerializer.Serialize(report));
    }

    [Fact]
    public void A_conflict_takes_precedence_over_another_modules_missing_evidence()
    {
        var facts = Facts();
        facts[0] = facts[0] with { AssemblySha256 = null };
        facts[1] = facts[1] with { BackingMvid = Guid.NewGuid() };
        var report = Observer(Config(), facts).Capture();
        Assert.Equal("Unknown", report.Modules[0].Status);
        Assert.Equal("Mismatch", report.IdentityStatus);
        Assert.Equal("IDENTITY_MISMATCH", report.Rollback.ReasonCode);
    }

    [Fact]
    public void Duplicate_expectations_and_unknown_package_are_rejected()
    {
        var config = Config();
        config["PlatformHostIdentity:ExpectedModules:5:PackageId"] = "CP6.Platform.Abstractions";
        var report = Observer(config, Facts()).Capture();
        Assert.Equal("Mismatch", report.IdentityStatus);
        config["PlatformHostIdentity:ExpectedModules:5:PackageId"] = "CP6.Platform.Release";
        Assert.Equal("Mismatch", Observer(config, Facts()).Capture().IdentityStatus);
    }

    [Fact]
    public void Expected_identity_is_snapshotted_at_registration()
    {
        var config = Config();
        var services = new ServiceCollection().AddPlatformHostIdentity(config);
        config["PlatformHostIdentity:ExpectedModules:0:SourceSha"] = "secret-after-startup";
        using var provider = services.BuildServiceProvider();
        var report = provider.GetRequiredService<PlatformHostIdentityObserver>().Capture();
        Assert.Equal(Source, report.Modules[0].Expected?.SourceSha);
    }

    internal static PlatformHostIdentityObserver Observer(IConfiguration config, IEnumerable<PlatformHostAssemblyObservation> facts) =>
        new(PlatformHostIdentityManifest.Read(config), () => facts.ToArray());

    internal static IConfigurationRoot Config()
    {
        var values = new Dictionary<string, string?>();
        for (var i = 0; i < PlatformHostIdentityManifest.PackageIds.Count; i++)
        {
            var key = $"PlatformHostIdentity:ExpectedModules:{i}";
            values[key + ":PackageId"] = PlatformHostIdentityManifest.PackageIds[i];
            values[key + ":PackageVersion"] = "0.10.3";
            values[key + ":SourceSha"] = Source;
            values[key + ":AssemblySha256"] = Hash;
        }
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    internal static PlatformHostAssemblyObservation[] Facts() => PlatformHostIdentityManifest.PackageIds.Select(id =>
    {
        var mvid = Guid.NewGuid();
        return new PlatformHostAssemblyObservation(id, true, "0.10.3.0", "0.10.3+" + Source, null,
            mvid, mvid, Hash, null, "0.10.3");
    }).ToArray();
}
