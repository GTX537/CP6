using System.Data.Common;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Primitives;
using CP6.Core.Services.CrmIdentity;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Services;

public interface ICrmOidcServiceDirectory
{
    Task<bool> IsServiceTenantActiveAsync(Guid tenantId, DateTime utcNow,
        CancellationToken cancellationToken = default);
}

public sealed class CrmOidcServiceTokens
{
    private readonly CrmOidcOptions _options;
    private readonly CrmOidcCrypto _crypto;
    private readonly ICrmOidcServiceDirectory _directory;
    private readonly TimeProvider _timeProvider;
    private readonly ICrmServiceTokenRecordStore? _records;
    internal bool RevocationEnabled => _records is not null;

    public CrmOidcServiceTokens(CrmOidcOptions options, CrmOidcCrypto crypto,
        ICrmOidcServiceDirectory directory, TimeProvider? timeProvider = null,
        ICrmServiceTokenRecordStore? records = null)
    {
        _options = options;
        _crypto = crypto;
        _directory = directory;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _records = records;
    }

    internal CrmOidcServiceAuthentication Authenticate(StringValues authorization)
    {
        if (!TryDecodeBasic(authorization, out var id, out var secret))
            return new(null);
        var client = _options.ServiceClients.SingleOrDefault(c => c.ClientId == id);
        return client != null && CrmOidcCrypto.EqualsSecret(secret, client.SecretSha256)
            ? new(client)
            : new(null);
    }

    internal async Task<CrmOidcServiceTokenIssue> IssueAsync(CrmOidcServiceClient client,
        CancellationToken cancellationToken = default)
    {
        if (!client.Enabled || !client.AllowedScopes.Contains("cp6.services", StringComparer.Ordinal))
            return CrmOidcServiceTokenIssue.Unauthorized;
        var lookupTime = _timeProvider.GetUtcNow();
        bool active;
        try
        {
            active = await _directory.IsServiceTenantActiveAsync(client.TenantId,
                lookupTime.UtcDateTime, cancellationToken);
        }
        catch (Exception ex) when (IsDatabaseUnavailable(ex))
        {
            return CrmOidcServiceTokenIssue.Unavailable;
        }
        if (!active) return CrmOidcServiceTokenIssue.Unauthorized;
        var issuedAt = _timeProvider.GetUtcNow();
        var expiresAt = issuedAt.AddSeconds(300);
        var jti = Guid.NewGuid().ToString("D");
        var token = _crypto.Sign("CP6.Services",
        [
            new Claim("sub", "service:" + client.ClientId),
            new Claim("client_id", client.ClientId),
            new Claim("tenant_id", client.TenantId.ToString()),
            new Claim("jti", jti),
            new Claim("scope", "cp6.services")
        ], expiresAt, "at+jwt");
        if (_records is not null)
        {
            try
            {
                await _records.RecordAsync(_options.Issuer, client.ClientId, client.TenantId, jti,
                    DateTimeOffset.FromUnixTimeSeconds(expiresAt.ToUnixTimeSeconds()), cancellationToken);
            }
            catch (Exception ex) when (IsDatabaseUnavailable(ex))
            { return CrmOidcServiceTokenIssue.Unavailable; }
        }
        var expiresIn = Math.Clamp((int)Math.Floor(
            (expiresAt - _timeProvider.GetUtcNow()).TotalSeconds), 0, 300);
        return new(CrmOidcServiceTokenIssueStatus.Success, token, expiresIn);
    }

    private static bool IsDatabaseUnavailable(Exception exception)
    {
        if (exception is DbException or TimeoutException) return true;
        if (exception is DbUpdateException { InnerException: { } updateInner }) return IsDatabaseUnavailable(updateInner);
        if (exception is RetryLimitExceededException retry)
            return retry.InnerException != null && IsDatabaseUnavailable(retry.InnerException);
        return exception is InvalidOperationException { InnerException: { } inner }
            && IsDatabaseUnavailable(inner);
    }

    internal async Task<bool> RevokeAsync(CrmOidcServiceClient client, string jti, CancellationToken cancellationToken)
    {
        if (_records is null) return false;
        try
        {
            await _records.RevokeAsync(_options.Issuer, client.ClientId, client.TenantId, jti, cancellationToken);
            return true;
        }
        catch (Exception ex) when (IsDatabaseUnavailable(ex)) { return false; }
    }

    internal static bool TryDecodeBasic(StringValues authorization, out string id, out string secret)
    {
        id = "";
        secret = "";
        if (authorization.Count != 1) return false;
        var value = authorization[0];
        if (value == null || !value.StartsWith("Basic ", StringComparison.Ordinal) || value.Length > 2048)
            return false;
        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(value[6..]));
            var separator = decoded.IndexOf(':');
            if (separator < 1) return false;
            id = Uri.UnescapeDataString(decoded[..separator].Replace('+', ' '));
            secret = Uri.UnescapeDataString(decoded[(separator + 1)..].Replace('+', ' '));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

internal sealed record CrmOidcServiceAuthentication(CrmOidcServiceClient? Client)
{
    public bool IsAuthenticated => Client != null;
}

internal enum CrmOidcServiceTokenIssueStatus
{
    Success,
    Unauthorized,
    Unavailable
}

internal sealed record CrmOidcServiceTokenIssue(CrmOidcServiceTokenIssueStatus Status,
    string? AccessToken = null, int ExpiresIn = 0)
{
    public static CrmOidcServiceTokenIssue Unauthorized { get; } = new(CrmOidcServiceTokenIssueStatus.Unauthorized);
    public static CrmOidcServiceTokenIssue Unavailable { get; } = new(CrmOidcServiceTokenIssueStatus.Unavailable);
}
