using System.Net;
using System.Net.Http.Json;
using CP6.Core.Services.Platform;
using CP6.Space.Contracts;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PGT017_planning_reservation_and_pause_use_the_same_commit_gate(bool reservationFirst)
    {
        await using var writer = await Seed();
        await using var administrator = await TenantManagementSeed();
        await GrantSpaceDesignActions(writer, "planning:scenario:create");
        var connection = writer.Db.Database.GetConnectionString()!;
        var site = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        await using var space = SpaceDesignContext(connection, new SpaceDesignExecution(writer.Tenant, writer.User));
        var (modelId, published) = await SeedW647Published(writer, space, site);
        var settings = SpaceDesignSettings(writer.Tenant, site);
        if (Environment.GetEnvironmentVariable("CP6_PLANNING_RESERVATION_HTTP_PORT") is { } port)
            settings["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        await using var host = await AuthenticationApiProcess.Start(connection, settings);
        var actor = await Login(host, writer);
        var manager = await Login(host, administrator);
        var authorityBefore = await ReadPauseAuthority(writer, administrator.User);
        var command = new CreateSpacePlanningScenarioBranchRequest(published, "Simulated planning reservation");
        var path = $"/api/space/planning/v1/sites/{site:D}/scenario-branches/{branchId:D}";
        var order = reservationFirst ? "reservation-first" : "pause-first";

        await using var watcher = await OpenHistoricalDatabase(connection);
        await using var businessLock = await OpenHistoricalDatabase(connection);
        await using var platformLock = await OpenHistoricalDatabase(connection);
        var before = await ReadW647CloneWrites(watcher, writer.Tenant);

        Task<HttpResponseMessage> reserve;
        Task<HttpResponseMessage> pause;
        FenceWait reservationWait;
        FenceWait pauseWait;
        W647CloneRow[] waiting;
        W647CloneRow[]? beforePauseCommit = null;
        await using (var heldBranch = await HoldUnderlayTable(businessLock, "Space_PlanningScenarioBranch"))
        await using (var heldTenant = await HoldUnderlayTenant(platformLock, writer.Tenant))
        {
            if (reservationFirst)
            {
                reserve = Reserve(actor);
                reservationWait = await WaitForFence(watcher, businessLock.ProcessID, "%Space_PlanningScenarioBranch%", null, reserve);
                pause = Pause();
                pauseWait = await SerialNativeWait(watcher, reservationWait.Backend, platformLock.ProcessID, "planning-reservation-before-pause", pause);
                waiting = await ReadW647CloneWrites(watcher, writer.Tenant);
                await heldBranch.RollbackAsync();
                await reserve.WaitAsync(TimeSpan.FromSeconds(12));
                beforePauseCommit = await ReadW647CloneWrites(watcher, writer.Tenant);
                Assert.True((await ReadPauseAuthority(writer, administrator.User)).Enabled);
                await heldTenant.RollbackAsync();
            }
            else
            {
                pause = Pause();
                pauseWait = await WaitForFence(watcher, platformLock.ProcessID, "%UPDATE%Sys_Tenants%", null, pause);
                reserve = Reserve(actor);
                reservationWait = await SerialNativeWait(watcher, pauseWait.Backend, businessLock.ProcessID, "planning-reservation-after-pause", reserve);
                waiting = await ReadW647CloneWrites(watcher, writer.Tenant);
                await heldTenant.RollbackAsync();
                await pause.WaitAsync(TimeSpan.FromSeconds(12));
                beforePauseCommit = await ReadW647CloneWrites(watcher, writer.Tenant);
                await heldBranch.RollbackAsync();
            }
        }

        using var result = await reserve;
        using var pausedResult = await pause;
        var pauseReceipt = await NumberHttpData<TenantManagementReceiptV1>(pausedResult);
        var after = await ReadW647CloneWrites(watcher, writer.Tenant);
        var authorityAfter = await ReadPauseAuthority(writer, administrator.User);
        CreateSpacePlanningScenarioBranchResponse? created = null;
        if (result.StatusCode == HttpStatusCode.OK)
            created = await result.Content.ReadFromJsonAsync<CreateSpacePlanningScenarioBranchResponse>();
        await DeviceEvidence($"planning-reservation-{order}.json", new
        {
            ReservationWait = reservationWait, PauseWait = pauseWait, Before = before, Waiting = waiting,
            BeforeRelease = beforePauseCommit, After = after, CreateStatus = (int)result.StatusCode, Created = created,
            AuthorityBefore = authorityBefore, AuthorityAfter = authorityAfter, PauseStatus = (int)pausedResult.StatusCode,
        });

        Assert.True(authorityBefore.Enabled);
        Assert.False(authorityAfter.Enabled);
        Assert.False(pauseReceipt.Snapshot.Enable);
        Assert.NotEqual(authorityBefore.PolicyRevision, authorityAfter.PolicyRevision);
        Assert.Equal(before, waiting);
        if (reservationFirst)
        {
            Assert.Equal(reservationWait.Backend, pauseWait.Blocker);
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            Assert.Equal(beforePauseCommit, after);
            CheckReceipt(Assert.IsType<CreateSpacePlanningScenarioBranchResponse>(created), after);
        }
        else
        {
            Assert.Equal(pauseWait.Backend, reservationWait.Blocker);
            Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
            Assert.Equal(before, after);
        }

        await ResumeSourceUploadTenant(host, manager, writer.Tenant, pausedResult);
        actor = await Login(host, writer);
        using var resumed = await Reserve(actor);
        Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);
        var recovered = Assert.IsType<CreateSpacePlanningScenarioBranchResponse>(
            await resumed.Content.ReadFromJsonAsync<CreateSpacePlanningScenarioBranchResponse>());
        var recoveredRows = await ReadW647CloneWrites(watcher, writer.Tenant);
        CheckReceipt(recovered, recoveredRows);
        if (created is not null)
        {
            Assert.Equal(created.Branch.CloneJobId, recovered.Branch.CloneJobId);
            Assert.Equal(created.Branch.ScenarioVersionId, recovered.Branch.ScenarioVersionId);
            Assert.Equal(after, recoveredRows);
        }

        Task<HttpResponseMessage> Reserve(Dictionary<string, string> current) =>
            host.Send(OperationsRequest(current, HttpMethod.Put, path, command));
        Task<HttpResponseMessage> Pause() =>
            host.Send(OperationsRequest(manager, HttpMethod.Post, ManagementPath(writer.Tenant, "/commands"),
                new TenantManagementCommandV1("planning-reservation-pause", authorityBefore.Revision, "suspend", null)));

        void CheckReceipt(CreateSpacePlanningScenarioBranchResponse receipt, W647CloneRow[] rows)
        {
            Assert.Equal(branchId, receipt.Branch.BranchId);
            Assert.Equal(modelId, receipt.Branch.ModelId);
            Assert.Equal(published, receipt.Branch.BasePublishedVersionId);
            Assert.True(receipt.Branch.ProductionIsolated);
            Assert.Contains(rows, row => row.Kind == "version" && row.Id == receipt.Branch.ScenarioVersionId);
            Assert.Contains(rows, row => row.Kind == "branch" && row.Id == branchId);
            Assert.Contains(rows, row => row.Kind == "job" && row.Id == receipt.Branch.CloneJobId);
            Assert.Equal(3, rows.Except(before).Count());
        }
    }
}
