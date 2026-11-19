using System.Security.Cryptography;
using System.Text.Json;
using CP6.Client.Api;
using CP6.Entity.DTOs.Client;

namespace CP6.Client.Core;

public sealed class NativeDeviceActivationService(IHttpClientFactory clients, ClientOptions options, IDeviceRequestSigner signer)
{
    private readonly HttpClient _transport = clients.CreateClient(ClientServiceCollectionExtensions.RawClient);

    public async Task<ActivatedClientDevice> ActivateAsync(Uri source, string tenant, string token, Guid? operation,
        CancellationToken ct = default)
    {
        var context = options.Context;
        var api = new Cp6ApiClient(_transport, new ClientRequestScope(source, Guid.Empty, Guid.Empty, context.DeviceId));
        var publicKey = await signer.GetOrCreatePublicKeyAsync(ct);
        string? marker = null;
        if (operation is { } id)
        {
            Directory.CreateDirectory(options.ActivationStateDirectory);
            marker = StatePath("attempt", source, context.DeviceId, tenant, id);
            try
            {
                // 在请求前持久化原操作键。文件不包含票据、证明或个人资料。
                using var started = new FileStream(marker, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            }
            catch (IOException) when (File.Exists(marker))
            {
                var original = await ReadOriginalAsync(api, id, tenant, context.DeviceId, ct);
                await RememberAsync(source, context.DeviceId, id, ct);
                return original;
            }
        }

        try
        {
            var activated = await api.ActivateDeviceAsync(new ActivateClientDeviceRequest
            {
                ActivationOperationId = operation,
                TenantCode = tenant,
                ActivationToken = token,
                DeviceId = context.DeviceId,
                PublicKey = publicKey,
                Platform = context.ClientKind,
                AppVersion = context.AppVersion,
                PlatformVersion = context.PlatformVersion,
            }, ct);
            if (operation is { } completedOperation) await RememberAsync(source, context.DeviceId, completedOperation, ct);
            return activated;
        }
        catch (Exception error) when (operation != null && IsUnknown(error, ct))
        {
            var original = await ReadOriginalAsync(api, operation.Value, tenant, context.DeviceId, ct);
            await RememberAsync(source, context.DeviceId, operation.Value, ct);
            return original;
        }
        catch (ApiException)
        {
            if (marker != null) File.Delete(marker);
            throw;
        }
    }

    private async Task<ActivatedClientDevice> ReadOriginalAsync(Cp6ApiClient api, Guid operation, string tenant,
        string device, CancellationToken ct)
    {
        var timestamp = DateTimeOffset.UtcNow;
        var status = await api.GetActivationStatusAsync(new RecoverDeviceActivationRequest
        {
            ActivationOperationId = operation,
            TenantCode = tenant,
            DeviceId = device,
            Timestamp = timestamp,
            Signature = await signer.SignAsync(DeviceProofV1.ActivationResult(operation, tenant, device, timestamp), ct),
        }, ct);
        if (status.State == "registered" && status.Registration is { } registration && registration.DeviceId == device
            && string.Equals(registration.TenantCode, tenant, StringComparison.OrdinalIgnoreCase))
            return registration;
        throw new InvalidOperationException("原设备激活结果暂不可用，请保留原操作并在连接恢复后查询。");
    }

    public async Task<ClientContext> AuthenticationContextAsync(CancellationToken ct = default)
    {
        var source = options.ApiBaseAddress;
        var context = options.Context;
        var path = StatePath("binding", source, context.DeviceId, "", Guid.Empty);
        if (!File.Exists(path)) return context;
        var raw = await File.ReadAllTextAsync(path, ct);
        if (!Guid.TryParse(raw, out var operation) || operation == Guid.Empty)
            throw new InvalidOperationException("设备登记状态无法读取，请重新核对原激活结果。");
        var timestamp = DateTimeOffset.UtcNow;
        var signature = await signer.SignAsync(
            DeviceProofV1.DeviceBinding(operation, context.DeviceId, context.ClientKind, context.AppVersion, timestamp), ct);
        return new ClientContext
        {
            SupportsSsoPending = context.SupportsSsoPending,
            RequiredGate = DeviceProofV1.RegisteredDeviceGate,
            DeviceBinding = new ClientDeviceBindingProof
            {
                ActivationOperationId = operation, Timestamp = timestamp, Signature = signature,
            },
            ClientKind = context.ClientKind,
            DeviceId = context.DeviceId,
            AppVersion = context.AppVersion,
            PlatformVersion = context.PlatformVersion,
        };
    }

    private async Task RememberAsync(Uri source, string device, Guid operation, CancellationToken ct)
    {
        var path = StatePath("binding", source, device, "", Guid.Empty);
        var temporary = path + "." + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllTextAsync(temporary, operation.ToString("D"), ct);
            File.Move(temporary, path, true);
        }
        finally { File.Delete(temporary); }
    }

    private string StatePath(string kind, Uri source, string device, string tenant, Guid operation)
    {
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            new object[] { source.AbsoluteUri, device, tenant, operation })));
        return Path.Combine(options.ActivationStateDirectory, kind + "-" + hash);
    }

    private static bool IsUnknown(Exception error, CancellationToken ct)
        => error is ApiException api ? (int?)api.StatusCode >= 500
            : error is HttpRequestException or JsonException || error is OperationCanceledException && !ct.IsCancellationRequested;
}
