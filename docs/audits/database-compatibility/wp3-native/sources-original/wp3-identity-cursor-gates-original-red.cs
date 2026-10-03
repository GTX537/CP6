using System.Globalization;
using System.Text;
using System.Text.Json;
using CP6.Core.Services.CrmIdentity;
using CP6.Entity.DomainModels.Sys;
using CP6.Platform.Messaging;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace CP6.DatabaseCompatibility.RuntimeProbe;

internal static class IdentityCursorGates
{
    private const string Issuer = "https://identity.cp6.test";

    public static async Task<string> GenerationAsync(RuntimeFixture fixture)
    {
        await fixture.VerifyOwnerAsync();
        var tenants = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var nonce = Guid.NewGuid().ToString("N");
        var temporaryRoot = Path.GetFullPath(Path.GetTempPath());
        var keys = Path.GetFullPath(Path.Combine(temporaryRoot, "CP6Wp3Identity_" + nonce));
        RequireExactTemporaryDirectory(temporaryRoot, keys, nonce);
        ProbeAssert.Require(!Directory.Exists(keys), "The Data Protection key directory must be a new exact task nonce.");
        var contracts = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "contracts", "events", "platform"));
        ProbeAssert.Require(Directory.Exists(contracts), "The native probe must run from its source worktree containing the pinned platform contract bundle.");
        var runtime = Runtime(contracts, tenants);
        try
        {
            Directory.CreateDirectory(keys);
            var protection = DataProtectionProvider.Create(new DirectoryInfo(keys),
                options => options.SetApplicationName("CP6.WP3.Identity." + nonce));
            await using (var setup = fixture.Core())
            {
                setup.CrmIdentityBootstrapStates.AddRange(tenants.Select(tenant => new CrmIdentityBootstrapState
                {
                    TenantId = tenant,
                    ContractBundleSha256 = runtime.ContractBundleSha256,
                    CompletedAtUtc = runtime.Clock.GetUtcNow()
                }));
                await setup.SaveChangesAsync();
            }

            ProbeAssert.Require(await GenerationAsync(fixture, tenants[0]) == 0,
                "An empty prepared tenant must have durable generation0 before snapshot writes.");
            await using var core = fixture.Core();
            var reader = new IdentitySnapshotReader(core, runtime, protection);
            var empty = await reader.ReadVersionsAsync(tenants[0], null, 1);
            ProbeAssert.Require(empty.Boundary == "0" && empty.Items.Length == 0 && empty.NextCursor is null,
                "The actual Reader must return generation0 and no cursor for an empty prepared tenant.");

            await using (var seed = fixture.Core())
            {
                seed.CrmIdentitySnapshots.AddRange(Enumerable.Range(1, 3)
                    .Select(role => Snapshot(runtime, tenants[0], role)));
                await seed.SaveChangesAsync();
            }
            var generation = await GenerationAsync(fixture, tenants[0]);
            ProbeAssert.Require(generation > 0, "Actual native snapshot writes must advance the same tenant's durable generation.");
            var page = await reader.ReadVersionsAsync(tenants[0], null, 1);
            ProbeAssert.Require(page.Boundary == generation.ToString(CultureInfo.InvariantCulture),
                "The actual Reader page Boundary must equal its own tenant's persisted Generation, rather than MAX(RowVersion).");
            ProbeAssert.Require(page.Items.Length == 1 && page.Items[0].AggregateId == "permission:role:1" &&
                page.NextCursor is not null,
                "Three real valid ASCII snapshots must produce the first ordered item and a protected continuation cursor.");
            return "Actual Reader: prepared empty tenant returned generation0; three valid persisted snapshots produced an ordered first page whose Boundary equaled the tenant's durable native generation. Further cursor lifecycle checks remain separate.";
        }
        finally
        {
            try
            {
                await using var cleanup = fixture.Core();
                await using var transaction = await cleanup.Database.BeginTransactionAsync();
                await cleanup.CrmIdentitySnapshots.Where(value => value.TenantId == tenants[0] || value.TenantId == tenants[1]).ExecuteDeleteAsync();
                await cleanup.CrmIdentityBootstrapStates.Where(value => value.TenantId == tenants[0] || value.TenantId == tenants[1]).ExecuteDeleteAsync();
                // Snapshot deletion itself advances generation; remove that generated row last.
                await cleanup.CrmIdentityTenantGenerations.Where(value => value.TenantId == tenants[0] || value.TenantId == tenants[1]).ExecuteDeleteAsync();
                await transaction.CommitAsync();
                ProbeAssert.Require(!await cleanup.CrmIdentitySnapshots.AnyAsync(value => value.TenantId == tenants[0] || value.TenantId == tenants[1]) &&
                    !await cleanup.CrmIdentityBootstrapStates.AnyAsync(value => value.TenantId == tenants[0] || value.TenantId == tenants[1]) &&
                    !await cleanup.CrmIdentityTenantGenerations.AnyAsync(value => value.TenantId == tenants[0] || value.TenantId == tenants[1]),
                    "Only the exact two owned tenant snapshot/bootstrap/generation fixtures must be absent after cleanup.");
            }
            finally
            {
                RequireExactTemporaryDirectory(temporaryRoot, keys, nonce);
                if (Directory.Exists(keys)) Directory.Delete(keys, recursive: true);
                ProbeAssert.Require(!Directory.Exists(keys), "The exact nonce Data Protection directory must be removed without archiving keys.");
            }
        }
    }

    private static CrmIdentityRuntime Runtime(string contracts, Guid[] tenants) => new(new CrmIdentityOptions
    {
        Enabled = true,
        Issuer = Issuer,
        Tenants = tenants.ToDictionary(tenant => tenant, _ => "us"),
        ProjectionReaderClientIds = ["wp3-native-reader"]
    }, new IdentityEventValidator(Cp6ContractBundle.Load(contracts), Issuer), contractDirectory: contracts);

    private static CrmIdentitySnapshot Snapshot(CrmIdentityRuntime runtime, Guid tenant, int role)
    {
        var aggregate = "permission:role:" + role.ToString(CultureInfo.InvariantCulture);
        var data = new PermissionIdentityData(tenant, "role:" + role.ToString(CultureInfo.InvariantCulture), true, 1, []);
        var envelope = IdentityEventContracts.Create(tenant, aggregate, 1, IdentityEventContracts.PermissionChanged,
            data, runtime.Options.Tenants[tenant], "wp3-native-correlation", "wp3-native-command", runtime.Clock);
        ProbeAssert.Require(runtime.Validator.Validate(envelope).IsValid, "The snapshot fixture must pass the actual pinned platform identity event validator.");
        var payload = JsonSerializer.Serialize(data, IdentityEventContracts.Json);
        return new()
        {
            TenantId = tenant,
            AggregateId = aggregate,
            EventType = IdentityEventContracts.PermissionChanged,
            Version = 1,
            PayloadJson = payload,
            PayloadSha256 = IdentityEventContracts.Hash(Encoding.UTF8.GetBytes(payload)),
            IsDeleted = false,
            UpdatedAtUtc = runtime.Clock.GetUtcNow()
        };
    }

    private static async Task<long> GenerationAsync(RuntimeFixture fixture, Guid tenant)
    {
        await using var core = fixture.Core();
        return await core.CrmIdentityTenantGenerations.Where(value => value.TenantId == tenant)
            .Select(value => (long?)value.Generation).SingleOrDefaultAsync() ?? 0;
    }

    private static void RequireExactTemporaryDirectory(string temporaryRoot, string keys, string nonce)
    {
        var expected = Path.GetFullPath(Path.Combine(temporaryRoot, "CP6Wp3Identity_" + nonce));
        ProbeAssert.Require(Path.IsPathFullyQualified(keys) && string.Equals(keys, expected, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Path.GetDirectoryName(keys), temporaryRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase),
            "Recursive key cleanup is confined to the verified absolute task nonce directory under the temporary root.");
    }
}
