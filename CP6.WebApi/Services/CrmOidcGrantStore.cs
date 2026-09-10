using Dapper;
using System.Data;
using Microsoft.Data.SqlClient;
using CP6.Core.EFDbContext;
using CP6.Core.Services.CrmIdentity;
using Microsoft.EntityFrameworkCore;
using System.Data.Common;

namespace CP6.WebApi.Services;

public sealed class CrmOidcGrant
{
    public Guid Id { get; set; }
    public string CodeHash { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string RedirectUri { get; set; } = "";
    public string Challenge { get; set; } = "";
    public string Nonce { get; set; } = "";
    public Guid SubjectId { get; set; }
    public Guid OrganizationId { get; set; }
    public string SourceJti { get; set; } = "";
    public string SourceRefreshHash { get; set; } = "";
    public string SecurityStamp { get; set; } = "";
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime SourceExpiresAtUtc { get; set; }
    public DateTime AccessExpiresAtUtc { get; set; }
    public bool Consumed { get; set; }
    public bool Revoked { get; set; }
}

public sealed class CrmOidcLogout
{
    public string TicketHash { get; set; } = "";
    public Guid GrantId { get; set; }
    public string RedirectUri { get; set; } = "";
    public DateTime ExpiresAtUtc { get; set; }
}

public interface ICrmOidcGrantStore
{
    Task SaveAsync(CrmOidcGrant grant);
    Task<CrmOidcGrant?> ConsumeAsync(string hash, string clientId, string redirectUri, string challenge);
    Task<CrmOidcGrant?> FindSessionAsync(Guid id);
    Task RevokeAsync(Guid id);
    Task<CrmOidcGrant?> FindForLogoutAsync(Guid id);
    Task RevokeSourceFamilyAsync(CrmOidcGrant grant);
    Task SaveLogoutAsync(CrmOidcLogout ticket);
    Task<CrmOidcLogout?> ConsumeLogoutAsync(string hash);
}

/// <summary>Durable cross-replica one-use codes. All bindings participate in the atomic SQL update.</summary>
public sealed class SqlCrmOidcGrantStore(string connectionString, CrmIdentityRuntime? identity = null) : ICrmOidcGrantStore
{
    public async Task SaveAsync(CrmOidcGrant grant)
    {
        await using var db = new SqlConnection(connectionString);
        await db.ExecuteAsync("""
            DELETE TOP(100) FROM dbo.CrmOidcGrant WHERE AccessExpiresAtUtc<DATEADD(day,-1,SYSUTCDATETIME());
            INSERT INTO dbo.CrmOidcGrant
              (Id,CodeHash,ClientId,RedirectUri,Challenge,Nonce,SubjectId,OrganizationId,SourceJti,SourceRefreshHash,SecurityStamp,
               ExpiresAtUtc,SourceExpiresAtUtc,AccessExpiresAtUtc,Consumed,Revoked)
            VALUES (@Id,@CodeHash,@ClientId,@RedirectUri,@Challenge,@Nonce,@SubjectId,@OrganizationId,@SourceJti,@SourceRefreshHash,@SecurityStamp,
               @ExpiresAtUtc,@SourceExpiresAtUtc,@AccessExpiresAtUtc,0,0)
            """, grant);
    }
    public async Task<CrmOidcGrant?> ConsumeAsync(string hash, string clientId, string redirectUri, string challenge)
    {
        await using var db = new SqlConnection(connectionString);
        return await db.QuerySingleOrDefaultAsync<CrmOidcGrant>("""
            UPDATE dbo.CrmOidcGrant SET Consumed=1
            OUTPUT inserted.*
            WHERE CodeHash=@hash AND ClientId=@clientId AND RedirectUri=@redirectUri AND Challenge=@challenge
              AND DATALENGTH(RedirectUri)=DATALENGTH(@redirectUri)
              AND Consumed=0 AND Revoked=0 AND ExpiresAtUtc>SYSUTCDATETIME() AND SourceExpiresAtUtc>SYSUTCDATETIME()
            """, new { hash, clientId, redirectUri, challenge });
    }
    public async Task<CrmOidcGrant?> FindSessionAsync(Guid id)
    {
        await using var db = new SqlConnection(connectionString);
        return await db.QuerySingleOrDefaultAsync<CrmOidcGrant>("""
            SELECT * FROM dbo.CrmOidcGrant WHERE Id=@id AND Consumed=1 AND Revoked=0
              AND AccessExpiresAtUtc>SYSUTCDATETIME() AND SourceExpiresAtUtc>SYSUTCDATETIME()
            """, new { id });
    }
    public async Task RevokeAsync(Guid id)
    {
        await using var db = new SqlConnection(connectionString);
        await db.OpenAsync();
        await using var transaction = await db.BeginTransactionAsync();
        var grants = await db.QueryAsync<CrmOidcGrant>("UPDATE dbo.CrmOidcGrant SET Revoked=1 OUTPUT inserted.* WHERE Id=@id AND Revoked=0", new { id }, transaction);
        await AppendRevocationsAsync(db, transaction, grants);
        await transaction.CommitAsync();
    }

