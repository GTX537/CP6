using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Services.CrmIdentity;
using CP6.Entity.DomainModels.Sys;
using CP6.Platform.Messaging;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace CP6.DatabaseCompatibility.RuntimeProbe;

internal static class IdentityCursorGates
{
    private const string Issuer = "https://identity.cp6.test";
    private const string CursorPurpose = "CP6.C02.IdentityVersions.Cursor.v2";
    private static readonly string[] Aggregates = ["permission:role:1", "permission:role:2", "permission:role:3"];

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
            var originalCursor = page.NextCursor!;
            var all = page.Items.Concat(await ContinueAsync(reader, tenants[0], originalCursor, generation)).ToArray();
            ProbeAssert.Require(all.Select(value => value.AggregateId).SequenceEqual(Aggregates) &&
                all.Select(value => value.AggregateId).Distinct(StringComparer.Ordinal).Count() == 3,
                "Actual size1 paging must return all three ordered ASCII snapshots exactly once without omissions.");
            await RequireRejectedAsync(reader, tenants[1], originalCursor, "C02_INVALID_CURSOR");
            await RequireRejectedAsync(reader, tenants[0], MintCursor(protection, "CP6.C02.IdentityVersions.Cursor.v1",
                tenants[0], generation, Aggregates[0], runtime.Clock.GetUtcNow().AddMinutes(10)), "C02_INVALID_CURSOR");
            await RequireRejectedAsync(reader, tenants[0], MintCursor(protection, CursorPurpose,
                tenants[0], generation, Aggregates[0], runtime.Clock.GetUtcNow().AddMinutes(-1)), "C02_INVALID_CURSOR");
            await RequireRejectedAsync(reader, tenants[0], "unprotected-native-cursor", "C02_INVALID_CURSOR");
            await RequireRejectedAsync(reader, tenants[0], MintCursor(protection, CursorPurpose,
                tenants[0], generation, null, runtime.Clock.GetUtcNow().AddMinutes(10)), "C02_INVALID_CURSOR");

            var continued = await RunChildAsync(fixture, tenants, nonce, keys, originalCursor, generation, changed: false);
            await using (var other = fixture.Core())
            {
                other.CrmIdentitySnapshots.Add(Snapshot(runtime, tenants[1], 1));
                await other.SaveChangesAsync();
            }
            var otherGeneration = await GenerationAsync(fixture, tenants[1]);
            ProbeAssert.Require(otherGeneration > 0 && await GenerationAsync(fixture, tenants[0]) == generation,
                "A committed write to the other owned tenant must advance only its own durable generation.");
            await RequireContinuationAsync(reader, tenants[0], originalCursor, generation);

