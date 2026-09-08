using CP6.Core.Migrations;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.WebApi.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CP6.Oidc.IntegrationTests;

/// <summary>Dedicated required SQL gate; never substitutes SQLite, an in-memory store, or a skipped test.</summary>
public sealed partial class GrantStoreSqlTests : IAsyncLifetime
{
    private readonly string _database = "CP6OidcTest_" + Guid.NewGuid().ToString("N");
    private string _master = "";
    private string _connection = "";
    private readonly Guid _legacyTokenId = Guid.NewGuid();
    private readonly Guid _legacyUserId = Guid.NewGuid();
    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable("CP6_OIDC_TEST_SQL");
        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException("CP6_OIDC_TEST_SQL must point to an isolated SQL Server test instance with CREATE DATABASE permission.");
        var builder = new SqlConnectionStringBuilder(configured) { InitialCatalog = "master" };
        _master = builder.ConnectionString;
        await using var master = new SqlConnection(_master);
        await master.ExecuteAsync($"CREATE DATABASE [{_database}]");
        builder.InitialCatalog = _database;
        _connection = builder.ConnectionString;
        await using var db = new SqlConnection(_connection);
        await db.ExecuteAsync(CrmOidcGrantStore.CreateSql);
        await using var model = new CP6Context(new DbContextOptionsBuilder<CP6Context>().UseSqlServer(_connection).Options);
        // Extract the real EF table/index DDL rather than approximating the production refresh-token schema.
        var ddl = model.Database.GenerateCreateScript();
        foreach (var batch in System.Text.RegularExpressions.Regex.Split(ddl, @"^GO\s*$", System.Text.RegularExpressions.RegexOptions.Multiline))
            if (batch.Contains("CREATE TABLE [Sys_RefreshTokens]", StringComparison.Ordinal)
                || batch.Contains("CREATE TABLE [Sys_Users]", StringComparison.Ordinal)
                || batch.Contains("CREATE TABLE [Sys_FieldAuditLogs]", StringComparison.Ordinal)
                || batch.Contains("CREATE UNIQUE INDEX [UX_Sys_RefreshToken_TokenHash]", StringComparison.Ordinal))
                await db.ExecuteAsync(batch);
        // Exercise the actual forward upgrade against the preceding refresh/user column shape.
        await db.ExecuteAsync("ALTER TABLE dbo.Sys_Users DROP COLUMN AuthenticationEpoch; ALTER TABLE dbo.Sys_RefreshTokens DROP COLUMN BrowserSessionId;");
        await db.ExecuteAsync("""
            INSERT dbo.Sys_Users(Id,TenantId,UserName,Password,Enable,FailedLoginCount,MustChangePassword,TwoFactorEnabled,AllowPasswordFallback,IsPlatformAdmin,CreateDate)
            VALUES(@userId,NEWID(),N'pre-migration-user',N'pre-migration-hash',1,0,0,0,0,0,GETDATE());
            INSERT dbo.Sys_RefreshTokens(Id,UserId,TenantId,TokenHash,ExpiresAt,ClientKind,CreateDate)
            VALUES(@tokenId,@userId,NEWID(),N'pre-migration-token-hash',DATEADD(day,1,GETDATE()),N'Web',GETDATE());
            """, new { userId = _legacyUserId, tokenId = _legacyTokenId });
        await db.ExecuteAsync(CrmOidcBrowserSessionFamily.CreateSql);
    }

    [Fact]
    public async Task Forward_family_migration_preserves_legacy_credentials_without_upgrading_their_authentication()
    {
        await using var db = new SqlConnection(_connection);
        var row = await db.QuerySingleAsync<Sys_RefreshToken>("SELECT * FROM dbo.Sys_RefreshTokens WHERE Id=@id", new { id = _legacyTokenId });
        Assert.Equal("pre-migration-token-hash", row.TokenHash);
        Assert.Null(row.RevokedAt);
        Assert.Null(row.BrowserSessionId);
        Assert.Equal(Guid.Empty, await db.QuerySingleAsync<Guid>("SELECT AuthenticationEpoch FROM dbo.Sys_Users WHERE Id=@id", new { id = _legacyUserId }));
        Assert.Equal(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM dbo.Sys_BrowserSessions"));
    }

    [Fact]
    public void Forward_migration_is_discoverable_and_generates_an_idempotent_script()
    {
        using var context = new CP6Context(new DbContextOptionsBuilder<CP6Context>().UseSqlServer(_connection).Options);
        var migrations = context.Database.GetMigrations().ToArray();
        Assert.Equal("20260908011000_CrmOidcBrowserSessionFamily", migrations[^1]);
        Assert.False(context.Database.HasPendingModelChanges());
        var script = context.GetService<IMigrator>().GenerateScript(migrations[^3], migrations[^1], MigrationsSqlGenerationOptions.Idempotent);
        Assert.Contains("CREATE TABLE dbo.CrmOidcGrant", script);
        Assert.Contains("CREATE TABLE dbo.CrmOidcLogout", script);
        Assert.Contains("CREATE TABLE dbo.Sys_BrowserSessions", script);
        Assert.Contains("__EFMigrationsHistory", script);
        Assert.DoesNotContain("DROP TABLE", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Twenty_four_independent_SQL_connections_redeem_exactly_once()
    {
        var grant = Grant();
        await new SqlCrmOidcGrantStore(_connection).SaveAsync(grant);
        var results = await Task.WhenAll(Enumerable.Range(0, 24).Select(_ =>
            new SqlCrmOidcGrantStore(_connection).ConsumeAsync(grant.CodeHash, grant.ClientId, grant.RedirectUri, grant.Challenge)));
        Assert.Single(results, r => r != null);
        Assert.True((await new SqlCrmOidcGrantStore(_connection).FindSessionAsync(grant.Id))!.Consumed);
    }

    [Theory]
    [InlineData("client")]
    [InlineData("redirect")]
    [InlineData("redirect-case")]
    [InlineData("redirect-padding")]
    [InlineData("challenge")]
    [InlineData("hash")]
    public async Task Every_code_binding_is_exact_and_failed_attempt_does_not_consume(string changed)
    {
        var grant = Grant();
        var store = new SqlCrmOidcGrantStore(_connection);
        await store.SaveAsync(grant);
        var redirect = changed switch { "redirect" => grant.RedirectUri + "x", "redirect-case" => grant.RedirectUri.ToUpperInvariant(), "redirect-padding" => grant.RedirectUri + " ", _ => grant.RedirectUri };
        Assert.Null(await store.ConsumeAsync(changed == "hash" ? new string('0', 64) : grant.CodeHash,
            changed == "client" ? "CP6.Services" : grant.ClientId, redirect,
            changed == "challenge" ? new string('x', 43) : grant.Challenge));
        Assert.NotNull(await store.ConsumeAsync(grant.CodeHash, grant.ClientId, grant.RedirectUri, grant.Challenge));
    }

    [Theory]
    [InlineData("code")]
    [InlineData("source")]
    [InlineData("revoked")]
    public async Task Expired_or_revoked_grants_fail_across_store_instances(string changed)
    {
        var grant = Grant();
        if (changed == "code") grant.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        if (changed == "source") grant.SourceExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await new SqlCrmOidcGrantStore(_connection).SaveAsync(grant);
        if (changed == "revoked") await new SqlCrmOidcGrantStore(_connection).RevokeAsync(grant.Id);
        Assert.Null(await new SqlCrmOidcGrantStore(_connection).ConsumeAsync(grant.CodeHash, grant.ClientId, grant.RedirectUri, grant.Challenge));
    }

    [Fact]
    public async Task Session_revocation_is_durable_and_ledger_contains_no_raw_code()
    {
        var raw = CrmOidcCrypto.RandomToken();
        var grant = Grant();
        grant.CodeHash = CrmOidcCrypto.Hash(raw);
        var store = new SqlCrmOidcGrantStore(_connection);
        await store.SaveAsync(grant);
        await store.ConsumeAsync(grant.CodeHash, grant.ClientId, grant.RedirectUri, grant.Challenge);
        Assert.NotNull(await new SqlCrmOidcGrantStore(_connection).FindSessionAsync(grant.Id));
        await new SqlCrmOidcGrantStore(_connection).RevokeAsync(grant.Id);
        Assert.Null(await new SqlCrmOidcGrantStore(_connection).FindSessionAsync(grant.Id));
        await using var db = new SqlConnection(_connection);
        var columns = (await db.QueryAsync<string>("SELECT name FROM sys.columns WHERE object_id=OBJECT_ID('dbo.CrmOidcGrant')")).ToArray();
        Assert.DoesNotContain("Code", columns);
        Assert.DoesNotContain("RawCode", columns);
        Assert.Equal(grant.CodeHash, await db.QuerySingleAsync<string>("SELECT CodeHash FROM dbo.CrmOidcGrant WHERE Id=@id", new { id = grant.Id }));
    }

    [Fact]
    public async Task Global_logout_revokes_only_original_refresh_family_and_keeps_expired_grant_for_logout()
    {
        var grant = Grant();
        grant.AccessExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        var store = new SqlCrmOidcGrantStore(_connection);
        await store.SaveAsync(grant);
        await store.ConsumeAsync(grant.CodeHash, grant.ClientId, grant.RedirectUri, grant.Challenge);
        var leaf = CrmOidcCrypto.Hash(CrmOidcCrypto.RandomToken());
        var unrelated = CrmOidcCrypto.Hash(CrmOidcCrypto.RandomToken());
        await AddRefreshAsync(grant, grant.SourceRefreshHash, leaf, true);
        await AddRefreshAsync(grant, leaf, null, false);
        await AddRefreshAsync(grant, unrelated, null, false, sameFamily: false);
        Assert.Null(await store.FindSessionAsync(grant.Id));
        Assert.NotNull(await store.FindForLogoutAsync(grant.Id));
        await store.RevokeSourceFamilyAsync(grant);
        await using var db = new SqlConnection(_connection);
        Assert.Equal(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM dbo.Sys_RefreshTokens WHERE TokenHash IN (@root,@leaf) AND RevokedAt IS NULL", new { root = grant.SourceRefreshHash, leaf }));
        Assert.Equal(1, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM dbo.Sys_RefreshTokens WHERE TokenHash=@unrelated AND RevokedAt IS NULL", new { unrelated }));
        Assert.True((await store.FindForLogoutAsync(grant.Id))!.Revoked);
    }

    [Fact]
    public async Task Real_refresh_after_global_logout_does_not_revoke_unrelated_browser_or_native_sessions()
    {
        var tenant = new TenantContext { CurrentTenantId = Guid.NewGuid() };
        await using var context = new CP6Context(new DbContextOptionsBuilder<CP6Context>().UseSqlServer(_connection).Options, tenant);
        var user = new Sys_User { Id = Guid.NewGuid(), TenantId = tenant.CurrentTenantId, UserName = "logout-race-user", Password = "test-hash" };
        context.Sys_Users.Add(user);
        await context.SaveChangesAsync();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["CrmOidc:Enabled"] = "true" }).Build();
        using var services = new ServiceCollection().AddSingleton<IConfiguration>(config).BuildServiceProvider();
        var refresh = ActivatorUtilities.CreateInstance<RefreshTokenService>(services, context, Options.Create(new SecurityOptions()), tenant);
        var original = await refresh.IssueAsync(user, null, null);
        var unrelated = await refresh.IssueAsync(user, null, null);
        var native = await refresh.IssueAsync(user, null, null, new RefreshTokenClientContext("Windows", "other-device", "1"));
        var grant = Grant();
        grant.SubjectId = user.Id;
        grant.OrganizationId = user.TenantId;
        grant.SourceRefreshHash = CrmOidcCrypto.Hash(original);
        var store = new SqlCrmOidcGrantStore(_connection);
        await store.SaveAsync(grant);
        await store.ConsumeAsync(grant.CodeHash, grant.ClientId, grant.RedirectUri, grant.Challenge);
        await store.RevokeSourceFamilyAsync(grant);
        context.ChangeTracker.Clear();

        Assert.IsType<InvalidOperationException>(await Record.ExceptionAsync(() => refresh.RotateAsync(original, null, null)));
        await using var check = new SqlConnection(_connection);
        Assert.Equal(2, await check.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM dbo.Sys_RefreshTokens WHERE TokenHash IN (@unrelated,@native) AND RevokedAt IS NULL",
            new { unrelated = CrmOidcCrypto.Hash(unrelated), native = CrmOidcCrypto.Hash(native) }));
    }

    [Fact]
    public async Task Logout_cookie_continuation_is_one_use_across_replicas_and_expires()
    {
        var grant = Grant();
        var ticket = new CrmOidcLogout { GrantId = grant.Id, TicketHash = CrmOidcCrypto.Hash(CrmOidcCrypto.RandomToken()), RedirectUri = "https://crm.example/signed-out", ExpiresAtUtc = DateTime.UtcNow.AddSeconds(60) };
        await new SqlCrmOidcGrantStore(_connection).SaveLogoutAsync(ticket);
        var consumed = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => new SqlCrmOidcGrantStore(_connection).ConsumeLogoutAsync(ticket.TicketHash)));
        Assert.Single(consumed, x => x != null);
        ticket.TicketHash = CrmOidcCrypto.Hash(CrmOidcCrypto.RandomToken());
        ticket.ExpiresAtUtc = DateTime.UtcNow.AddSeconds(-1);
        await new SqlCrmOidcGrantStore(_connection).SaveLogoutAsync(ticket);
        Assert.Null(await new SqlCrmOidcGrantStore(_connection).ConsumeLogoutAsync(ticket.TicketHash));
    }

    private readonly Dictionary<Guid, Guid> _manualFamilies = new();
    private async Task AddRefreshAsync(CrmOidcGrant grant, string hash, string? next, bool revoked, bool sameFamily = true)
    {
        await using var db = new SqlConnection(_connection);
        if (!_manualFamilies.TryGetValue(grant.Id, out var familyId)) _manualFamilies[grant.Id] = familyId = Guid.NewGuid();
        if (!sameFamily) familyId = Guid.NewGuid();
        await db.ExecuteAsync("""
            IF NOT EXISTS (SELECT 1 FROM dbo.Sys_BrowserSessions WHERE Id=@familyId)
                INSERT dbo.Sys_BrowserSessions(Id,UserId,TenantId,AuthenticationVersion,CreateDate)
                VALUES(@familyId,@SubjectId,@OrganizationId,@SecurityStamp,GETDATE());
            INSERT dbo.Sys_RefreshTokens(Id,UserId,TenantId,TokenHash,BrowserSessionId,ExpiresAt,RevokedAt,ReplacedByTokenHash,ClientKind,CreateDate)
            VALUES(NEWID(),@SubjectId,@OrganizationId,@hash,@familyId,DATEADD(day,1,GETDATE()),CASE WHEN @revoked=1 THEN GETDATE() ELSE NULL END,@next,N'Web',GETDATE())
            """, new { grant.SubjectId, grant.OrganizationId, grant.SecurityStamp, familyId, hash, next, revoked });
    }

    private static CrmOidcGrant Grant() => new()
    {
        Id = Guid.NewGuid(), CodeHash = CrmOidcCrypto.Hash(CrmOidcCrypto.RandomToken()), ClientId = "CP6.Web",
        RedirectUri = "https://crm.example/signin-oidc", Challenge = CrmOidcCrypto.Challenge(CrmOidcCrypto.RandomToken()),
        Nonce = CrmOidcCrypto.RandomToken(), SubjectId = Guid.NewGuid(), OrganizationId = Guid.NewGuid(),
        SourceJti = Guid.NewGuid().ToString(), SecurityStamp = new string('A', 64),
        SourceRefreshHash = CrmOidcCrypto.Hash(CrmOidcCrypto.RandomToken()),
        ExpiresAtUtc = DateTime.UtcNow.AddMinutes(1), AccessExpiresAtUtc = DateTime.UtcNow.AddMinutes(5), SourceExpiresAtUtc = DateTime.UtcNow.AddMinutes(10)
    };

    public async Task DisposeAsync()
    {
        if (_master.Length == 0) return;
        // Only the generated test database is eligible for removal, even if configuration targets a real catalog.
        if (!System.Text.RegularExpressions.Regex.IsMatch(_database, "^CP6OidcTest_[0-9a-f]{32}$"))
            throw new InvalidOperationException("Refusing to remove a database outside the generated test namespace.");
        SqlConnection.ClearAllPools();
        await using var master = new SqlConnection(_master);
        await master.ExecuteAsync($"IF DB_ID('{_database}') IS NOT NULL BEGIN ALTER DATABASE [{_database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_database}]; END");
    }
}
