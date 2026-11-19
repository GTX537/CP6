using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs.Client;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Reenabled_device_uses_fresh_session_and_current_owner_reads_the_original_move()
    {
        using var key = RSA.Create(2048);
        await using var h = await DeviceLifecycleSeed(key.ExportSubjectPublicKeyInfoPem());
        const string device = "D1";
        const string active = "MOVE-19B-ACTIVE";
        const string complete = "MOVE-19B-ORIGINAL";
        const string taskRoute = "api/v2/wms/tasks/";
        var originalOperation = Guid.NewGuid();
        var originalExecution = Guid.NewGuid();
        h.Db.WmsRoleScopes.Add(new() { RoleId = 72, WarehouseCd = "W2" });
        h.Db.Locations.AddRange(new() { WarehouseCd = "W1", LocationCd = "FROM", AreaCd = "A" },
            new() { WarehouseCd = "W1", LocationCd = "TO", AreaCd = "A" });
        h.Db.MobileTasks.AddRange(NewTask(active, MobileTaskStatus.InProgress), NewTask(complete, MobileTaskStatus.Completed));
        h.Db.TaskCommandReceipts.Add(new() { OperationId = originalOperation, TaskNo = complete,
            CommandName = "complete", ResultJson = "{}" });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        var before = await ReadTasks();

        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var administrator = await Login(host, h);
        var initial = await DeviceNativeLogin(host, h, device);
        using var prior = await host.Send(DeviceBearer(initial.AccessToken, HttpMethod.Get, taskRoute + active, new { }));
        Assert.True(prior.IsSuccessStatusCode, await OperationsFailure(prior));

        await Update(ClientDeviceStatus.Disabled, ClientDeviceMode.Shared, "W1", "A");
        using var suspended = await host.Profile(initial.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, suspended.StatusCode);
        await Update(ClientDeviceStatus.Active, ClientDeviceMode.Personal, "W2", null);
        using var stale = await host.Profile(initial.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
        using var staleRefresh = await host.Send(new HttpRequestMessage(HttpMethod.Post, "api/client-auth/refresh")
        {
            Content = JsonContent.Create(new NativeRefreshRequest
            {
                RefreshToken = initial.RefreshToken, Client = new() { ClientKind = "Windows", DeviceId = device, AppVersion = "1.0.0" }
            })
        });
        Assert.Equal(HttpStatusCode.Unauthorized, staleRefresh.StatusCode);
        Assert.Null(await h.Db.ClientDevices.Where(d => d.DeviceId == device).Select(d => d.FullAuthExpiresAt).SingleAsync());

        var fresh = await DeviceNativeLogin(host, h, device);
        Assert.NotEqual(initial.AccessToken, fresh.AccessToken);
        using var readOriginal = await host.Send(DeviceBearer(fresh.AccessToken, HttpMethod.Get, taskRoute + complete, new { }));
        Assert.True(readOriginal.IsSuccessStatusCode, await OperationsFailure(readOriginal));
        var recovered = (await readOriginal.Content.ReadFromJsonAsync<MobileTaskV2Dto>())!;
        Assert.Equal(originalOperation, recovered.CompletionOperationId);
        Assert.Equal(MobileTaskStatus.Completed, recovered.Status);
        using var heldProfile = await host.Send(DeviceBearer(fresh.AccessToken, HttpMethod.Get, taskRoute + active + "/scan-profile", new { }));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, heldProfile.StatusCode);
        Assert.Contains("MOBILE_OWNER_ADOPTION_UNPROVEN", await heldProfile.Content.ReadAsStringAsync());
        using var heldScan = await host.Send(DeviceBearer(fresh.AccessToken, HttpMethod.Post, taskRoute + active + "/scan",
            new ScanCommand { OperationId = Guid.NewGuid(), DeviceId = device, ExecutionVersion = 7,
                ClientScanNo = "offline-scan-19b", Step = "SourceLocation", RawBarcode = "FROM" }));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, heldScan.StatusCode);
        Assert.Contains("MOBILE_OWNER_ADOPTION_UNPROVEN", await heldScan.Content.ReadAsStringAsync());
        using var heldCompletion = await host.Send(DeviceBearer(fresh.AccessToken, HttpMethod.Post, taskRoute + active + "/complete",
            new CompleteMoveV2Request { OperationId = Guid.NewGuid(), DeviceId = device, ExecutionVersion = 7,
                ScannedQty = 1m, ToLocationCd = "TO" }));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, heldCompletion.StatusCode);
        Assert.Contains("MOBILE_OWNER_ADOPTION_UNPROVEN", await heldCompletion.Content.ReadAsStringAsync());
        Assert.Equal(before, await ReadTasks());
        Assert.Single(await h.Db.TaskCommandReceipts.AsNoTracking().Where(r => r.OperationId == originalOperation).ToArrayAsync());
        Assert.Empty(await h.Db.MobileTaskScanLogs.AsNoTracking().ToArrayAsync());
        Assert.Empty(await h.Db.StockTransactions.AsNoTracking().ToArrayAsync());

        await DeviceEvidence("wp02-19b-wms-owner-boundary.json", new
        {
            PreviousSession = (int)stale.StatusCode, OldRefresh = (int)staleRefresh.StatusCode,
            OriginalReceipt = (int)readOriginal.StatusCode, OriginalCompletion = recovered.CompletionOperationId,
            Profile = (int)heldProfile.StatusCode, Scan = (int)heldScan.StatusCode,
            Complete = (int)heldCompletion.StatusCode, OriginalOwnerUnchanged = true
        });

        MobileTask NewTask(string number, int status) => new()
        {
            MobileTaskNo = number, TaskType = MobileTaskType.Move, ContractVersion = 2, WarehouseCd = "W1", AreaCd = "A",
            ProductCd = "P1", Qty = 1m, FromLocationCd = "FROM", ToLocationCd = "TO", Status = status,
            Creator = "price-owner", AssignedTo = "price-owner", LastDeviceId = device, ExecutionVersion = 7,
            ExecutionId = originalExecution, CompletionOperationId = status == MobileTaskStatus.Completed ? originalOperation : null
        };

        async Task Update(string status, string mode, string warehouse, string? area)
        {
            var current = await h.Db.ClientDevices.AsNoTracking().SingleAsync(d => d.DeviceId == device);
            using var update = await host.Send(OperationsRequest(administrator, HttpMethod.Patch, "api/v2/admin/client-devices/" + device,
                new UpdateClientDeviceRequest { RowVersion = Convert.ToBase64String(current.RowVersion!), Status = status,
                    DeviceMode = mode, WarehouseCd = warehouse, AreaCd = area }));
            Assert.True(update.IsSuccessStatusCode, await OperationsFailure(update));
        }

        async Task<string> ReadTasks()
            => System.Text.Json.JsonSerializer.Serialize(await h.Db.MobileTasks.AsNoTracking().OrderBy(t => t.MobileTaskNo).ToArrayAsync());
    }
}
