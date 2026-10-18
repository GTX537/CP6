using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Core.Persistence.Tenancy;
using Npgsql;

namespace CP6.Tests.Persistence.Tenancy;

[Collection("ServiceTokenPostgres")]
public sealed class ServiceTokenTenantDirectoryControlPostgresTests(ServiceTokenPostgresFixture postgres)
{
    private static readonly Guid A = ServiceRevocationPostgresFixture.A, B = ServiceRevocationPostgresFixture.B;
    private ServiceTokenTenantControlRequestV1 Inspect(Guid? tenant = null) => new(postgres.TokenInstallation, tenant ?? A, "inspect");
    private ServiceTokenTenantControlRequestV1 Set(bool oldEnable, DateTime? oldExpiry, bool enable, DateTime? expiry) =>
        Inspect() with { Operation = "set-eligibility", Expected = new(oldEnable, oldExpiry), Desired = new(enable, expiry) };
    private ServiceTokenTenantControlRequestV1 Initialize(Guid tenant, string code = "a", string name = "Fixture A") =>
        Inspect(tenant) with { Operation = "initialize", TenantCode = code, TenantName = name, Desired = new(true, null) };
    private async Task<ServiceTokenTenantControlResultV1> Run(ServiceTokenTenantControlRequestV1 request,
        Func<TenantControlCheckpointV1, Task>? checkpoint = null, CancellationToken ct = default)
    {
        await using var control = await postgres.Owner();
        return await new ServiceTokenTenantDirectoryControlV1 { Checkpoint = checkpoint }.ExecuteAsync(control, request, postgres.Baseline, ct);
    }
    private async Task Reset()
    {
        await using var control = await postgres.Owner();
        await TenantStoragePostgresFixture.Sql(control, $"""
            DELETE FROM public."Sys_Tenants" WHERE "Id" IN ('{A}','{B}');
            INSERT INTO public."Sys_Tenants"("Id","TenantCode","TenantName","Enable","TwoFactorMode","CreateDate")
            VALUES('{A}','a','Fixture A',true,0,localtimestamp),('{B}','b','Fixture B',true,0,localtimestamp)
            """);
    }
    private async Task Delete(Guid tenant)
    {
        await using var control = await postgres.Owner();
        await TenantStoragePostgresFixture.Sql(control, $"DELETE FROM public.\"Sys_Tenants\" WHERE \"Id\"='{tenant}'");
    }

    [TenantStoragePostgresFact]
    public async Task Initialize_preserves_real_identity_capacity_unique_code_and_missing_registration()
    {
        await Reset();
        try
        {
            Assert.Equal("CURRENT", (await Run(Inspect())).Outcome);
            await Delete(A);
            Assert.Equal("MISSING", (await Run(Inspect())).Outcome);
            Assert.Equal("MISSING", (await Run(Set(true, null, false, null))).Outcome);
            Assert.Equal("CODE_CONFLICT", (await Run(Initialize(A, "b"))).Outcome);
            Assert.Null((await Run(Inspect())).Current);
            var code = string.Concat(Enumerable.Repeat("😀", 25));
            var name = new string('名', 200);
            foreach (var bad in new[] { Initialize(A, code + "x"), Initialize(A, "valid", name + "x"), Initialize(A, " ") })
                Assert.Equal("NOT_APPLIED", (await Assert.ThrowsAsync<ServiceTokenTenantControlException>(() => Run(bad))).Outcome);
            var initialized = await Run(Initialize(A, code, name));
            Assert.Equal("INITIALIZED", initialized.Outcome);
            Assert.Equal(A, initialized.Current!.TenantId); Assert.Equal(code, initialized.Current.TenantCode);
            Assert.Equal(name, initialized.Current.TenantName);
            Assert.Equal("ALREADY_EXISTS", (await Run(Initialize(A, "different", "do not replace"))).Outcome);
            Assert.Equal(initialized.Current, (await Run(Inspect())).Current);
            Assert.Equal("input-rejected", (await Assert.ThrowsAsync<ServiceTokenTenantControlException>(() => Run(Inspect(Guid.NewGuid())))).Message);
            // Frozen bpchar/cp6_ci_as_provider_v1 uniqueness is retained, without app-side normalization.
            await Delete(A);
            Assert.Equal("CODE_CONFLICT", (await Run(Initialize(A, "B"))).Outcome);
            Assert.Equal("CODE_CONFLICT", (await Run(Initialize(A, "b "))).Outcome);
            Assert.Equal("INITIALIZED", (await Run(Initialize(A, "C", "O'Brien"))).Outcome);
            Assert.Equal("C", (await Run(Inspect())).Current!.TenantCode);
            Assert.Equal("b", (await Run(Inspect(B))).Current!.TenantCode);
            await using var control = await postgres.Owner();
            Assert.Equal(postgres.Installation.InstallationId, (await postgres.Install(control)).InstallationId);
        }
        finally { await Reset(); }
    }

