using System.Net;
using System.Text;
using System.Text.Json;
using CP6.Core.Persistence.Tenancy;
using CP6.WebApi.Services.ServiceRevocationTenancy;
using Npgsql;
using NpgsqlTypes;

namespace CP6.Tests.Persistence.Tenancy;

[CollectionDefinition("ServiceTokenPostgres", DisableParallelization = true)]
public sealed class ServiceTokenPostgresCollection : ICollectionFixture<ServiceTokenPostgresFixture>;
public sealed class ServiceTokenPostgresFixture() : ServiceRevocationPostgresFixture(serviceTokens: true);

[Collection("ServiceTokenPostgres")]
public sealed class ServiceTokenPostgresTests(ServiceTokenPostgresFixture postgres)
{
    private static readonly Guid A = ServiceRevocationPostgresFixture.A, B = ServiceRevocationPostgresFixture.B;
    private static Task<HttpResponseMessage> Issue(RevocationHttpHost host, string client = "client-a", string? secret = null,
        Dictionary<string,string>? form = null) => host.Client.SendAsync(host.Request("/connect/token", client,
            form ?? new() { ["grant_type"] = "client_credentials", ["scope"] = "cp6.services" }, secret));

    [TenantStoragePostgresFact]
    public async Task Real_http_issue_record_self_revoke_and_pool_isolation()
    {
        // Run unchanged end-to-end assertions against the new profile and actual tenant directory.
        var revoke = new ServiceRevocationPostgresTests(postgres);
        await revoke.Http_real_issuance_revoke_repeat_concurrency_and_cross_client_isolation();
        await revoke.Http_invalid_auth_never_opens_runtime_database_and_disabled_client_can_self_revoke();
        await revoke.Runtime_has_only_limited_table_rights_and_cannot_create_or_edit_directory();
        await revoke.Production_verifier_reauthenticates_and_authority_rejects_forgery_and_previous_host_source();
        await using var host = await postgres.Start();
        using var validator = new CP6.WebApi.Services.CrmOidcCrypto(host.Options,host.Clock);
        host.Options.Issuer = "https://mutated.example";
        host.Options.Keys[0].Pem = "mutated-after-registration";
        host.Options.Keys.Clear();
        host.Options.ActiveKeyId = "mutated-key-id";
        var response = await Issue(host); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(json.RootElement.GetProperty("access_token").GetString());
        validator.Validate(json.RootElement.GetProperty("access_token").GetString()!,"CP6.Services","at+jwt");
        Assert.Equal("fixture",jwt.Header.Kid);
        var jwksResponse = await host.Client.GetAsync("/.well-known/jwks.json");
        Assert.Equal(HttpStatusCode.OK,jwksResponse.StatusCode);
        var jwks = new Microsoft.IdentityModel.Tokens.JsonWebKeySet(await jwksResponse.Content.ReadAsStringAsync());
        var verifiedByPublicKey = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler { MapInboundClaims=false }
            .ValidateToken(json.RootElement.GetProperty("access_token").GetString(),
                new Microsoft.IdentityModel.Tokens.TokenValidationParameters
                {
                    ValidIssuer="https://127.0.0.1", ValidAudience="CP6.Services", IssuerSigningKeys=jwks.GetSigningKeys(),
                    ValidAlgorithms=[Microsoft.IdentityModel.Tokens.SecurityAlgorithms.RsaSha256], ValidTypes=["at+jwt"],
                    ValidateIssuerSigningKey=true, ValidateLifetime=true, ClockSkew=TimeSpan.Zero
                }, out _);
        Assert.Equal("service:client-a",verifiedByPublicKey.FindFirst("sub")!.Value);
        Assert.Equal("https://127.0.0.1", jwt.Issuer);
        Assert.Equal("CP6.Services", jwt.Audiences.Single());
        Assert.Equal("service:client-a", jwt.Subject); Assert.Equal("at+jwt", jwt.Header.Typ);
        Assert.Equal(A.ToString("D"), jwt.Claims.Single(c => c.Type == "tenant_id").Value);
        Assert.InRange(json.RootElement.GetProperty("expires_in").GetInt32(), 299, 300);
        await using var owner = await postgres.Owner();
        await using var row = new NpgsqlCommand("SELECT \"ExpiresAtUtc\" FROM crm_identity.\"ServiceToken\" WHERE \"Jti\"=CAST(@jti AS bpchar)", owner);
        row.Parameters.AddWithValue("jti", jwt.Id);
        Assert.Equal(jwt.ValidTo, (DateTime)(await row.ExecuteScalarAsync())!);
        Assert.Equal(0L, await TenantStoragePostgresFixture.Scalar(owner, "SELECT count(*) FROM pg_catalog.pg_class WHERE relname LIKE '__EFMigrationsHistory%'"));
    }

