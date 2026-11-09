using System.Diagnostics;
using System.Net;
using System.Text.Json;
using CP6.Core.Services.Integration.StockOutbox;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private sealed record StockFailureObservation(string Failure, Guid OperationId, int WaiterPid, int RecoveredPid,
        int OtherTenantPid, string? OtherTenantWait);

    private static async Task VerifyStockConnectionFailureAsync(Harness a, Harness b, NpgsqlConnection control,
        AuthenticationApiProcess host, Dictionary<string, string> aSession, string aLogin, string bLogin, StockTenantPairResult pair)
    {
        var bSession = await Login(host, b);
        var lockKey = StockOutboxProtocolV1.RouteLock(StockOutboxProtocolV1.Root(a.Tenant));
        var cancelled = await ExerciseAsync(terminate: false, .12345671m);
        var terminated = await ExerciseAsync(terminate: true, .12345673m);
        await File.WriteAllTextAsync(Path.Combine(Environment.GetEnvironmentVariable("CP6_PERMISSION_PG_ROOT")!,
            "stock-connection-failures.json"), JsonSerializer.Serialize(new { lockKey, cancelled, terminated }));

        async Task<StockFailureObservation> ExerciseAsync(bool terminate, decimal delta)
        {
            var aBefore = await StockTenantEffectsAsync(a);
            var bBefore = await StockTenantEffectsAsync(b);
            var input = new StockApplyCommandV1
            {
                OperationId = Guid.NewGuid(), TxnType = WmsTxnType.ADJ, WarehouseCd = "W01", LocationCd = "L1",
                ProductCd = "P1", LotNo = "LOT", Qty = delta, UnitPrice = 3m
            };
            using var cancellation = new CancellationTokenSource();
            await RouteLockAsync(acquire: true);
            int pid;
            string? bWait;
            try
            {
                using var request = AdjustRequest(input);
                var pending = host.Send(request, cancellation.Token);
                pid = await WaitForRouteWaiterAsync(control, aLogin);
                Assert.Equal(aBefore, await StockTenantEffectsAsync(a));
                var bPending = Original(bSession, pair.OperationId);
                bWait = await ObserveOtherTenantWaitAsync(control, pid, bPending);
                if (terminate)
                {
                    Assert.Equal(true, await CurrentPermissionPostgresFixture.Scalar(control, $"SELECT pg_terminate_backend({pid})"));
                    using var response = await pending;
                    Assert.True(response.StatusCode == HttpStatusCode.ServiceUnavailable, await OperationsFailure(response));
                    Assert.Contains("OPERATIONS_OUTCOME_UNKNOWN", await response.Content.ReadAsStringAsync());
                }
                else
                {
                    await cancellation.CancelAsync();
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
                }
                await WaitForRollbackAsync(control, pid, terminate);
                var bOriginal = await bPending;
                Assert.Equal("COMMITTED", bOriginal.GetProperty("state").GetString());
                Assert.Equal(pair.BResult.GetRawText(), bOriginal.GetProperty("result").GetRawText());
                Assert.Equal(aBefore, await StockTenantEffectsAsync(a));
                Assert.Equal(bBefore, await StockTenantEffectsAsync(b));
            }
            finally
            {
                await cancellation.CancelAsync();
                await RouteLockAsync(acquire: false);
            }

            Assert.Equal("UNKNOWN", (await Original(aSession, input.OperationId)).GetProperty("state").GetString());
            using var replay = AdjustRequest(input);
            using var recovered = await host.Send(replay);
            var result = await StockCreationResponse(recovered);
            var original = await Original(aSession, input.OperationId);
            Assert.Equal("COMMITTED", original.GetProperty("state").GetString());
            Assert.Equal(result.GetRawText(), original.GetProperty("result").GetRawText());
            Assert.Equal("UNKNOWN", (await Original(bSession, input.OperationId)).GetProperty("state").GetString());
            Assert.Equal((aBefore.StockId, aBefore.Quantity + delta, aBefore.Movements + 1, aBefore.Originals + 1, aBefore.Outbox + 1),
                await StockTenantEffectsAsync(a));
            Assert.Equal(bBefore, await StockTenantEffectsAsync(b));
            var pools = await StockPoolSessionsAsync(control, aLogin, bLogin);
            var recoveredPid = Assert.Single(pools, row => row.Login == aLogin).Pid;
            if (terminate) Assert.NotEqual(pid, recoveredPid);
            var bPid = Assert.Single(pools, row => row.Login == bLogin).Pid;
            Assert.Equal(pair.Sessions.Single(row => row.Login == bLogin).Pid, bPid);
            return new(terminate ? "BACKEND_TERMINATED" : "HTTP_CANCELLED", input.OperationId, pid, recoveredPid, bPid, bWait);
        }

        HttpRequestMessage AdjustRequest(StockApplyCommandV1 input) =>
            OperationsRequest(aSession, HttpMethod.Post, "api/wms/stock/commands/v1/apply", input);
        async Task<JsonElement> Original(Dictionary<string, string> session, Guid operation)
        {
            using var request = OperationsRequest(session, HttpMethod.Get, $"api/wms/stock/operations/v1/{operation:D}");
            using var response = await host.Send(request);
            return await StockCreationResponse(response);
        }
        async Task RouteLockAsync(bool acquire)
        {
            var sql = acquire ? "SELECT pg_advisory_lock(@key)" : "SELECT pg_advisory_unlock(@key)";
            await using var command = new NpgsqlCommand(sql, control);
            command.Parameters.AddWithValue("key", lockKey);
            var value = await command.ExecuteScalarAsync();
            if (!acquire) Assert.Equal(true, value);
        }
    }

    private static async Task<int> WaitForRouteWaiterAsync(NpgsqlConnection control, string login)
    {
        await using var command = new NpgsqlCommand("""
            SELECT pid FROM pg_stat_activity
            WHERE datname=current_database() AND application_name='cp6-tenant-storage-v1' AND usename::text=@login
                AND wait_event_type='Lock' AND wait_event='advisory' AND @blocker=ANY(pg_blocking_pids(pid))
                AND query LIKE 'SELECT pg_advisory_xact_lock(%'
            """, control);
        command.Parameters.AddWithValue("login", login);
        command.Parameters.AddWithValue("blocker", control.ProcessID);
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(6))
        {
            if (await command.ExecuteScalarAsync() is int pid) return pid;
            await Task.Delay(40);
        }
        throw new TimeoutException("Ordinary Stock writer did not reach its actual Outbox route lock.");
    }

    private static async Task<string?> ObserveOtherTenantWaitAsync(NpgsqlConnection control, int blocker, Task request)
    {
        await using var command = new NpgsqlCommand("""
            SELECT CASE WHEN query LIKE '%cp6_platform_admin_guard_v1.root%' THEN 'AUTHORITY_ROOT_FOR_SHARE' ELSE 'OWNER_GRAPH_SHARE' END
            FROM pg_stat_activity
            WHERE datname=current_database() AND wait_event_type='Lock' AND @blocker=ANY(pg_blocking_pids(pid))
              AND (query LIKE '%cp6_platform_admin_guard_v1.root%FOR SHARE%' OR query LIKE 'LOCK TABLE% IN SHARE MODE%')
            """, control);
        command.Parameters.AddWithValue("blocker", blocker);
        var clock = Stopwatch.StartNew();
        while (!request.IsCompleted && clock.Elapsed < TimeSpan.FromSeconds(3))
        {
            if (await command.ExecuteScalarAsync() is string wait) return wait;
            await Task.Delay(40);
        }
        return null;
    }

    private static async Task WaitForRollbackAsync(NpgsqlConnection control, int pid, bool terminated)
    {
        await using var command = new NpgsqlCommand("""
            SELECT NOT EXISTS (SELECT FROM pg_stat_activity WHERE pid=@pid AND (@terminated OR xact_start IS NOT NULL))
            """, control);
        command.Parameters.AddWithValue("pid", pid);
        command.Parameters.AddWithValue("terminated", terminated);
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(6))
        {
            if (await command.ExecuteScalarAsync() is true) return;
            await Task.Delay(40);
        }
        throw new TimeoutException("Failed Stock transaction remained active in PostgreSQL.");
    }
}
