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
    public async Task Mandatory_client_upgrade_preserves_the_original_owner_read_and_allows_native_logout()
    {
        using var publicKey = RSA.Create(2048);
        await using var h = await DeviceLifecycleSeed(publicKey.ExportSubjectPublicKeyInfoPem());
        const string taskNo = "MOVE-20-ORIG";
        var operation = Guid.NewGuid();
        h.Db.Locations.AddRange(new() { WarehouseCd = "W1", LocationCd = "FROM", AreaCd = "A" },
            new() { WarehouseCd = "W1", LocationCd = "TO", AreaCd = "A" });
        h.Db.MobileTasks.Add(new()
        {
            MobileTaskNo = taskNo, TaskType = MobileTaskType.Move, ContractVersion = 2, WarehouseCd = "W1", AreaCd = "A",
            ProductCd = "P1", Qty = 1m, FromLocationCd = "FROM", ToLocationCd = "TO", Status = MobileTaskStatus.Completed,
            Creator = "price-owner", AssignedTo = "price-owner", LastDeviceId = "D1", ExecutionVersion = 7,
            ExecutionId = Guid.NewGuid(), CompletionOperationId = operation
        });
        h.Db.TaskCommandReceipts.Add(new()
        {
            OperationId = operation, TaskNo = taskNo, CommandName = "complete", ResultJson = "{\"testFixture\":true}"
        });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,
            new Dictionary<string, string>
            {
                ["Security__NativeClient__Windows__Channel"] = "stable",
                ["Security__NativeClient__Windows__LatestVersion"] = "2.1.0",
                ["Security__NativeClient__Windows__MinimumVersion"] = "2.0.0",
                ["Security__NativeClient__Windows__DownloadUrl"] = "https://packages.fixture.invalid/desktop.msix",
                ["Security__NativeClient__Windows__Sha256"] = new string('a', 64),
                ["Security__NativeClient__Windows__PublisherSha256"] = new string('b', 64),
                ["Security__NativeClient__Android__MinimumVersion"] = "invalid"
            });
        using var bootstrap = await host.Send(new(HttpMethod.Get, "api/client/bootstrap?platform=windows&currentVersion=1.0.0&channel=stable"));
        Assert.Equal(HttpStatusCode.OK, bootstrap.StatusCode);
        var policy = (await bootstrap.Content.ReadFromJsonAsync<ClientBootstrapDto>())!;
        Assert.Equal(("stable", "2.0.0", "2.1.0", true),
            (policy.Channel, policy.MinimumVersion, policy.LatestVersion, policy.UpgradeRequired));
        Assert.Equal(new string('b', 64), policy.PublisherSha256);
        using var wrongChannel = await host.Send(new(HttpMethod.Get, "api/client/bootstrap?platform=windows&currentVersion=1.0.0&channel=pilot"));
        Assert.Equal(HttpStatusCode.BadRequest, wrongChannel.StatusCode);
        Assert.Contains("E-CLIENT-CHANNEL-MISMATCH", await wrongChannel.Content.ReadAsStringAsync());
        using var invalidPolicy = await host.Send(new(HttpMethod.Get, "api/client/bootstrap?platform=android&currentVersion=1.0.0"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, invalidPolicy.StatusCode);
        Assert.Contains("E-CLIENT-VERSION-POLICY", await invalidPolicy.Content.ReadAsStringAsync());

        using var validLogin = await host.Send(new(HttpMethod.Post, "api/client-auth/login")
        {
            Content = JsonContent.Create(Login("2.0.0", requireDevice: false))
        });
        Assert.Equal(HttpStatusCode.OK, validLogin.StatusCode);
        var session = (await validLogin.Content.ReadFromJsonAsync<NativeAuthResult>())!.Session!;
        using var obsoleteLogin = await host.Send(new(HttpMethod.Post, "api/client-auth/login")
        {
            Content = JsonContent.Create(Login("1.0.0", requireDevice: true))
        });
        Assert.Equal(HttpStatusCode.Forbidden, obsoleteLogin.StatusCode);

        var path = "api/v2/wms/tasks/" + taskNo;
        using var first = await host.Send(DeviceBearer(session.AccessToken, HttpMethod.Get, path, new { }));
        Assert.True(first.IsSuccessStatusCode, await OperationsFailure(first));
        var original = await first.Content.ReadAsStringAsync();
        Assert.Equal(operation, (await first.Content.ReadFromJsonAsync<MobileTaskV2Dto>())!.CompletionOperationId);
        using var again = await host.Send(DeviceBearer(session.AccessToken, HttpMethod.Get, path, new { }));
        Assert.Equal(original, await again.Content.ReadAsStringAsync());

        using var logout = await host.Send(DeviceBearer(session.AccessToken, HttpMethod.Post, "api/client-auth/logout",
            new NativeLogoutRequest
            {
                Client = Login("1.0.0", requireDevice: true).Client,
                RefreshToken = session.RefreshToken
            }));
        Assert.True(logout.IsSuccessStatusCode, await OperationsFailure(logout));
        using var afterLogout = await host.Send(DeviceBearer(session.AccessToken, HttpMethod.Get, path, new { }));
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
        Assert.Single(await h.Db.TaskCommandReceipts.AsNoTracking().ToArrayAsync(), r => r.OperationId == operation);
        Assert.Empty(await h.Db.MobileTaskScanLogs.AsNoTracking().ToArrayAsync());
        Assert.Empty(await h.Db.StockTransactions.AsNoTracking().ToArrayAsync());

        await DeviceEvidence("wp02-20-main-upgrade-recovery.json", new
        {
            policy.Platform, policy.Channel, policy.MinimumVersion, policy.LatestVersion,
            InvalidPolicy = (int)invalidPolicy.StatusCode, WrongChannel = (int)wrongChannel.StatusCode,
            ObsoleteNewSession = (int)obsoleteLogin.StatusCode, ExistingOriginal = (int)first.StatusCode,
            Logout = (int)logout.StatusCode, AfterLogout = (int)afterLogout.StatusCode,
            Operation = operation, OriginalReceiptCount = 1, NewScans = 0, NewStockTransactions = 0
        });

        NativeLoginRequest Login(string version, bool requireDevice) => new()
        {
            TenantCode = h.Tenant.ToString("N"), UserName = "price-owner", Password = "Price1!Password",
            Client = new()
            {
                ClientKind = "Windows", DeviceId = "D1", AppVersion = version,
                RequiredGate = requireDevice ? DeviceProofV1.RegisteredDeviceGate : null
            }
        };
    }
}