    [TenantStoragePostgresFact]
    public async Task Invalid_basic_scope_and_forged_tenant_do_not_open_pool_or_write()
    {
        await using var host = await postgres.Start(); var before = await Count();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Issue(host, secret: "wrong")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Issue(host, "unknown")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Issue(host, form: new() { ["grant_type"]="client_credentials",["scope"]="other" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Issue(host, form: new() { ["grant_type"]="client_credentials",["scope"]="cp6.services",["tenant_id"]=B.ToString() })).StatusCode);
        using var bad = host.Request("/connect/token", "client-a", new() { ["grant_type"]="client_credentials",["scope"]="cp6.services" });
        bad.Headers.Remove("Authorization"); bad.Headers.TryAddWithoutValidation("Authorization", "Basic /w==");
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.SendAsync(bad)).StatusCode);
        Assert.Equal(0, host.Tenant.PoolSnapshot.Pools); Assert.Equal(before, await Count());
        await using var disabled = await postgres.Start(disabledA:true);
        Assert.Equal(HttpStatusCode.BadRequest, (await Issue(disabled)).StatusCode);
        Assert.Equal(0, disabled.Tenant.PoolSnapshot.Pools);
        var verifier = new ServiceRevocationTenantHost.SourceVerifier(host.Tenant);
        var source = new ServiceRevocationTenantHost.SourceInput(["Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("client-a:"+postgres.ServiceSecret))],"client-a",A,Issue:true);
        var verified = (await verifier.VerifyAsync(source))!;
        Assert.Equal(ServiceRevocationTenantHost.IssuePurpose, verified.Operation.Purpose);
        Assert.Null(await verifier.VerifyAsync(source with { Tenant=B }));
        await using var replacement = await postgres.Start();
        Assert.Null(await new ServiceRevocationTenantHost.BindingAuthority(replacement.Tenant).ResolveCurrentAsync(verified.Operation,verified.Source));
    }

    [TenantStoragePostgresFact]
    public async Task Real_directory_null_future_equal_past_disabled_and_missing_are_enforced()
    {
        await using var host = await postgres.Start();
        var now = host.Clock.GetUtcNow().UtcDateTime.ToLocalTime();
        try
        {
            foreach (var expiry in new DateTime?[] { null, now.AddSeconds(1) })
            { await Tenant(true,expiry); Assert.Equal(HttpStatusCode.OK,(await Issue(host)).StatusCode); }
            var before = await Count();
            foreach (var expiry in new[] { now,now.AddTicks(-10) })
            { await Tenant(true,expiry); await Unauthorized(host); }
            await Tenant(false,null); await Unauthorized(host);
            await using (var control = await postgres.Owner())
                await TenantStoragePostgresFixture.Sql(control,$"UPDATE public.\"Sys_Tenants\" SET \"Id\"='{Guid.NewGuid()}' WHERE \"Id\"='{A}'");
            await Unauthorized(host); Assert.Equal(before,await Count());
        }
        finally
        {
            await using var control = await postgres.Owner();
            await TenantStoragePostgresFixture.Sql(control,$"UPDATE public.\"Sys_Tenants\" SET \"Id\"='{A}' WHERE \"TenantCode\"='a'");
            await Tenant(true,null);
        }
    }

    [TenantStoragePostgresFact]
    public async Task Final_time_and_authority_fences_rollback_before_success()
    {
        foreach (var kind in new[] { "tenant", "source", "registration", "failure" })
        {
            await using var host = await postgres.Start(configure: c =>
            { if (kind == "registration") c["CrmIdentity:ServiceTokenTenancy:ValidUntilUtc"] = DateTimeOffset.UtcNow.AddSeconds(1).ToString("O"); });
            var before = await Count();
            await Tenant(true,kind == "tenant" ? host.Clock.GetUtcNow().UtcDateTime.ToLocalTime().AddSeconds(1) : null);
            host.Tenant.Checkpoint = point =>
            {
                if (point == ServiceRevocationCheckpoint.AfterWriteBeforeCommit)
                {
                    if (kind == "failure") throw new InvalidOperationException("injected-before-commit");
                    host.Clock.Advance(kind == "source" ? TimeSpan.FromSeconds(61) : TimeSpan.FromSeconds(2));
                }
                return Task.CompletedTask;
            };
            try
            {
                var response = await Issue(host);
                Assert.Equal(kind == "tenant" ? HttpStatusCode.BadRequest : HttpStatusCode.ServiceUnavailable,response.StatusCode);
                Assert.DoesNotContain("access_token",await response.Content.ReadAsStringAsync());
                Assert.Equal(before,await Count());
            }
            finally { await Tenant(true,null); }
        }
    }

    [TenantStoragePostgresFact]
    public async Task Tenant_update_and_issuance_are_serialized_in_both_orders_and_wait_rechecks_time()
    {
        await using var host = await postgres.Start();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Tenant.Checkpoint = async point => { if (point == ServiceRevocationCheckpoint.AfterWriteBeforeCommit) { reached.TrySetResult(); await resume.Task; } };
        var issuing = Issue(host); await reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await using var control = await postgres.Owner();
        var update = TenantStoragePostgresFixture.Sql(control,$"UPDATE public.\"Sys_Tenants\" SET \"Enable\"=false WHERE \"Id\"='{A}'");
        try
        {
            await Waiting(control.ProcessID);
            Assert.False(update.IsCompleted); resume.TrySetResult(); Assert.Equal(HttpStatusCode.OK,(await issuing).StatusCode); await update;
            host.Tenant.Checkpoint=null; await Unauthorized(host);
        }
        finally { resume.TrySetResult(); await update; await Tenant(true,null); }
        foreach (var disable in new[] { true,false })
        {
            await using var tx = await control.BeginTransactionAsync();
            await using (var write = new NpgsqlCommand($"UPDATE public.\"Sys_Tenants\" SET \"Enable\"=@enabled,\"ExpireDate\"=@expiry WHERE \"Id\"='{A}'",control,tx))
            {
                write.Parameters.AddWithValue("enabled",!disable);
                write.Parameters.AddWithValue("expiry",NpgsqlDbType.Timestamp,DateTime.SpecifyKind(host.Clock.GetUtcNow().UtcDateTime.ToLocalTime().AddSeconds(1),DateTimeKind.Unspecified));
                await write.ExecuteNonQueryAsync();
            }
            var before=await Count(); var waitingIssue=Issue(host);
            try
            {
                await Waiting(null); host.Clock.Advance(TimeSpan.FromSeconds(2));
                await tx.CommitAsync(); Assert.Equal(HttpStatusCode.BadRequest,(await waitingIssue).StatusCode);
                Assert.Equal(before,await Count());
            }
            finally { await tx.DisposeAsync(); await Tenant(true,null); }
        }
    }

    [TenantStoragePostgresFact]
    public async Task Lost_response_is_uncertain_and_retry_is_a_new_issuance()
    {
        await using var host=await postgres.Start(); var before=await Count();
        host.Tenant.Checkpoint=point => point==ServiceRevocationCheckpoint.AfterCommit
            ? Task.FromException(new InvalidOperationException("injected-lost-response")) : Task.CompletedTask;
        var response=await Issue(host); Assert.Equal(HttpStatusCode.ServiceUnavailable,response.StatusCode);
        Assert.DoesNotContain("access_token",await response.Content.ReadAsStringAsync()); Assert.Equal(before+1,await Count());
        host.Tenant.Checkpoint=null; await host.Issue(); Assert.Equal(before+2,await Count());
    }

    [TenantStoragePostgresFact]
    public async Task Runtime_cannot_enumerate_or_modify_registry_and_rls_prevents_cross_tenant_insert()
    {
        await using var host=await postgres.Start(); var jti=await host.Issue("client-b");
        await using var tenant=await TenantStoragePostgresFixture.OpenRawAsync("tenant_a",postgres.Server.PasswordA,ServiceRevocationPostgresFixture.Database);
        foreach (var sql in new[] { "SELECT * FROM public.\"Sys_Tenants\"", "UPDATE public.\"Sys_Tenants\" SET \"Enable\"=false", "SELECT * FROM cp6_tenancy_v1.login_tenant" })
            Assert.Equal("42501",(await Assert.ThrowsAsync<PostgresException>(()=>TenantStoragePostgresFixture.Sql(tenant,sql))).SqlState);
        Assert.Equal(1L,await TenantStoragePostgresFixture.Scalar(tenant,"SELECT count(*) FROM cp6_service_token_v1.current_tenant_eligibility()"));
        Assert.Equal(0L,await TenantStoragePostgresFixture.Scalar(tenant,$"SELECT count(*) FROM crm_identity.\"ServiceToken\" WHERE \"Jti\"='{jti}'"));
        Assert.Equal("42501",(await Assert.ThrowsAsync<PostgresException>(()=>TenantStoragePostgresFixture.Sql(tenant,$"""
            INSERT INTO crm_identity."ServiceToken"("Issuer","Jti","TenantId","ClientId","ExpiresAtUtc")
            VALUES('https://127.0.0.1','{Guid.NewGuid():D}','{B}','client-b',CURRENT_TIMESTAMP)
            """))).SqlState);
        await using var control=await postgres.Owner();
        Assert.False((bool)(await TenantStoragePostgresFixture.Scalar(control,"SELECT has_table_privilege('service_issuer','crm_identity.\"ServiceToken\"','INSERT')"))!);
    }

    [TenantStoragePostgresFact]
    public async Task Wrong_profile_identity_and_permission_drift_reject_without_default_fallback()
    {
        await using var control=await postgres.Owner();
        await Assert.ThrowsAnyAsync<Exception>(()=>ServiceRevocationInstallation.InstallAsync(control,postgres.Installation,CP6.Persistence.PostgreSql.Tenancy.ServiceRevocationBaselineV1.GenerateSql()));
        var before=await Count();
        await using (var host=await postgres.Start(configure:c=>c["CrmIdentity:ServiceTokenTenancy:InstallationId"]=Guid.NewGuid().ToString("D")))
            Assert.Equal(HttpStatusCode.ServiceUnavailable,(await Issue(host)).StatusCode);
        foreach (var target in new[] { "public.\"Sys_Tenants\"", "pg_catalog.pg_roles" })
        {
        await TenantStoragePostgresFixture.Sql(control,$"GRANT SELECT ON {target} TO tenant_a");
        try
        {
            await Assert.ThrowsAnyAsync<Exception>(()=>postgres.Install(control));
            await using var host=await postgres.Start(); Assert.Equal(HttpStatusCode.ServiceUnavailable,(await Issue(host)).StatusCode);
        }
        finally { await TenantStoragePostgresFixture.Sql(control,$"REVOKE SELECT ON {target} FROM tenant_a"); }
        }
        Assert.Equal(before,await Count()); await postgres.Install(control);
        await using var valid=await postgres.Start(); Assert.Equal(HttpStatusCode.OK,(await Issue(valid)).StatusCode);
    }

    private async Task Unauthorized(RevocationHttpHost host)
    {
        var response=await Issue(host); Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
        Assert.Contains("unauthorized_client",await response.Content.ReadAsStringAsync());
    }
    private async Task<long> Count()
    {
        await using var owner=await postgres.Owner();
        return (long)(await TenantStoragePostgresFixture.Scalar(owner,"SELECT count(*) FROM crm_identity.\"ServiceToken\""))!;
    }
    private async Task Tenant(bool enabled,DateTime? expiry)
    {
        await using var owner=await postgres.Owner();
        await using var command=new NpgsqlCommand("UPDATE public.\"Sys_Tenants\" SET \"Enable\"=@enabled,\"ExpireDate\"=@expiry WHERE \"Id\"=@id",owner);
        command.Parameters.AddWithValue("enabled",enabled); command.Parameters.AddWithValue("id",A);
        command.Parameters.AddWithValue("expiry",NpgsqlDbType.Timestamp,expiry is {} time ? DateTime.SpecifyKind(time,DateTimeKind.Unspecified) : DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }
    private async Task Waiting(int? pid)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var owner=await postgres.Owner();
        while(true)
        {
            await using var command=new NpgsqlCommand("SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock' AND (@pid IS NULL AND usename='tenant_a' OR pid=@pid))",owner);
            command.Parameters.AddWithValue("pid",NpgsqlDbType.Integer,(object?)pid??DBNull.Value);
            if(await command.ExecuteScalarAsync(timeout.Token) is true) return;
            await Task.Delay(20,timeout.Token);
        }
    }
}
