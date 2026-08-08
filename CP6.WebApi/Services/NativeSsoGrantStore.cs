using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Entity.DTOs.Client;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using CP6.Core.Services.Sys;

namespace CP6.WebApi.Services;

public interface INativeSsoGrantStore
{
    Task<string> CreateRequestAsync(
        string redirectUri,
        string codeChallenge,
        ClientContextDto client,
        CancellationToken ct = default);

    Task<NativeSsoRequest?> GetRequestAsync(
        string requestId,
        CancellationToken ct = default);

    Task<string> CompleteAsync(
        string requestId,
        Guid userId,
        Guid tenantId,
        CancellationToken ct = default);

    Task<NativeSsoGrant> ConsumeGrantAsync(
        string grantCode,
        string verifier,
        ClientContextDto client,
        CancellationToken ct = default);
}

public sealed record NativeSsoRequest(
    string RedirectUri,
    string CodeChallenge,
    ClientContextDto Client);

public sealed record NativeSsoGrant(
    Guid UserId,
    Guid TenantId,
    string RedirectUri,
    ClientContextDto Client);

/// <summary>二次 PKCE 原生 SSO grant；请求和授权码均落 IDistributedCache、短期且一次性。</summary>
public sealed class NativeSsoGrantStore : INativeSsoGrantStore
{
    private readonly INativeSsoGrantCache _cache;
    private readonly SecurityOptions _security;

    public NativeSsoGrantStore(
        INativeSsoGrantCache cache,
        IOptions<SecurityOptions> security)
    {
        _cache = cache;
        _security = security.Value;
    }

    private static string RequestKey(string id) => $"sec:native-sso:req:{id}";
    private static string GrantKey(string id) => $"sec:native-sso:grant:{id}";

    public async Task<string> CreateRequestAsync(
        string redirectUri,
        string codeChallenge,
        ClientContextDto client,
        CancellationToken ct = default)
    {
        if (!_security.NativeClient.AllowedRedirectUris.Contains(
                redirectUri, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("E-SEC-024");
        if (string.IsNullOrWhiteSpace(codeChallenge) || codeChallenge.Length is < 43 or > 128)
            throw new InvalidOperationException("E-SEC-024");
        ClientContextValidator.Validate(client);

        var id = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(24));
        await _cache.SetAsync(
            RequestKey(id),
            JsonSerializer.Serialize(new NativeSsoRequest(redirectUri, codeChallenge, client)),
            TimeSpan.FromMinutes(Math.Max(2, _security.Sso.StateMinutes)),
            ct);
        return id;
    }

    public async Task<NativeSsoRequest?> GetRequestAsync(
        string requestId,
        CancellationToken ct = default)
    {
        var json = await _cache.GetAsync(RequestKey(requestId), ct);
        return string.IsNullOrEmpty(json)
            ? null
            : JsonSerializer.Deserialize<NativeSsoRequest>(json);
    }

    public async Task<string> CompleteAsync(
        string requestId,
        Guid userId,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var requestKey = RequestKey(requestId);
        var requestJson = await _cache.GetAsync(requestKey, ct);
        var request = string.IsNullOrEmpty(requestJson)
            ? null
            : JsonSerializer.Deserialize<NativeSsoRequest>(requestJson);
        if (request is null)
            throw new InvalidOperationException("E-SEC-022");
        if (!await _cache.RemoveIfValueMatchesAsync(
                requestKey,
                requestJson!,
                ct))
        {
            throw new InvalidOperationException("E-SEC-022");
        }

        var code = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var grant = new NativeSsoGrant(
            userId, tenantId, request.RedirectUri, request.Client);
        await _cache.SetAsync(
            GrantKey(code),
            JsonSerializer.Serialize(new GrantEnvelope(grant, request.CodeChallenge)),
            TimeSpan.FromMinutes(
                Math.Max(1, _security.NativeClient.SsoGrantMinutes)),
            ct);
        return code;
    }

    public async Task<NativeSsoGrant> ConsumeGrantAsync(
        string grantCode,
        string verifier,
        ClientContextDto client,
        CancellationToken ct = default)
    {
        var key = GrantKey(grantCode);
        var json = await _cache.GetAsync(key, ct);
        if (string.IsNullOrEmpty(json))
            throw new InvalidOperationException("E-SEC-022");

        var envelope = JsonSerializer.Deserialize<GrantEnvelope>(json)
            ?? throw new InvalidOperationException("E-SEC-022");
        var actual = WebEncoders.Base64UrlEncode(
            SHA256.HashData(Encoding.ASCII.GetBytes(verifier ?? string.Empty)));
        if (!FixedTimeEquals(actual, envelope.CodeChallenge))
            throw new InvalidOperationException("E-SEC-024");

        ClientContextValidator.Validate(client);
        if (!string.Equals(client.ClientKind, envelope.Grant.Client.ClientKind, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(client.DeviceId, envelope.Grant.Client.DeviceId, StringComparison.Ordinal))
            throw new InvalidOperationException("E-SEC-024");
        if (!string.Equals(
                client.AppVersion,
                envelope.Grant.Client.AppVersion,
                StringComparison.Ordinal) ||
            !string.Equals(
                client.PlatformVersion,
                envelope.Grant.Client.PlatformVersion,
                StringComparison.Ordinal))
            throw new InvalidOperationException("E-SEC-024");

        if (!await _cache.RemoveIfValueMatchesAsync(key, json, ct))
            throw new InvalidOperationException("E-SEC-022");

        return envelope.Grant;
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var a = Encoding.ASCII.GetBytes(left);
        var b = Encoding.ASCII.GetBytes(right);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    private sealed record GrantEnvelope(NativeSsoGrant Grant, string CodeChallenge);
}

public static class ClientContextValidator
{
    public static void Validate(ClientContextDto client)
    {
        if (client is null
            || (client.ClientKind != "Windows" && client.ClientKind != "Android")
            || string.IsNullOrWhiteSpace(client.DeviceId)
            || client.DeviceId.Length > 128
            || string.IsNullOrWhiteSpace(client.AppVersion)
            || client.AppVersion.Length > 32)
            throw new InvalidOperationException("E-SEC-024");
    }
}