    [TenantStoragePostgresFact]
    public async Task Null_safe_CAS_local_deadlines_and_unrelated_columns_are_preserved()
    {
        await Reset();
        try
        {
            await using var control = await postgres.Owner();
            await TenantStoragePostgresFixture.Sql(control, $"""
                UPDATE public."Sys_Tenants" SET "Remark"='keep',"TimeZoneId"='Asia/Shanghai',"TwoFactorMode"=2 WHERE "Id"='{A}'
                """);
            var before = await Unrelated(control);
            var date = new DateTime(2030, 10, 17, 12, 34, 56).AddTicks(1234560);
            Assert.Equal("UPDATED", (await Run(Set(true, null, true, date))).Outcome);
            var conflict = await Run(Set(true, null, false, null));
            Assert.Equal("CAS_CONFLICT", conflict.Outcome); Assert.Equal(date, conflict.Current!.Eligibility.ExpireDate);
            Assert.Equal("CAS_CONFLICT", (await Run(Set(false, date, false, null))).Outcome);
            Assert.Equal("CAS_CONFLICT", (await Run(Set(true, date.AddTicks(10), false, null))).Outcome);
            Assert.Equal("UPDATED", (await Run(Set(true, date, false, null))).Outcome);
            Assert.Equal(before, await Unrelated(control));
            foreach (var invalid in new[] { DateTime.SpecifyKind(date, DateTimeKind.Utc), date.AddTicks(1), DateTime.MinValue })
                Assert.Equal("NOT_APPLIED", (await Assert.ThrowsAsync<ServiceTokenTenantControlException>(() => Run(Set(false, null, true, invalid)))).Outcome);
            var clock = new FixedClock(new DateTimeOffset(2030, 10, 17, 12, 34, 56, TimeSpan.Zero));
            var wall = DateTime.SpecifyKind(clock.GetUtcNow().UtcDateTime.ToLocalTime(), DateTimeKind.Unspecified);
            foreach (var (expiry, active) in new (DateTime?, bool)[] { (null, true), (wall.AddTicks(10), true), (wall, false), (wall.AddTicks(-10), false) })
            {
                var previous = (await Run(Inspect())).Current!.Eligibility;
                Assert.Equal("UPDATED", (await Run(Set(previous.Enable, previous.ExpireDate, true, expiry))).Outcome);
                await using var runtime = await Runtime(); await using var tx = await runtime.BeginTransactionAsync();
                if (active) await ServiceTokenProfileV1.RequireActiveAsync(runtime, tx, clock, default);
                else Assert.Equal(TenantStorageFailure.TenantInactive, (await Assert.ThrowsAsync<TenantStorageException>(() =>
                    ServiceTokenProfileV1.RequireActiveAsync(runtime, tx, clock, default))).Failure);
            }
        }
        finally { await Reset(); }
    }

    [TenantStoragePostgresFact]
    public async Task Concurrent_initialize_serializes_absence_and_global_code_uniqueness()
    {
        await Reset(); await Delete(A); await Delete(B);
        try
        {
            var reached = Signal(); var resume = Signal();
            var first = Run(Initialize(A, "shared"), async point =>
            { if (point == TenantControlCheckpointV1.BeforeCommit) { reached.TrySetResult(); await resume.Task; } });
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await using var otherA = await postgres.Owner(); await using var otherB = await postgres.Owner();
            var duplicate = new ServiceTokenTenantDirectoryControlV1().ExecuteAsync(otherA, Initialize(A, "other"), postgres.Baseline);
            var collision = new ServiceTokenTenantDirectoryControlV1().ExecuteAsync(otherB, Initialize(B, "shared"), postgres.Baseline);
            try
            {
                await Waiting(otherA.ProcessID); await Waiting(otherB.ProcessID);
                Assert.False(duplicate.IsCompleted); Assert.False(collision.IsCompleted);
            }
            finally { resume.TrySetResult(); }
            Assert.Equal("INITIALIZED", (await first).Outcome);
            Assert.Equal("ALREADY_EXISTS", (await duplicate).Outcome);
            Assert.Equal("CODE_CONFLICT", (await collision).Outcome);
            Assert.Null((await Run(Inspect(B))).Current);
        }
        finally { await Reset(); }
    }

