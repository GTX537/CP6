using CP6.Core.Persistence;
using CP6.Core.Services.Common;
using CP6.Entity.DomainModels.Sys;
using CP6.WebApi.Services;
using Dapper;
using Microsoft.EntityFrameworkCore;

namespace CP6.Oidc.IntegrationTests;

public sealed partial class GrantStoreRelationalTests
{
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
        var store = fixture.CreateStore();
        await store.SaveAsync(grant);
        var redirect = changed switch
        {
            "redirect" => grant.RedirectUri + "x",
            "redirect-case" => grant.RedirectUri.ToUpperInvariant(),
            "redirect-padding" => grant.RedirectUri + " ",
            _ => grant.RedirectUri
        };
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
        await fixture.CreateStore().SaveAsync(grant);
        if (changed == "revoked") await fixture.CreateStore().RevokeAsync(grant.Id);
        Assert.Null(await fixture.CreateStore().ConsumeAsync(grant.CodeHash, grant.ClientId, grant.RedirectUri, grant.Challenge));
    }

    [Fact]
    public async Task Session_revocation_is_durable_and_ledger_contains_no_raw_code()
    {
        var raw = CrmOidcCrypto.RandomToken();
        var grant = Grant();
        grant.CodeHash = CrmOidcCrypto.Hash(raw);
        var store = fixture.CreateStore();
        await store.SaveAsync(grant);
        await store.ConsumeAsync(grant.CodeHash, grant.ClientId, grant.RedirectUri, grant.Challenge);
        Assert.NotNull(await fixture.CreateStore().FindSessionAsync(grant.Id));
        await fixture.CreateStore().RevokeAsync(grant.Id);
        Assert.Null(await fixture.CreateStore().FindSessionAsync(grant.Id));
        await using var db = fixture.CreateConnection();
        var columns = (await db.QueryAsync<string>(fixture.IsPostgreSql
            ? "SELECT column_name FROM information_schema.columns WHERE table_schema='public' AND table_name='CrmOidcGrant'"
            : "SELECT name FROM sys.columns WHERE object_id=OBJECT_ID('dbo.CrmOidcGrant')")).ToArray();
        Assert.NotEmpty(columns);
        Assert.DoesNotContain("Code", columns);
        Assert.DoesNotContain("RawCode", columns);
        Assert.Equal(grant.CodeHash, await db.QuerySingleAsync<string>(
            $"SELECT {C("CodeHash")} FROM {T("CrmOidcGrant")} WHERE {C("Id")}=@id", new { id = grant.Id }));
    }

    [Fact]
    public async Task Global_logout_revokes_only_original_refresh_family_and_keeps_expired_grant_for_logout()
    {
        var grant = Grant();
        grant.AccessExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        var store = fixture.CreateStore();
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
        await using var db = fixture.CreateContext(new TenantContext());
        Assert.Equal(0, await db.Sys_RefreshTokens.IgnoreQueryFilters().CountAsync(x =>
            (x.TokenHash == grant.SourceRefreshHash || x.TokenHash == leaf) && x.RevokedAt == null));
        Assert.Equal(1, await db.Sys_RefreshTokens.IgnoreQueryFilters().CountAsync(x => x.TokenHash == unrelated && x.RevokedAt == null));
        Assert.True((await store.FindForLogoutAsync(grant.Id))!.Revoked);
    }

    [Fact]
    public async Task Real_refresh_after_global_logout_does_not_revoke_unrelated_browser_or_native_sessions()
    {
        var logins = await CreateLoginsAsync();
        var grant = await SaveBrowserGrantAsync(logins);
        await fixture.CreateStore().RevokeSourceFamilyAsync(grant);
        var tenant = new TenantContext();
        await using var context = fixture.CreateContext(tenant);
        var refresh = CreateRefresh(context, tenant);
        Assert.IsType<InvalidOperationException>(await Record.ExceptionAsync(() => refresh.RotateAsync(logins.Original, null, null)));
        await AssertOnlyOriginalFamilyLoggedOutAsync(logins);
    }

    [Fact]
    public async Task Logout_cookie_continuation_is_one_use_across_replicas_and_expires()
    {
        var ticket = new CrmOidcLogout
        {
            GrantId = Guid.NewGuid(), TicketHash = CrmOidcCrypto.Hash(CrmOidcCrypto.RandomToken()),
            RedirectUri = "https://crm.example/signed-out", ExpiresAtUtc = DateTime.UtcNow.AddSeconds(60)
        };
        await fixture.CreateStore().SaveLogoutAsync(ticket);
        var consumed = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => fixture.CreateStore().ConsumeLogoutAsync(ticket.TicketHash)));
        Assert.Single(consumed, x => x != null);
        ticket.TicketHash = CrmOidcCrypto.Hash(CrmOidcCrypto.RandomToken());
        ticket.ExpiresAtUtc = DateTime.UtcNow.AddSeconds(-1);
        await fixture.CreateStore().SaveLogoutAsync(ticket);
        Assert.Null(await fixture.CreateStore().ConsumeLogoutAsync(ticket.TicketHash));
    }

    [Theory]
    [InlineData("access", true)]
    [InlineData("source", true)]
    [InlineData("logout-window", false)]
    public async Task Session_and_logout_reads_keep_their_distinct_expiry_rules(string expiry, bool availableForLogout)
    {
        var grant = Grant();
        var store = fixture.CreateStore();
        await store.SaveAsync(grant);
        Assert.NotNull(await store.ConsumeAsync(grant.CodeHash, grant.ClientId, grant.RedirectUri, grant.Challenge));
        await using var context = fixture.CreateContext(new TenantContext());
        var now = await DatabaseUtcClock.ReadUtcNowAsync(context);
        var expiredAt = expiry == "logout-window" ? now.AddDays(-2) : now.AddMinutes(-1);
        await using var db = fixture.CreateConnection();
        await db.ExecuteAsync($"UPDATE {T("CrmOidcGrant")} SET {C(expiry == "source" ? "SourceExpiresAtUtc" : "AccessExpiresAtUtc")}=@expiredAt WHERE {C("Id")}=@id",
            new { expiredAt, id = grant.Id });
        Assert.Null(await fixture.CreateStore().FindSessionAsync(grant.Id));
        Assert.Equal(availableForLogout, await fixture.CreateStore().FindForLogoutAsync(grant.Id) is not null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Save_cleans_only_one_hundred_expired_rows_and_preserves_eligible_rows(bool logout)
    {
        await using var context = fixture.CreateContext(new TenantContext());
        var now = await DatabaseUtcClock.ReadUtcNowAsync(context);
        await using var db = fixture.CreateConnection();
        var table = logout ? "CrmOidcLogout" : "CrmOidcGrant";
        var expiry = logout ? "ExpiresAtUtc" : "AccessExpiresAtUtc";
        var cutoff = logout ? now : now.AddDays(-1);
        var expired = Enumerable.Range(0, 101).Select(_ => Grant()).ToArray();
        var retained = Grant();
        retained.ExpiresAtUtc = now.AddMinutes(10);
        retained.AccessExpiresAtUtc = now.AddHours(-1); // Expired access still belongs to the logout retention window.
        foreach (var grant in expired)
        {
            grant.ExpiresAtUtc = now.AddDays(-2);
            grant.AccessExpiresAtUtc = now.AddDays(-2);
        }
        var inserted = Grant();
        var rows = expired.Append(retained).Append(inserted).ToArray();
        try
        {
            foreach (var grant in expired.Append(retained))
                await InsertFixtureRowAsync(db, grant, logout);
            var before = await db.QuerySingleAsync<int>($"SELECT COUNT(*) FROM {T(table)} WHERE {C(expiry)}<@cutoff", new { cutoff });
            Assert.True(before >= 101);
            if (logout) await fixture.CreateStore().SaveLogoutAsync(Ticket(inserted));
            else await fixture.CreateStore().SaveAsync(inserted);
            var after = await db.QuerySingleAsync<int>($"SELECT COUNT(*) FROM {T(table)} WHERE {C(expiry)}<@cutoff", new { cutoff });
            Assert.Equal(before - 100, after);
            var key = logout ? "TicketHash" : "CodeHash";
            Assert.Equal(2, await db.QuerySingleAsync<int>($"SELECT COUNT(*) FROM {T(table)} WHERE {C(key)} IN (@retained,@inserted)",
                new { retained = retained.CodeHash, inserted = inserted.CodeHash }));
        }
        finally
        {
            var key = logout ? "TicketHash" : "CodeHash";
            foreach (var row in rows)
                await db.ExecuteAsync($"DELETE FROM {T(table)} WHERE {C(key)}=@hash", new { hash = row.CodeHash });
        }
    }

    private async Task InsertFixtureRowAsync(System.Data.Common.DbConnection db, CrmOidcGrant grant, bool logout)
    {
        if (logout)
        {
            await db.ExecuteAsync($"INSERT INTO {T("CrmOidcLogout")} ({C("TicketHash")},{C("GrantId")},{C("RedirectUri")},{C("ExpiresAtUtc")}) VALUES (@TicketHash,@GrantId,@RedirectUri,@ExpiresAtUtc)", Ticket(grant));
            return;
        }
        string[] columns = ["Id", "CodeHash", "ClientId", "RedirectUri", "Challenge", "Nonce", "SubjectId", "OrganizationId", "SourceJti", "SourceRefreshHash", "SecurityStamp", "ExpiresAtUtc", "SourceExpiresAtUtc", "AccessExpiresAtUtc", "Consumed", "Revoked"];
        await db.ExecuteAsync($"INSERT INTO {T("CrmOidcGrant")} ({string.Join(",", columns.Select(C))}) VALUES ({string.Join(",", columns.Select(x => "@" + x))})", grant);
    }

    private static CrmOidcLogout Ticket(CrmOidcGrant grant) => new()
    {
        TicketHash = grant.CodeHash, GrantId = grant.Id, RedirectUri = grant.RedirectUri, ExpiresAtUtc = grant.ExpiresAtUtc
    };

    private readonly Dictionary<Guid, Guid> _manualFamilies = new();
    private async Task AddRefreshAsync(CrmOidcGrant grant, string hash, string? next, bool revoked, bool sameFamily = true)
    {
        if (!_manualFamilies.TryGetValue(grant.Id, out var familyId)) _manualFamilies[grant.Id] = familyId = Guid.NewGuid();
        if (!sameFamily) familyId = Guid.NewGuid();
        await using var db = fixture.CreateContext(new TenantContext { CurrentTenantId = grant.OrganizationId });
        if (!await db.Sys_BrowserSessions.AnyAsync(x => x.Id == familyId))
            db.Sys_BrowserSessions.Add(new Sys_BrowserSession
            {
                Id = familyId, UserId = grant.SubjectId, TenantId = grant.OrganizationId, AuthenticationVersion = grant.SecurityStamp
            });
        db.Sys_RefreshTokens.Add(new Sys_RefreshToken
        {
            Id = Guid.NewGuid(), UserId = grant.SubjectId, TenantId = grant.OrganizationId, TokenHash = hash,
            BrowserSessionId = familyId, ExpiresAt = DateTime.Now.AddDays(1),
            RevokedAt = revoked ? DateTime.Now : null, ReplacedByTokenHash = next, ClientKind = "Web"
        });
        await db.SaveChangesAsync();
    }

    private string T(string name) => fixture.Table(name);
    private string C(string name) => fixture.Column(name);
    private static CrmOidcGrant Grant() => new()
    {
        Id = Guid.NewGuid(), CodeHash = CrmOidcCrypto.Hash(CrmOidcCrypto.RandomToken()), ClientId = "CP6.Web",
        RedirectUri = "https://crm.example/signin-oidc", Challenge = CrmOidcCrypto.Challenge(CrmOidcCrypto.RandomToken()),
        Nonce = CrmOidcCrypto.RandomToken(), SubjectId = Guid.NewGuid(), OrganizationId = Guid.NewGuid(),
        SourceJti = Guid.NewGuid().ToString(), SecurityStamp = new string('A', 64),
        SourceRefreshHash = CrmOidcCrypto.Hash(CrmOidcCrypto.RandomToken()),
        ExpiresAtUtc = DateTime.UtcNow.AddMinutes(1), AccessExpiresAtUtc = DateTime.UtcNow.AddMinutes(5),
        SourceExpiresAtUtc = DateTime.UtcNow.AddMinutes(10)
    };
}
