using System.Net;
using System.Text.Json;
using CP6.Core.Services.Platform;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PGT017_order_number_and_tenant_pause_share_the_commit_fence(bool pauseFirst)
    {
        await using var h = await Seed();
        await using var administrator = await TenantManagementSeed();
        var db = h.Db;
        var connection = db.Database.GetConnectionString()!;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public."T_DocSequence" ("Id", "FuncCode", "LastSeq", "CreateDate")
            VALUES ({Guid.NewGuid()}, 'ORD', 0, CURRENT_TIMESTAMP)
            ON CONFLICT ("FuncCode") DO NOTHING
            """);

        await using var host = await AuthenticationApiProcess.Start(connection,
            IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant));
        var writer = await Login(host, h);
        var manager = await Login(host, administrator);
        using var initial = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(h.Tenant)));
        var active = await NumberHttpData<TenantManagementSnapshotV1>(initial);
        var command = new TenantManagementCommandV1("order-number-" + pauseFirst, active.Revision, "suspend", null);
        var authorityBefore = await ReadPauseAuthority(h, administrator.User);
        var before = await Number();

        await using var barrier = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await barrier.OpenAsync();
        await observer.OpenAsync();
        Task<HttpResponseMessage> writing;
        Task<HttpResponseMessage> pausing;
        FenceWait firstWait;
        FenceWait rootWait;
        await using (var hold = await barrier.BeginTransactionAsync())
        {
            if (pauseFirst)
            {
                await using var lockRow = new NpgsqlCommand(
                    """SELECT 1 FROM public."Sys_Tenants" WHERE "Id"=@tenant FOR UPDATE""", barrier, hold);
                lockRow.Parameters.AddWithValue("tenant", h.Tenant);
                Assert.Equal(1, await lockRow.ExecuteScalarAsync());
                pausing = Pause();
                firstWait = await WaitForFence(observer, barrier.ProcessID, "%UPDATE%Sys_Tenants%", null, pausing);
                writing = Reserve();
                rootWait = await WaitForFence(observer, firstWait.Backend, "%cp6_authentication_v1.acquire()%", null, writing);
            }
            else
            {
                await using var lockRow = new NpgsqlCommand(
                    """SELECT 1 FROM public."T_DocSequence" WHERE "FuncCode"='ORD' FOR UPDATE""", barrier, hold);
                Assert.Equal(1, await lockRow.ExecuteScalarAsync());
                writing = Reserve();
                firstWait = await WaitForFence(observer, barrier.ProcessID, "%INSERT INTO%T_DocSequence%", null, writing);
                pausing = Pause();
                rootWait = await WaitForFence(observer, firstWait.Backend, "%cp6_authentication_v1.acquire()%", null, pausing);
            }
            Assert.Equal(before, await Number());
            Assert.Equal(authorityBefore, await ReadPauseAuthority(h, administrator.User));
            await hold.RollbackAsync();
        }

        using var suspended = await pausing;
        var receipt = await NumberHttpData<TenantManagementReceiptV1>(suspended);
        var authorityAfter = await ReadPauseAuthority(h, administrator.User);
        CheckPause(authorityBefore, authorityAfter, receipt, command);
        using var result = await writing;
        if (pauseFirst)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
            Assert.Equal(before, await Number());
        }
        else
        {
            Assert.True(result.StatusCode == HttpStatusCode.OK, await OperationsFailure(result));
            var issued = await NumberHttpData<JsonElement>(result);
            var sequence = issued.GetProperty("sequence").GetString()!;
            Assert.Matches("^ORD[0-9]{6}[0-9]{4,}$", sequence);
            Assert.Equal(before + 1, int.Parse(sequence[9..], System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(before + 1, await Number());
        }
        var committed = await Number();
        using var stale = await Reserve();
        Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
        Assert.Equal(committed, await Number());
        Assert.Equal(authorityAfter, await ReadPauseAuthority(h, administrator.User));
        output.WriteLine(JsonSerializer.Serialize(new { PauseFirst = pauseFirst, FirstWait = firstWait,
            RootWait = rootWait, Before = before, After = committed, Status = (int)result.StatusCode,
            AuthorityBefore = authorityBefore, AuthorityAfter = authorityAfter }));

        Task<int> Number() => db.DocSequences.AsNoTracking().Where(row => row.FuncCode == "ORD")
            .Select(row => row.LastSeq).SingleAsync();
        Task<HttpResponseMessage> Reserve() => host.Send(OperationsRequest(writer, HttpMethod.Get, "api/orders/next-seq"));
        Task<HttpResponseMessage> Pause() => host.Send(OperationsRequest(manager, HttpMethod.Post,
            ManagementPath(h.Tenant, "/commands"), command));
    }
}