    [TenantStoragePostgresFact]
    public async Task Issuance_share_lock_and_control_row_lock_serialize_both_orders_without_blocking_other_tenant()
    {
        await Reset();
        try
        {
            await using var runtime = await Runtime(); await using var readerTx = await runtime.BeginTransactionAsync();
            Assert.True(await Eligibility(runtime, readerTx)); // Actual production helper holds FOR SHARE.
            await using var control = await postgres.Owner();
            var update = new ServiceTokenTenantDirectoryControlV1().ExecuteAsync(control, Set(true, null, false, null), postgres.Baseline);
            try
            {
                await Waiting(control.ProcessID);
                await using var b = await Runtime(b: true); await using var bTx = await b.BeginTransactionAsync();
                Assert.True(await Eligibility(b, bTx));
                Assert.False(update.IsCompleted);
            }
            finally { await readerTx.CommitAsync(); }
            Assert.Equal("UPDATED", (await update).Outcome);
            var reached = Signal(); var resume = Signal();
            var enable = Run(Set(false, null, true, null), async point =>
            { if (point == TenantControlCheckpointV1.BeforeCommit) { reached.TrySetResult(); await resume.Task; } });
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await using var readTx = await runtime.BeginTransactionAsync();
            var reading = Eligibility(runtime, readTx);
            try { await Waiting(runtime.ProcessID); Assert.False(reading.IsCompleted); }
            finally { resume.TrySetResult(); }
            Assert.Equal("UPDATED", (await enable).Outcome); Assert.True(await reading);
        }
        finally { await Reset(); }
    }

    [TenantStoragePostgresFact]
    public async Task Physical_identity_control_owner_profile_seal_and_exact_mapping_fail_closed()
    {
        await Reset();
        var request = Inspect();
        foreach (var target in new[]
        {
            request.Installation with { DatabaseOid = 1 }, request.Installation with { SystemIdentifier = "1" },
            request.Installation with { InstallationId = Guid.NewGuid() }, request.Installation with { Database = "tenant_other" },
            request.Installation with { Tenants = [new(A,"tenant_b"),new(B,"tenant_a")] }
        }) Assert.Equal("NOT_APPLIED", (await Assert.ThrowsAsync<ServiceTokenTenantControlException>(() => Run(request with { Installation = target }))).Outcome);
        foreach (var (role, password) in new[] { ("tenant_a", postgres.Server.PasswordA), ("privileged_fixture", postgres.Server.PasswordAdmin) })
        {
            await using var wrong = await TenantStoragePostgresFixture.OpenRawAsync(role, password, ServiceRevocationPostgresFixture.Database);
            Assert.Equal("NOT_APPLIED", (await Assert.ThrowsAsync<ServiceTokenTenantControlException>(() =>
                new ServiceTokenTenantDirectoryControlV1().ExecuteAsync(wrong, request, postgres.Baseline))).Outcome);
        }
        await using var owner = await postgres.Owner();
        foreach (var column in new[] { "profile", "fingerprint", "baseline_sha256", "canonical", "issuer_name" })
        {
            var original = await TenantStoragePostgresFixture.Scalar(owner, $"SELECT {column}::text FROM cp6_service_token_v1.installation");
            await TenantStoragePostgresFixture.Sql(owner, $"UPDATE cp6_service_token_v1.installation SET {column}='wrong'");
            try { Assert.Equal("NOT_APPLIED", (await Assert.ThrowsAsync<ServiceTokenTenantControlException>(() => Run(Set(true,null,false,null)))).Outcome); }
            finally
            {
                await using var restore = new NpgsqlCommand($"UPDATE cp6_service_token_v1.installation SET {column}=@value", owner);
                restore.Parameters.AddWithValue("value", original!); await restore.ExecuteNonQueryAsync();
            }
        }
        await TenantStoragePostgresFixture.Sql(owner, "UPDATE cp6_tenancy_v1.login_tenant SET tenant_id='44444444-4444-4444-8444-444444444444' WHERE role_name='tenant_a'");
        try { Assert.Equal("installation-or-mapping-rejected", (await Assert.ThrowsAsync<ServiceTokenTenantControlException>(() => Run(request))).Message); }
        finally { await TenantStoragePostgresFixture.Sql(owner, $"UPDATE cp6_tenancy_v1.login_tenant SET tenant_id='{A}' WHERE role_name='tenant_a'"); }
        await TenantStoragePostgresFixture.Sql(owner, "GRANT SELECT ON public.\"Sys_Tenants\" TO tenant_a");
        try { Assert.Equal("installation-or-mapping-rejected", (await Assert.ThrowsAsync<ServiceTokenTenantControlException>(() => Run(request))).Message); }
        finally { await TenantStoragePostgresFixture.Sql(owner, "REVOKE SELECT ON public.\"Sys_Tenants\" FROM tenant_a"); }
        Assert.True((await Run(request)).Current!.Eligibility.Enable);
        // A different saved profile cannot be treated as the current five-table installation.
        await using var other = await postgres.Server.OpenOwnerAsync("tenant_other");
        var otherOid = (uint)(await TenantStoragePostgresFixture.Scalar(other, "SELECT oid FROM pg_catalog.pg_database WHERE datname=current_database()"))!;
        Assert.Equal("NOT_APPLIED", (await Assert.ThrowsAsync<ServiceTokenTenantControlException>(() =>
            new ServiceTokenTenantDirectoryControlV1().ExecuteAsync(other, request with { Installation = request.Installation with
                { Database = "tenant_other", DatabaseOid = otherOid } }, postgres.Baseline))).Outcome);
    }

