using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace CP6.Core.Services.Sys;

public partial class SsoService
{
    private readonly AuthenticationPolicyServiceV1? _policies;
    private readonly SecurityOptions? _security;

    private async Task<string> BuildPostgreSqlAuthorizeAsync(Guid tenantId, string redirectUri, string? returnUrl)
    {
        if (_policies == null || _security == null) throw new InvalidOperationException("AUTH_POLICY_UNAVAILABLE");
        var nonce = Guid.NewGuid().ToString("N");
        var verifier = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        string state;
        CP6.Entity.DomainModels.Sys.Sys_TenantSsoConfig cfg;
        await using (var tx = await _db.Database.BeginTransactionAsync())
        {
            await AuthenticationCompletionGateV1.AcquireAsync(_db);
            var policy = await _policies.ReadAsync(tenantId);
            if (!policy.SsoEnabled) throw new InvalidOperationException("E-SEC-020");
            cfg = await _db.Sys_TenantSsoConfigs.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.TenantId == tenantId);
            state = _state.Create(tenantId, nonce, verifier, returnUrl);
            var expires = DateTimeOffset.UtcNow.AddMinutes(Math.Max(1, _security.Sso.StateMinutes));
            await _db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_authentication_v1.sso_states(state_hash,policy,expires_at) VALUES ({AuthenticationInteractionStoreV1.Hash(state)},{JsonSerializer.Serialize(policy)}::jsonb,{expires})");
            await tx.CommitAsync();
        }
        var ep = await _disco.GetAsync(cfg.Authority);
        return $"{ep.AuthorizationEndpoint}?response_type=code&client_id={Uri.EscapeDataString(cfg.ClientId)}"
            + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}&scope={Uri.EscapeDataString(cfg.Scopes)}"
            + $"&state={state}&nonce={nonce}&code_challenge={challenge}&code_challenge_method=S256";
    }

    public async Task BindStateAsync(string state, string clientKind, string clientBinding)
    {
        if (!_db.Database.IsNpgsql() || clientKind is not ("Web" or "Windows" or "Android") || string.IsNullOrWhiteSpace(clientBinding))
            throw new InvalidOperationException("AUTH_PRIMARY_PROOF_REQUIRED");
        if (await _db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_authentication_v1.sso_states SET client_kind={clientKind},client_binding={clientBinding} WHERE state_hash={AuthenticationInteractionStoreV1.Hash(state)} AND client_binding IS NULL AND consumed_at IS NULL AND expires_at>clock_timestamp()") != 1)
            throw new InvalidOperationException("E-SEC-022");
    }

    private async Task<AuthenticationPolicyV1> ConsumePostgreSqlStateAsync(string state, string clientKind, string clientBinding)
    {
        // PostgreSQL compare-and-consume commits before the one-use IdP code is exchanged.
        var rows = await _db.Database.SqlQuery<string>($"UPDATE cp6_authentication_v1.sso_states SET consumed_at=clock_timestamp() WHERE state_hash={AuthenticationInteractionStoreV1.Hash(state)} AND client_kind={clientKind} AND client_binding={clientBinding} AND consumed_at IS NULL AND expires_at>clock_timestamp() RETURNING policy::text AS \"Value\"").ToListAsync();
        if (rows.Count != 1) throw new InvalidOperationException("E-SEC-022");
        return JsonSerializer.Deserialize<AuthenticationPolicyV1>(rows[0]) ?? throw new InvalidOperationException("AUTH_POLICY_UNAVAILABLE");
    }
}
