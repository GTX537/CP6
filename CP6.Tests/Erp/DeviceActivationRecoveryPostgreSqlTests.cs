using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Original_device_reads_committed_activation_after_response_loss_without_writes()
    {
        using var key = RSA.Create(2048);
        using var unrelatedKey = RSA.Create(2048);
        await using var h = await DeviceLifecycleSeed(key.ExportSubjectPublicKeyInfoPem());
        var tenantCode = h.Tenant.ToString("N");
        await using var writer = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        await using var reader = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var manager = await Login(writer, h);

        using var issued = await writer.Send(OperationsRequest(manager, HttpMethod.Post, "api/v2/admin/client-devices", TicketRequest()));
        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
        using var ticket = JsonDocument.Parse(await issued.Content.ReadAsStringAsync());
        Assert.True(ticket.RootElement.TryGetProperty("activationOperationId", out var operationValue));
        var operation = operationValue.GetGuid();
        var raw = ticket.RootElement.GetProperty("activationToken").GetString()!;
        const string deviceId = "original-activation-device";
        using (var result = await writer.Send(Post("api/client/devices/activate", new
        {
            TenantCode = tenantCode, ActivationOperationId = operation, ActivationToken = raw, DeviceId = deviceId,
            PublicKey = key.ExportSubjectPublicKeyInfoPem(), Platform = "Windows", AppVersion = "1.0.0"
        })))
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        // 客户端未读取首次响应，从独立宿主仅查询原结果。
        var initial = await DeviceWriteSet(h, true);
        var recorded = await h.Db.ClientDevices.AsNoTracking().SingleAsync(d => d.DeviceId == deviceId);
        using var recovered = await Read(operation, tenantCode, deviceId, key, DateTimeOffset.UtcNow);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        var text = await recovered.Content.ReadAsStringAsync();
        Assert.DoesNotContain(raw, text);
        using var response = JsonDocument.Parse(text);
        Assert.Equal("registered", response.RootElement.GetProperty("state").GetString());
        var receipt = response.RootElement.GetProperty("registration").Deserialize<ActivatedClientDeviceDto>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(deviceId, receipt.DeviceId);
        Assert.Equal(tenantCode, receipt.TenantCode);
        Assert.Equal(recorded.WarehouseCd, receipt.WarehouseCd);
        Assert.Equal(recorded.ActivatedAt, receipt.ActivatedAt.UtcDateTime);
        Assert.Equal(6, response.RootElement.GetProperty("registration").EnumerateObject().Count());
        Assert.False(recovered.Headers.Contains("Set-Cookie"));

        await Unavailable(operation, tenantCode, deviceId, unrelatedKey, DateTimeOffset.UtcNow);
        await Unavailable(operation, Guid.NewGuid().ToString("N"), deviceId, key, DateTimeOffset.UtcNow);
        await Unavailable(Guid.NewGuid(), tenantCode, deviceId, key, DateTimeOffset.UtcNow);
        await Unavailable(operation, tenantCode, deviceId, key, DateTimeOffset.UtcNow.AddMinutes(-6));
        Assert.Equal(initial, await DeviceWriteSet(h, true));
        Assert.Equal(1, await h.Db.DeviceActivations.AsNoTracking().CountAsync(row => row.Id == operation && row.ConsumedByDeviceId == deviceId));

        using var suspended = await writer.Send(OperationsRequest(manager, HttpMethod.Patch, "api/v2/admin/client-devices/" + deviceId,
            new UpdateClientDeviceRequest
            {
                RowVersion = Convert.ToBase64String(recorded.RowVersion!), Status = ClientDeviceStatus.Disabled,
                WarehouseCd = recorded.WarehouseCd, AreaCd = recorded.AreaCd
            }));
        Assert.Equal(HttpStatusCode.OK, suspended.StatusCode);
        var disabled = await DeviceWriteSet(h, true);
        await Unavailable(operation, tenantCode, deviceId, key, DateTimeOffset.UtcNow);
        Assert.Equal(disabled, await DeviceWriteSet(h, true));

        async Task Unavailable(Guid id, string tenant, string device, RSA signer, DateTimeOffset stamp)
        {
            using var denied = await Read(id, tenant, device, signer, stamp);
            Assert.Equal(HttpStatusCode.OK, denied.StatusCode);
            using var body = JsonDocument.Parse(await denied.Content.ReadAsStringAsync());
            Assert.Equal("unavailable", body.RootElement.GetProperty("state").GetString());
            Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("registration").ValueKind);
        }

        Task<HttpResponseMessage> Read(Guid id, string tenant, string device, RSA signer, DateTimeOffset stamp)
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(new object[] { "cp6-activation-result-v1", id, tenant, device, stamp.ToUnixTimeSeconds() });
            var signature = Convert.ToBase64String(signer.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            return reader.Send(Post("api/client/devices/activate/status", new
            {
                ActivationOperationId = id, TenantCode = tenant, DeviceId = device, Timestamp = stamp, Signature = signature
            }));
        }

        static HttpRequestMessage Post(string path, object body)
            => new(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
    }

    [CurrentPermissionPostgresFact]
    public async Task Native_optional_login_is_preserved_and_required_device_binding_is_explicit()
    {
        using var key = RSA.Create(2048);
        await using var h = await DeviceLifecycleSeed(key.ExportSubjectPublicKeyInfoPem());
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);

        using var legacy = await LoginAs("not-registered");
        Assert.Equal(HttpStatusCode.OK, legacy.StatusCode);
        using var legacyBody = JsonDocument.Parse(await legacy.Content.ReadAsStringAsync());
        Assert.Equal("authenticated", legacyBody.RootElement.GetProperty("state").GetString());
        var prior = await DeviceWriteSet(h, true);
        using var unknown = await LoginAs("D1", "unrecognized-capability");
        Assert.Equal(HttpStatusCode.Forbidden, unknown.StatusCode);
        using var withoutProof = await LoginAs("D1", "registered-device-v1");
        Assert.Equal(HttpStatusCode.Forbidden, withoutProof.StatusCode);
        Assert.Equal(prior, await DeviceWriteSet(h, true));

        Task<HttpResponseMessage> LoginAs(string device, string? gate = null)
            => host.Send(new HttpRequestMessage(HttpMethod.Post, "api/client-auth/login")
            {
                Content = JsonContent.Create(new
                {
                    TenantCode = h.Tenant.ToString("N"), UserName = "price-owner", Password = "Price1!Password",
                    Client = new { ClientKind = "Windows", DeviceId = device, AppVersion = "1.0.0", RequiredGate = gate }
                }),
            });
    }

    [CurrentPermissionPostgresFact]
    public async Task Required_device_proof_uses_current_tenant_registration_and_version()
    {
        using var key = ECDsa.Create();
        using var otherKey = ECDsa.Create();
        await using var h = await DeviceLifecycleSeed(key.ExportSubjectPublicKeyInfoPem());
        await using var other = await Seed();
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        const string deviceId = "required-device";
        var tenant = h.Tenant.ToString("N");
        var foreign = other.Tenant.ToString("N");
        var manager = await Login(host, h);
        using var issued = await host.Send(OperationsRequest(manager, HttpMethod.Post, "api/v2/admin/client-devices", TicketRequest()));
        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
        var ticket = (await issued.Content.ReadFromJsonAsync<DeviceActivationTicket>())!;
        using var activation = await host.Send(new HttpRequestMessage(HttpMethod.Post, "api/client/devices/activate")
        {
            Content = JsonContent.Create(new ActivateClientDeviceRequest
            {
                ActivationOperationId = ticket.ActivationOperationId, ActivationToken = ticket.ActivationToken,
                TenantCode = tenant, DeviceId = deviceId, PublicKey = key.ExportSubjectPublicKeyInfoPem(),
                Platform = "Windows", AppVersion = "1.0.0"
            })
        });
        Assert.Equal(HttpStatusCode.OK, activation.StatusCode);

        using var declared = await host.Send(new HttpRequestMessage(HttpMethod.Get, "api/client/bootstrap?platform=windows&currentVersion=1.0.0"));
        var compatibility = (await declared.Content.ReadFromJsonAsync<CP6.Entity.DTOs.Client.ClientBootstrapDto>())!;
        Assert.Contains("registered-device-v1", compatibility.RequiredGates);
        using var allowed = await LoginAs(tenant, MakeProof(key));
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        using var allowable = JsonDocument.Parse(await allowed.Content.ReadAsStringAsync());
        Assert.Equal("authenticated", allowable.RootElement.GetProperty("state").GetString());
        Assert.True(allowable.RootElement.GetProperty("registeredDeviceVerified").GetBoolean());

        using var plainOther = await LoginAs(foreign, null);
        Assert.Equal(HttpStatusCode.OK, plainOther.StatusCode);
        Assert.Equal(0, await other.Db.ClientDevices.CountAsync());
        var before = await DeviceWriteSet(h, true);
        var foreignBefore = await DeviceWriteSet(other, true);
        using var outsideTenant = await LoginAs(foreign, MakeProof(key));
        using var invalidProof = await LoginAs(tenant, MakeProof(otherKey));
        using var obsoleteVersion = await LoginAs(tenant, MakeProof(key, "0.1.0"), "0.1.0");
        Assert.Equal(HttpStatusCode.Forbidden, outsideTenant.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, invalidProof.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, obsoleteVersion.StatusCode);
        Assert.Equal(before, await DeviceWriteSet(h, true));
        Assert.Equal(foreignBefore, await DeviceWriteSet(other, true));

        var registration = await h.Db.ClientDevices.AsNoTracking().SingleAsync(d => d.DeviceId == deviceId);
        using var disable = await host.Send(OperationsRequest(manager, HttpMethod.Patch, "api/v2/admin/client-devices/" + deviceId,
            new UpdateClientDeviceRequest { RowVersion = Convert.ToBase64String(registration.RowVersion!),
                Status = ClientDeviceStatus.Disabled, WarehouseCd = registration.WarehouseCd, AreaCd = registration.AreaCd }));
        Assert.Equal(HttpStatusCode.OK, disable.StatusCode);
        using var noLongerCurrent = await LoginAs(tenant, MakeProof(key));
        Assert.Equal(HttpStatusCode.Forbidden, noLongerCurrent.StatusCode);

        object MakeProof(ECDsa signer, string version = "1.0.0")
        {
            var at = DateTimeOffset.UtcNow;
            var payload = JsonSerializer.SerializeToUtf8Bytes(new object[]
                { "cp6-auth-device-binding-v1", ticket.ActivationOperationId, deviceId, "Windows", version, at.ToUnixTimeSeconds() });
            return new { ticket.ActivationOperationId, Timestamp = at,
                Signature = Convert.ToBase64String(signer.SignData(payload, HashAlgorithmName.SHA256)) };
        }

        Task<HttpResponseMessage> LoginAs(string tenantCode, object? proof, string version = "1.0.0")
            => host.Send(new HttpRequestMessage(HttpMethod.Post, "api/client-auth/login")
            {
                Content = JsonContent.Create(new
                {
                    TenantCode = tenantCode, UserName = "price-owner", Password = "Price1!Password",
                    Client = new { ClientKind = "Windows", DeviceId = deviceId, AppVersion = version,
                        RequiredGate = proof == null ? null : "registered-device-v1", DeviceBinding = proof }
                }),
            });
    }
}