    [TenantStoragePostgresFact]
    public async Task Cancellation_rollback_and_lost_commit_response_are_not_request_recovery()
    {
        await Reset();
        try
        {
            using var cancelled = new CancellationTokenSource();
            var failure = await Assert.ThrowsAsync<ServiceTokenTenantControlException>(() => Run(Set(true,null,false,null), point =>
            { if (point == TenantControlCheckpointV1.BeforeCommit) cancelled.Cancel(); return Task.CompletedTask; }, cancelled.Token));
            Assert.Equal("NOT_APPLIED", failure.Outcome); Assert.True((await Run(Inspect())).Current!.Eligibility.Enable);
            failure = await Assert.ThrowsAsync<ServiceTokenTenantControlException>(() => Run(Set(true,null,false,null), point =>
                point == TenantControlCheckpointV1.BeforeCommit ? Task.FromException(new IOException("private-error")) : Task.CompletedTask));
            Assert.Equal("NOT_APPLIED", failure.Outcome); Assert.DoesNotContain("private-error", failure.ToString());
            failure = await Assert.ThrowsAsync<ServiceTokenTenantControlException>(() => Run(Set(true,null,false,null), point =>
                point == TenantControlCheckpointV1.AfterCommit ? Task.FromException(new IOException("lost-output")) : Task.CompletedTask));
            Assert.Equal("UNKNOWN", failure.Outcome); Assert.False((await Run(Inspect())).Current!.Eligibility.Enable);
            Assert.Equal("CAS_CONFLICT", (await Run(Set(true,null,false,null))).Outcome);
            Assert.Equal("UPDATED", (await Run(Set(false,null,true,null))).Outcome);
            // ABA explicitly remains possible: the same old eligibility tuple is not an operation identity.
            Assert.Equal("UPDATED", (await Run(Set(true,null,false,null))).Outcome);
            await using var runtime = await Runtime(); await using var tx = await runtime.BeginTransactionAsync();
            Assert.False(await Eligibility(runtime, tx));
            await using var control = await postgres.Owner(); using var waitingCancellation = new CancellationTokenSource();
            var pending = new ServiceTokenTenantDirectoryControlV1().ExecuteAsync(control, Set(false,null,true,null), postgres.Baseline, waitingCancellation.Token);
            await Waiting(control.ProcessID); waitingCancellation.Cancel();
            Assert.Equal("NOT_APPLIED", (await Assert.ThrowsAsync<ServiceTokenTenantControlException>(() => pending)).Outcome);
            await tx.CommitAsync(); Assert.False((await Run(Inspect())).Current!.Eligibility.Enable);
        }
        finally { await Reset(); }
    }