    public async Task<CrmOidcGrant?> FindForLogoutAsync(Guid id)
    {
        await using var db = new SqlConnection(connectionString);
        return await db.QuerySingleOrDefaultAsync<CrmOidcGrant>(
            "SELECT * FROM dbo.CrmOidcGrant WHERE Id=@id AND Consumed=1 AND AccessExpiresAtUtc>DATEADD(day,-1,SYSUTCDATETIME())", new { id });
    }

    public async Task RevokeSourceFamilyAsync(CrmOidcGrant grant)
    {
        await using var db = new SqlConnection(connectionString);
        await db.OpenAsync();
        // The token-to-family association is immutable. Resolve it before taking the family lock, so
        // rotation and logout always lock the family before locking any refresh-token rows.
        var familyId = await db.QuerySingleOrDefaultAsync<Guid?>("""
            SELECT BrowserSessionId FROM dbo.Sys_RefreshTokens
            WHERE TokenHash=@SourceRefreshHash AND UserId=@SubjectId AND TenantId=@OrganizationId AND ClientKind=N'Web'
            """, grant);
        if (familyId == null) throw new InvalidOperationException("Missing original browser authentication family.");
        await using var transaction = await db.BeginTransactionAsync(IsolationLevel.Serializable);
        var boundFamily = await db.QuerySingleOrDefaultAsync<Guid?>("""
            SELECT Id FROM dbo.Sys_BrowserSessions WITH (UPDLOCK,HOLDLOCK)
            WHERE Id=@familyId AND UserId=@SubjectId AND TenantId=@OrganizationId
            """, new { familyId, grant.SubjectId, grant.OrganizationId }, transaction);
        if (boundFamily == null) throw new InvalidOperationException("Invalid original browser authentication family.");
        var revoked = await db.QueryAsync<CrmOidcGrant>("""
            UPDATE dbo.Sys_BrowserSessions SET LoggedOutAtUtc=COALESCE(LoggedOutAtUtc,SYSUTCDATETIME()) WHERE Id=@familyId;
            UPDATE dbo.Sys_RefreshTokens SET RevokedAt=COALESCE(RevokedAt,GETDATE())
            WHERE BrowserSessionId=@familyId AND UserId=@SubjectId AND TenantId=@OrganizationId;
            UPDATE g SET Revoked=1 OUTPUT inserted.* FROM dbo.CrmOidcGrant g
            INNER JOIN dbo.Sys_RefreshTokens r ON g.SourceRefreshHash=r.TokenHash COLLATE Latin1_General_100_BIN2
            WHERE r.BrowserSessionId=@familyId AND r.UserId=@SubjectId AND r.TenantId=@OrganizationId
                AND g.SubjectId=@SubjectId AND g.OrganizationId=@OrganizationId AND g.Revoked=0;
            """, new { familyId, grant.SubjectId, grant.OrganizationId }, transaction);
        await AppendRevocationsAsync(db, transaction, revoked);
        await transaction.CommitAsync();
    }

    private async Task AppendRevocationsAsync(SqlConnection connection, DbTransaction transaction, IEnumerable<CrmOidcGrant> revoked)
    {
        if (identity is null) return;
        await using var context = new CP6Context(new DbContextOptionsBuilder<CP6Context>().UseSqlServer(connection).Options);
        await context.Database.UseTransactionAsync(transaction);
        var writer = new IdentitySnapshotWriter(context, identity);
        foreach (var grant in revoked.OrderBy(x => x.OrganizationId).ThenBy(x => x.Id))
            await writer.RevokeTokenAsync(grant.OrganizationId, grant.Id.ToString("D"), $"user:{grant.SubjectId:D}",
                new DateTimeOffset(DateTime.SpecifyKind(grant.AccessExpiresAtUtc, DateTimeKind.Utc)));
        await context.SaveChangesAsync();
    }

    public async Task SaveLogoutAsync(CrmOidcLogout ticket)
    {
        await using var db = new SqlConnection(connectionString);
        await db.ExecuteAsync("""
            DELETE TOP(100) FROM dbo.CrmOidcLogout WHERE ExpiresAtUtc<SYSUTCDATETIME();
            INSERT INTO dbo.CrmOidcLogout(TicketHash,GrantId,RedirectUri,ExpiresAtUtc)
            VALUES(@TicketHash,@GrantId,@RedirectUri,@ExpiresAtUtc)
            """, ticket);
    }

    public async Task<CrmOidcLogout?> ConsumeLogoutAsync(string hash)
    {
        await using var db = new SqlConnection(connectionString);
        return await db.QuerySingleOrDefaultAsync<CrmOidcLogout>("""
            DELETE FROM dbo.CrmOidcLogout OUTPUT deleted.* WHERE TicketHash=@hash AND ExpiresAtUtc>SYSUTCDATETIME()
            """, new { hash });
    }

}