            await using (var rolledBack = fixture.Core())
            await using (var transaction = await rolledBack.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted))
            {
                var row = await rolledBack.CrmIdentitySnapshots.SingleAsync(value =>
                    value.TenantId == tenants[0] && value.AggregateId == Aggregates[2]);
                Rewrite(row, Snapshot(runtime, tenants[0], 3, version: 2));
                await rolledBack.SaveChangesAsync();
                var inTransaction = await rolledBack.CrmIdentityTenantGenerations.AsNoTracking()
                    .Where(value => value.TenantId == tenants[0]).Select(value => value.Generation).SingleAsync();
                ProbeAssert.Require(inTransaction > generation,
                    "The actual snapshot mutation must advance generation inside its own uncommitted transaction before rollback.");
                await transaction.RollbackAsync();
            }
            ProbeAssert.Require(await GenerationAsync(fixture, tenants[0]) == generation &&
                (await reader.ReadSnapshotAsync(tenants[0], Aggregates[2]))?.Version == 1,
                "Full rollback must restore both the committed tenant generation and original snapshot version1.");
            await RequireContinuationAsync(reader, tenants[0], originalCursor, generation);

            await MutateAsync(fixture, runtime, tenants[0], 3, version: 2);
            var updatedGeneration = await RequireAdvancedAsync(fixture, tenants[0], generation);
            await RequireRejectedAsync(reader, tenants[0], originalCursor, "C02_SNAPSHOT_BOUNDARY_CHANGED");
            var rejected = await RunChildAsync(fixture, tenants, nonce, keys, originalCursor, generation, changed: true);
            ProbeAssert.Require((await reader.ReadSnapshotAsync(tenants[0], Aggregates[2]))?.Version == 2,
                "The committed update must be visible through the actual Reader snapshot API.");

            var beforeTombstone = await reader.ReadVersionsAsync(tenants[0], null, 1);
            RequireCurrentPage(beforeTombstone, updatedGeneration);
            await MutateAsync(fixture, runtime, tenants[0], 2, version: 2, deleted: true);
            var tombstoneGeneration = await RequireAdvancedAsync(fixture, tenants[0], updatedGeneration);
            await RequireRejectedAsync(reader, tenants[0], beforeTombstone.NextCursor!, "C02_SNAPSHOT_BOUNDARY_CHANGED");
            var tombstone = await reader.ReadSnapshotAsync(tenants[0], Aggregates[1]);
            ProbeAssert.Require(tombstone is { IsDeleted: true, Version: 2 } &&
                tombstone.Data.GetProperty("deleted").GetBoolean() && !tombstone.Data.GetProperty("enabled").GetBoolean(),
                "A valid disabled deletion tombstone must remain readable after its committed generation advance.");

            var beforeDelete = await reader.ReadVersionsAsync(tenants[0], null, 1);
            RequireCurrentPage(beforeDelete, tombstoneGeneration);
            await using (var delete = fixture.Core())
            {
                var row = await delete.CrmIdentitySnapshots.SingleAsync(value =>
                    value.TenantId == tenants[0] && value.AggregateId == Aggregates[2]);
                delete.CrmIdentitySnapshots.Remove(row);
                await delete.SaveChangesAsync();
            }
            var deletedGeneration = await RequireAdvancedAsync(fixture, tenants[0], tombstoneGeneration);
            await RequireRejectedAsync(reader, tenants[0], beforeDelete.NextCursor!, "C02_SNAPSHOT_BOUNDARY_CHANGED");
            var final = await reader.ReadVersionsAsync(tenants[0], null, 200);
            ProbeAssert.Require(final.Boundary == deletedGeneration.ToString(CultureInfo.InvariantCulture) &&
                final.Items.Select(value => value.AggregateId).SequenceEqual(Aggregates.Take(2)) &&
                final.Items.Single(value => value.AggregateId == Aggregates[1]).IsDeleted &&
                final.NextCursor is null && await reader.ReadSnapshotAsync(tenants[0], Aggregates[2]) is null &&
                await GenerationAsync(fixture, tenants[1]) == otherGeneration,
                "Physical deletion must advance generation, remove only its owned row, retain the tombstone, and leave the other tenant unchanged.");
            return "Actual Reader: empty generation0, exact persisted-generation boundary, all ASCII pages once; cross-tenant/v1/expired/bad/null-position cursors rejected; other-tenant commit and own rollback retained cursor; own update/tombstone/physical delete advanced generation and rejected prior cursors. Actual separate-process continuation and post-write rejection verified: " +
                JsonSerializer.Serialize(new { Continued = continued, RejectedAfterWrite = rejected });
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

    private static CrmIdentitySnapshot Snapshot(CrmIdentityRuntime runtime, Guid tenant, int role,
        int version = 1, bool deleted = false)
    {
        var aggregate = "permission:role:" + role.ToString(CultureInfo.InvariantCulture);
        var data = JsonSerializer.SerializeToNode(new PermissionIdentityData(tenant,
            "role:" + role.ToString(CultureInfo.InvariantCulture), !deleted, version, []), IdentityEventContracts.Json)!.AsObject();
        if (deleted) data["deleted"] = true;
        var envelope = IdentityEventContracts.Create(tenant, aggregate, version, IdentityEventContracts.PermissionChanged,
            data, runtime.Options.Tenants[tenant], "wp3-native-correlation", "wp3-native-command", runtime.Clock);
        ProbeAssert.Require(runtime.Validator.Validate(envelope).IsValid, "The snapshot fixture must pass the actual pinned platform identity event validator.");
        var payload = JsonSerializer.Serialize(data, IdentityEventContracts.Json);
        return new()
        {
            TenantId = tenant,
            AggregateId = aggregate,
            EventType = IdentityEventContracts.PermissionChanged,
            Version = version,
            PayloadJson = payload,
            PayloadSha256 = IdentityEventContracts.Hash(Encoding.UTF8.GetBytes(payload)),
            IsDeleted = deleted,
            UpdatedAtUtc = runtime.Clock.GetUtcNow()
        };
    }

    private static async Task<long> GenerationAsync(RuntimeFixture fixture, Guid tenant)
    {
        await using var core = fixture.Core();
        return await core.CrmIdentityTenantGenerations.Where(value => value.TenantId == tenant)
            .Select(value => (long?)value.Generation).SingleOrDefaultAsync() ?? 0;
    }

    private static void Rewrite(CrmIdentitySnapshot row, CrmIdentitySnapshot replacement)
    {
        row.Version = replacement.Version;
        row.PayloadJson = replacement.PayloadJson;
        row.PayloadSha256 = replacement.PayloadSha256;
        row.IsDeleted = replacement.IsDeleted;
        row.UpdatedAtUtc = replacement.UpdatedAtUtc;
    }

    private static async Task MutateAsync(RuntimeFixture fixture, CrmIdentityRuntime runtime, Guid tenant,
        int role, int version, bool deleted = false)
    {
        await using var core = fixture.Core();
        var aggregate = "permission:role:" + role.ToString(CultureInfo.InvariantCulture);
        var row = await core.CrmIdentitySnapshots.SingleAsync(value => value.TenantId == tenant && value.AggregateId == aggregate);
        Rewrite(row, Snapshot(runtime, tenant, role, version, deleted));
        await core.SaveChangesAsync();
    }

    private static async Task<long> RequireAdvancedAsync(RuntimeFixture fixture, Guid tenant, long before)
    {
        var after = await GenerationAsync(fixture, tenant);
        ProbeAssert.Require(after > before, "The committed same-tenant snapshot mutation must advance its durable generation.");
        return after;
    }

    private static void RequireCurrentPage(IdentityVersionPage page, long generation)
        => ProbeAssert.Require(page.Boundary == generation.ToString(CultureInfo.InvariantCulture) &&
            page.Items.Length == 1 && page.Items[0].AggregateId == Aggregates[0] && page.NextCursor is not null,
            "A fresh first page must bind its continuation cursor to the current persisted generation.");

    private static async Task RequireContinuationAsync(IdentitySnapshotReader reader, Guid tenant, string cursor, long generation)
        => ProbeAssert.Require((await ContinueAsync(reader, tenant, cursor, generation)).Select(value => value.AggregateId)
            .SequenceEqual(Aggregates.Skip(1)), "An unchanged committed generation must preserve the full original continuation.");

    private static async Task<IdentityVersion[]> ContinueAsync(IdentitySnapshotReader reader, Guid tenant, string cursor,
        long generation, CancellationToken cancellationToken = default)
    {
        var result = new List<IdentityVersion>();
        var pages = 0;
        string? next = cursor;
        while (next is not null)
        {
            ProbeAssert.Require(++pages <= 3 && result.Count < 3,
                "Three snapshots must terminate pagination within three ordered pages/items.");
            var page = await reader.ReadVersionsAsync(tenant, next, 1, cancellationToken);
            ProbeAssert.Require(page.Boundary == generation.ToString(CultureInfo.InvariantCulture),
                "Every actual continuation page must retain the same persisted tenant generation.");
            result.AddRange(page.Items);
            next = page.NextCursor;
        }
        return result.ToArray();
    }

    private static async Task RequireRejectedAsync(IdentitySnapshotReader reader, Guid tenant, string cursor, string code,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await reader.ReadVersionsAsync(tenant, cursor, 1, cancellationToken);
            throw new ProbeAssertionException("The actual Reader must reject this fixture cursor using the expected public error code.");
        }
        catch (IdentityReadException exception) when (exception.Message == code) { }
    }

    private static string MintCursor(IDataProtectionProvider protection, string purpose, Guid tenant, long generation,
        string? aggregate, DateTimeOffset expires)
        => protection.CreateProtector(purpose).Protect(JsonSerializer.Serialize(new
        {
            Tenant = tenant, Boundary = generation, LastAggregate = aggregate, ExpiresAtUtc = expires
        }, IdentityEventContracts.Json));

    private static async Task<ChildResult> RunChildAsync(RuntimeFixture fixture, Guid[] tenants, string nonce, string keys,
        string cursor, long generation, bool changed)
    {
        var operation = changed ? "changed" : "continue";
        var statePath = Path.Combine(keys, "child-state-" + operation + ".json");
        var resultPath = Path.Combine(keys, "child-result-" + operation + ".json");
        ProbeAssert.Require(!File.Exists(statePath) && !File.Exists(resultPath), "Native child evidence must use fresh exact operation files.");
        await File.WriteAllTextAsync(statePath, JsonSerializer.Serialize(new ChildState(fixture.Database.Provider,
            tenants, nonce, cursor, generation, changed, Environment.ProcessId)));
        var executable = Environment.ProcessPath ?? throw new ProbeAssertionException("The running native probe executable path is required.");
        var launch = new ProcessStartInfo(executable)
        {
            WorkingDirectory = Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
            launch.ArgumentList.Add(typeof(IdentityCursorGates).Assembly.Location);
        launch.ArgumentList.Add("--identity-cursor-child");
        launch.ArgumentList.Add("--state");
        launch.ArgumentList.Add(statePath);
        // Credentials remain in the inherited test environment and never enter argv/state files.
        using var child = Process.Start(launch) ?? throw new ProbeAssertionException("The native Reader child process must start.");
        var stdout = child.StandardOutput.ReadToEndAsync();
        var stderr = child.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await child.WaitForExitAsync(timeout.Token); }
        finally
        {
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
                using var termination = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await child.WaitForExitAsync(termination.Token);
            }
        }
        var output = await stdout;
        _ = await stderr; // Do not expose raw child errors or private cursor/key data in evidence.
        ProbeAssert.Require(child.ExitCode == 0 && output.Trim() == "IdentityCursorChild.Passed" && File.Exists(resultPath),
            "The actual separately started Reader process must exit0 and produce its sanitized success proof.");
        var result = JsonSerializer.Deserialize<ChildResult>(await File.ReadAllTextAsync(resultPath));
        ProbeAssert.Require(result is not null && result.ProcessId == child.Id && result.ProcessId != Environment.ProcessId &&
            result.CoreSha256 == CoreSha256() && result.RejectedChangedBoundary == changed &&
            result.Items.SequenceEqual(changed ? Array.Empty<string>() : Aggregates.Skip(1)),
            "Child proof must bind the distinct actual PID, same loaded Core bytes, and expected continuation or boundary rejection.");
        return result!;
    }

    /// <summary>Program's early --identity-cursor-child hook returns this exit code before normal suite parsing.</summary>
    public static async Task<int> ChildAsync(string[] args)
    {
        try
        {
            var position = Array.IndexOf(args, "--state");
            ProbeAssert.Require(position >= 0 && position + 1 < args.Length, "A private native child state path is required.");
            var statePath = Path.GetFullPath(args[position + 1]);
            var keys = Path.GetDirectoryName(statePath) ?? throw new ProbeAssertionException("An exact task key directory is required.");
            var name = Path.GetFileName(keys);
            ProbeAssert.Require(name.StartsWith("CP6Wp3Identity_", StringComparison.Ordinal), "Child state must reside in the task nonce directory.");
            var nonce = name["CP6Wp3Identity_".Length..];
            ProbeAssert.Require(Guid.TryParseExact(nonce, "N", out var nonceId) && nonceId != Guid.Empty,
                "An exact random task nonce is required for native child state.");
            RequireExactTemporaryDirectory(Path.GetFullPath(Path.GetTempPath()), keys, nonce);
            var state = JsonSerializer.Deserialize<ChildState>(await File.ReadAllTextAsync(statePath))
                ?? throw new ProbeAssertionException("A complete private child state is required.");
            ProbeAssert.Require(state.Nonce == nonce && state.Tenants.Length == 2 &&
                state.Tenants.All(tenant => tenant != Guid.Empty) && state.Tenants.Distinct().Count() == 2 &&
                (state.Provider is DatabaseProvider.SqlServer or DatabaseProvider.PostgreSql) && state.Generation > 0 &&
                state.ParentProcessId != Environment.ProcessId, "Child state must bind two owned tenants, provider, generation and a different parent PID.");
            var operation = state.Changed ? "changed" : "continue";
            ProbeAssert.Require(Path.GetFileName(statePath) == "child-state-" + operation + ".json",
                "The child may read only its exact operation state file.");
            var fixture = new RuntimeFixture(state.Provider);
            await fixture.VerifyOwnerAsync();
            var contracts = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "contracts", "events", "platform"));
            var runtime = Runtime(contracts, state.Tenants);
            var protection = DataProtectionProvider.Create(new DirectoryInfo(keys),
                options => options.SetApplicationName("CP6.WP3.Identity." + nonce));
            await using var core = fixture.Core();
            var reader = new IdentitySnapshotReader(core, runtime, protection);
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            string[] items;
            if (state.Changed)
            {
                await RequireRejectedAsync(reader, state.Tenants[0], state.Cursor, "C02_SNAPSHOT_BOUNDARY_CHANGED", budget.Token);
                items = [];
            }
            else
            {
                items = (await ContinueAsync(reader, state.Tenants[0], state.Cursor, state.Generation, budget.Token))
                    .Select(value => value.AggregateId).ToArray();
                ProbeAssert.Require(items.SequenceEqual(Aggregates.Skip(1)), "A restarted Reader must continue all original remaining items exactly once.");
            }
            var result = new ChildResult(Environment.ProcessId, CoreSha256(), state.Changed, items);
            await File.WriteAllTextAsync(Path.Combine(keys, "child-result-" + operation + ".json"), JsonSerializer.Serialize(result));
            Console.WriteLine("IdentityCursorChild.Passed");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.GetType().Name);
            return 1;
        }
    }

    private static string CoreSha256() => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(IdentitySnapshotReader).Assembly.Location)));
    private sealed record ChildState(DatabaseProvider Provider, Guid[] Tenants, string Nonce, string Cursor,
        long Generation, bool Changed, int ParentProcessId);
    private sealed record ChildResult(int ProcessId, string CoreSha256, bool RejectedChangedBoundary, string[] Items);

    private static void RequireExactTemporaryDirectory(string temporaryRoot, string keys, string nonce)
    {
        var expected = Path.GetFullPath(Path.Combine(temporaryRoot, "CP6Wp3Identity_" + nonce));
        ProbeAssert.Require(Path.IsPathFullyQualified(keys) && string.Equals(keys, expected, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Path.GetDirectoryName(keys), temporaryRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase),
            "Recursive key cleanup is confined to the verified absolute task nonce directory under the temporary root.");
    }
}