    [TenantStoragePostgresFact]
    public async Task Actual_private_stdin_CLI_rejects_ambiguous_input_and_returns_control_results()
    {
        await Reset();
        try
        {
            var inspect = JsonSerializer.SerializeToNode(new { Request = Inspect(), AdminUsername = "fixture_owner",
                AdminPassword = postgres.Server.ProvisioningAdminPassword, SslMode = "Disable" })!;
            var result = await Cli(inspect.ToJsonString()); Assert.Equal(0, result.Exit); Assert.Equal("", result.Error);
            Assert.Equal("CURRENT", JsonNode.Parse(result.Output)!["Outcome"]!.GetValue<string>());
            var update = inspect.DeepClone(); update["Request"] = JsonSerializer.SerializeToNode(Set(true,null,false,null));
            result = await Cli(update.ToJsonString()); Assert.Equal(0, result.Exit); Assert.Contains("UPDATED", result.Output);
            result = await Cli(update.ToJsonString()); Assert.Equal(3, result.Exit); Assert.Contains("CAS_CONFLICT", result.Output);
            var missing = update.DeepClone(); missing["Request"]!["Expected"]!.AsObject().Remove("ExpireDate");
            var badBool = update.DeepClone(); badBool["Request"]!["Desired"]!.AsObject().Remove("Enable");
            var fingerprint = inspect.DeepClone(); fingerprint["Request"]!["Fingerprint"] = "do-not-trust";
            foreach (var invalid in new[] { missing.ToJsonString(), badBool.ToJsonString(), fingerprint.ToJsonString(),
                inspect.ToJsonString().Insert(1, "\"AdminUsername\":\"duplicate\","), new string('x',16385) })
            {
                result = await Cli(invalid); Assert.Equal(1, result.Exit); Assert.Equal("", result.Output);
                Assert.Contains("NOT_APPLIED", result.Error); Assert.DoesNotContain("Retry", result.Error);
                Assert.DoesNotContain(postgres.Server.ProvisioningAdminPassword, result.Error);
            }
            await Delete(A);
            var initialize = inspect.DeepClone(); initialize["Request"] = JsonSerializer.SerializeToNode(Initialize(A));
            result = await Cli(initialize.ToJsonString()); Assert.Equal(0, result.Exit); Assert.Contains("INITIALIZED", result.Output);
            result = await Cli(initialize.ToJsonString()); Assert.Equal(3, result.Exit); Assert.Contains("ALREADY_EXISTS", result.Output);
        }
        finally { await Reset(); }
    }

    [TenantStoragePostgresFact]
    public Task Actual_stdin_Expected_deadline_preserves_original_precision() => RawDeadline("set-eligibility", "Expected");

    [TenantStoragePostgresFact]
    public Task Actual_stdin_Desired_deadline_preserves_original_precision() => RawDeadline("set-eligibility", "Desired");

    [TenantStoragePostgresFact]
    public Task Actual_stdin_initialize_deadline_preserves_original_precision() => RawDeadline("initialize", "Desired");

    private async Task RawDeadline(string operation, string member)
    {
        await Reset();
        try
        {
            const string local = "2030-10-17T12:34:56";
            var expiry = new DateTime(2030, 10, 17, 12, 34, 56).AddTicks(1234560);
            JsonNode Input(ServiceTokenTenantControlRequestV1 request) => JsonSerializer.SerializeToNode(new
            {
                Request = request, AdminUsername = "fixture_owner",
                AdminPassword = postgres.Server.ProvisioningAdminPassword, SslMode = "Disable"
            })!;
            // Prove the same real CLI credentials/physical installation can write before testing rejection.
            var seeded = await Cli(Input(Set(true, null, true, expiry)).ToJsonString());
            Assert.Equal(0, seeded.Exit); Assert.Contains("UPDATED", seeded.Output);
            if (operation == "initialize") await Delete(A);
            var input = Input(operation == "initialize" ? Initialize(A) : Set(true, expiry, false, expiry));
            await using var owner = await postgres.Owner();
            Task<object?> Rows() => TenantStoragePostgresFixture.Scalar(owner,
                "SELECT jsonb_agg(to_jsonb(t) ORDER BY t.\"Id\")::text FROM public.\"Sys_Tenants\" t");
            var before = await Rows();
            foreach (var fraction in new[] { ".12345601", ".1234560000000001", ".1234561", ".1234560\\u0031" })
            {
                var invalid = input.DeepClone(); invalid["Request"]![member]!["ExpireDate"] = local + ".12345601";
                // Include a JSON-escaped final digit: validation must inspect the decoded original value.
                var raw = invalid.ToJsonString().Replace(local + ".12345601", local + fraction, StringComparison.Ordinal);
                var result = await Cli(raw);
                var after = await Rows();
                Assert.True(Equals(before, after), $"{operation}/{member} {fraction}: exit={result.Exit}; stdout={result.Output}; directory changed");
                Assert.Equal(1, result.Exit); Assert.Equal("", result.Output);
                var rejected = JsonNode.Parse(result.Error)!;
                Assert.Equal("NOT_APPLIED", rejected["Outcome"]!.GetValue<string>());
                Assert.Equal("input-rejected", rejected["Code"]!.GetValue<string>());
                Assert.DoesNotContain(postgres.Server.ProvisioningAdminPassword, result.Error);
            }
            // Six digits and equivalent 7/16-digit trailing-zero forms must all remain valid.
            foreach (var fraction in new[] { ".123456", ".1234560", ".1234560000000000" })
            {
                if (operation == "initialize") await Delete(A);
                else
                {
                    var current = (await Run(Inspect())).Current!.Eligibility;
                    Assert.Equal("UPDATED", (await Run(Set(current.Enable, current.ExpireDate, true, expiry))).Outcome);
                }
                var valid = input.DeepClone(); valid["Request"]![member]!["ExpireDate"] = local + fraction;
                var result = await Cli(valid.ToJsonString());
                Assert.Equal(0, result.Exit); Assert.Equal("", result.Error);
                Assert.Equal(operation == "initialize" ? "INITIALIZED" : "UPDATED", JsonNode.Parse(result.Output)!["Outcome"]!.GetValue<string>());
                var currentRow = (await Run(Inspect())).Current!;
                Assert.Equal(expiry, currentRow.Eligibility.ExpireDate);
                Assert.Equal(operation == "initialize", currentRow.Eligibility.Enable);
            }
            // Explicit null remains unlimited, including a null Expected CAS value.
            if (operation == "initialize") await Delete(A);
            else
            {
                var current = (await Run(Inspect())).Current!.Eligibility;
                Assert.Equal("UPDATED", (await Run(Set(current.Enable, current.ExpireDate, true, null))).Outcome);
            }
            var unlimited = Input(operation == "initialize" ? Initialize(A) : Set(true, null, false, null));
            Assert.Equal(0, (await Cli(unlimited.ToJsonString())).Exit);
            Assert.Null((await Run(Inspect())).Current!.Eligibility.ExpireDate);
        }
        finally { await Reset(); }
    }

    private Task<NpgsqlConnection> Runtime(bool b = false) => TenantStoragePostgresFixture.OpenRawAsync(b ? "tenant_b" : "tenant_a",
        b ? postgres.Server.PasswordB : postgres.Server.PasswordA, ServiceRevocationPostgresFixture.Database);
    private static async Task<bool> Eligibility(NpgsqlConnection connection, NpgsqlTransaction tx)
    {
        await using var command = new NpgsqlCommand("SELECT enabled FROM cp6_service_token_v1.current_tenant_eligibility()", connection, tx);
        return (bool)(await command.ExecuteScalarAsync())!;
    }
    private async Task Waiting(int pid)
    {
        await using var observer = await postgres.Owner();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (await TenantStoragePostgresFixture.Scalar(observer, $"SELECT wait_event_type='Lock' FROM pg_catalog.pg_stat_activity WHERE pid={pid}") is not true)
            await Task.Delay(25, timeout.Token);
    }
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static Task<object?> Unrelated(NpgsqlConnection connection) => TenantStoragePostgresFixture.Scalar(connection,
        $"SELECT (to_jsonb(t)-'Enable'-'ExpireDate'-'ModifyDate'-'Modifier')::text FROM public.\"Sys_Tenants\" t WHERE \"Id\"='{A}'");
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
    private static async Task<(int Exit, string Output, string Error)> Cli(string input)
    {
        var executable = Environment.GetEnvironmentVariable("CP6_TENANT_CONTROL_CLI")
            ?? throw new InvalidOperationException("Build CLI and set CP6_TENANT_CONTROL_CLI to its absolute apphost path for this selected run.");
        var info = new ProcessStartInfo(executable)
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add(ServiceTokenTenantDirectoryControlV1.Contract); info.ArgumentList.Add("--stdin");
        using var process = Process.Start(info)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token); var error = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.StandardInput.WriteAsync(input); process.StandardInput.Close();
        await process.WaitForExitAsync(timeout.Token);
        return (process.ExitCode, await output, await error);
    }
}
