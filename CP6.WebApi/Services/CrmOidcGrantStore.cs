using Dapper;
using System.Data;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Services.CrmIdentity;
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
public sealed class SqlCrmOidcGrantStore(DatabaseOptions database, string connectionString, CrmIdentityRuntime? identity = null) : ICrmOidcGrantStore
{
    private readonly DatabaseOptions _database = database ?? throw new ArgumentNullException(nameof(database));
    private readonly DatabaseConnectionFactory _connections = new(database);
    private bool IsPostgreSql => _database.Provider == DatabaseProvider.PostgreSql;

    public SqlCrmOidcGrantStore(string connectionString, CrmIdentityRuntime? identity = null)
        : this(new(DatabaseProvider.SqlServer), connectionString, identity) { }

    public async Task SaveAsync(CrmOidcGrant grant)
    {
        await using var db = _connections.Create(connectionString);
        await db.ExecuteAsync(IsPostgreSql ? """
            DELETE FROM public."CrmOidcGrant" WHERE "Id" IN (
              SELECT "Id" FROM public."CrmOidcGrant"
              WHERE "AccessExpiresAtUtc"<clock_timestamp()-INTERVAL '24 hours' LIMIT 100);
            INSERT INTO public."CrmOidcGrant"
              ("Id","CodeHash","ClientId","RedirectUri","Challenge","Nonce","SubjectId","OrganizationId","SourceJti","SourceRefreshHash","SecurityStamp",
               "ExpiresAtUtc","SourceExpiresAtUtc","AccessExpiresAtUtc","Consumed","Revoked")
            VALUES (@Id,@CodeHash,@ClientId,@RedirectUri,@Challenge,@Nonce,@SubjectId,@OrganizationId,@SourceJti,@SourceRefreshHash,@SecurityStamp,
               @ExpiresAtUtc,@SourceExpiresAtUtc,@AccessExpiresAtUtc,false,false)
            """ : """
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
        await using var db = _connections.Create(connectionString);
        // PostgreSQL bpchar equality has SQL's padded comparison semantics. Only the redirect's
        // existing DATALENGTH contract needs its raw stored text, including any trailing spaces.
        return await db.QuerySingleOrDefaultAsync<CrmOidcGrant>(IsPostgreSql ? """
            UPDATE public."CrmOidcGrant" SET "Consumed"=true
            WHERE "CodeHash"=CAST(@hash AS bpchar) AND "ClientId"=CAST(@clientId AS bpchar)
              AND convert_from(pg_catalog.bpcharsend("RedirectUri"),'UTF8') COLLATE "C"=CAST(@redirectUri AS text) COLLATE "C"
              AND "Challenge"=CAST(@challenge AS bpchar)
              AND NOT "Consumed" AND NOT "Revoked"
              AND "ExpiresAtUtc">clock_timestamp() AND "SourceExpiresAtUtc">clock_timestamp()
            RETURNING *
            """ : """
            UPDATE dbo.CrmOidcGrant SET Consumed=1
            OUTPUT inserted.*
            WHERE CodeHash=@hash AND ClientId=@clientId AND RedirectUri=@redirectUri AND Challenge=@challenge
              AND DATALENGTH(RedirectUri)=DATALENGTH(@redirectUri)
              AND Consumed=0 AND Revoked=0 AND ExpiresAtUtc>SYSUTCDATETIME() AND SourceExpiresAtUtc>SYSUTCDATETIME()
            """, new { hash, clientId, redirectUri, challenge });
    }
    public async Task<CrmOidcGrant?> FindSessionAsync(Guid id)
    {
        await using var db = _connections.Create(connectionString);
        return await db.QuerySingleOrDefaultAsync<CrmOidcGrant>(IsPostgreSql ? """
            SELECT * FROM public."CrmOidcGrant" WHERE "Id"=@id AND "Consumed" AND NOT "Revoked"
              AND "AccessExpiresAtUtc">clock_timestamp() AND "SourceExpiresAtUtc">clock_timestamp()
            """ : """
            SELECT * FROM dbo.CrmOidcGrant WHERE Id=@id AND Consumed=1 AND Revoked=0
              AND AccessExpiresAtUtc>SYSUTCDATETIME() AND SourceExpiresAtUtc>SYSUTCDATETIME()
            """, new { id });
    }
    public async Task RevokeAsync(Guid id)
    {
        await using var db = _connections.Create(connectionString);
        await db.OpenAsync();
        await using var transaction = await db.BeginTransactionAsync();
        var grants = await db.QueryAsync<CrmOidcGrant>(IsPostgreSql
            ? "UPDATE public.\"CrmOidcGrant\" SET \"Revoked\"=true WHERE \"Id\"=@id AND NOT \"Revoked\" RETURNING *"
            : "UPDATE dbo.CrmOidcGrant SET Revoked=1 OUTPUT inserted.* WHERE Id=@id AND Revoked=0", new { id }, transaction);
        await AppendRevocationsAsync(db, transaction, grants);
        await transaction.CommitAsync();
    }

    public async Task<CrmOidcGrant?> FindForLogoutAsync(Guid id)
    {
        await using var db = _connections.Create(connectionString);
        return await db.QuerySingleOrDefaultAsync<CrmOidcGrant>(
            IsPostgreSql
                ? "SELECT * FROM public.\"CrmOidcGrant\" WHERE \"Id\"=@id AND \"Consumed\" AND \"AccessExpiresAtUtc\">clock_timestamp()-INTERVAL '24 hours'"
                : "SELECT * FROM dbo.CrmOidcGrant WHERE Id=@id AND Consumed=1 AND AccessExpiresAtUtc>DATEADD(day,-1,SYSUTCDATETIME())", new { id });
    }

    public async Task RevokeSourceFamilyAsync(CrmOidcGrant grant)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await RevokeSourceFamilyOnceAsync(grant);
                return;
            }
            catch (Exception exception) when (IsPostgreSql && attempt < 2
                && DatabaseFailureClassifier.Classify(exception).CanRetryTransaction)
            {
                // A Serializable row-lock wait can retain an older PostgreSQL snapshot. The owned
                // helper has fully rolled back and disposed its transaction/child context/connection;
                // repeat the complete DB operation on a new connection, never a statement in that TX.
            }
        }
    }

    private async Task RevokeSourceFamilyOnceAsync(CrmOidcGrant grant)
    {
        await using var db = _connections.Create(connectionString);
        await db.OpenAsync();
        // The token-to-family association is immutable. Resolve it before taking the family lock, so
        // rotation and logout always lock the family before locking any refresh-token rows.
        var familyId = await db.QuerySingleOrDefaultAsync<Guid?>(IsPostgreSql ? """
            SELECT "BrowserSessionId" FROM public."Sys_RefreshTokens"
            WHERE "TokenHash"=CAST(@SourceRefreshHash AS bpchar) AND "UserId"=@SubjectId
              AND "TenantId"=@OrganizationId AND "ClientKind"=CAST('Web' AS bpchar)
            """ : """
            SELECT BrowserSessionId FROM dbo.Sys_RefreshTokens
            WHERE TokenHash=@SourceRefreshHash AND UserId=@SubjectId AND TenantId=@OrganizationId AND ClientKind=N'Web'
            """, grant);
        if (familyId == null) throw new InvalidOperationException("Missing original browser authentication family.");
        await using var transaction = await db.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var boundFamily = await db.QuerySingleOrDefaultAsync<Guid?>(IsPostgreSql ? """
                SELECT "Id" FROM public."Sys_BrowserSessions"
                WHERE "Id"=@familyId AND "UserId"=@SubjectId AND "TenantId"=@OrganizationId
                FOR UPDATE
                """ : """
                SELECT Id FROM dbo.Sys_BrowserSessions WITH (UPDLOCK,HOLDLOCK)
                WHERE Id=@familyId AND UserId=@SubjectId AND TenantId=@OrganizationId
                """, new { familyId, grant.SubjectId, grant.OrganizationId }, transaction);
            if (boundFamily == null) throw new InvalidOperationException("Invalid original browser authentication family.");
            if (IsPostgreSql)
                await db.ExecuteAsync("""
                UPDATE public."Sys_BrowserSessions"
                SET "LoggedOutAtUtc"=COALESCE("LoggedOutAtUtc",clock_timestamp()) WHERE "Id"=@familyId;
                UPDATE public."Sys_RefreshTokens" SET "RevokedAt"=COALESCE("RevokedAt",clock_timestamp()::timestamp)
                WHERE "BrowserSessionId"=@familyId AND "UserId"=@SubjectId AND "TenantId"=@OrganizationId;
                """, new { familyId, grant.SubjectId, grant.OrganizationId }, transaction);
            var revoked = await db.QueryAsync<CrmOidcGrant>(IsPostgreSql ? """
                UPDATE public."CrmOidcGrant" g SET "Revoked"=true
                FROM public."Sys_RefreshTokens" r
                WHERE g."SourceRefreshHash"=r."TokenHash" COLLATE "C"
                    AND r."BrowserSessionId"=@familyId AND r."UserId"=@SubjectId AND r."TenantId"=@OrganizationId
                    AND g."SubjectId"=@SubjectId AND g."OrganizationId"=@OrganizationId AND NOT g."Revoked"
                RETURNING g.*;
                """ : """
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
        catch when (IsPostgreSql)
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private async Task AppendRevocationsAsync(DbConnection connection, DbTransaction transaction, IEnumerable<CrmOidcGrant> revoked)
    {
        if (identity is null) return;
        await using var context = await DatabaseSharedContext.CreateAsync<CP6Context>(_database,
            connection, transaction, DatabaseContextKind.Core, options => new(options));
        var writer = new IdentitySnapshotWriter(context, identity);
        foreach (var grant in revoked.OrderBy(x => x.OrganizationId).ThenBy(x => x.Id))
            await writer.RevokeTokenAsync(grant.OrganizationId, grant.Id.ToString("D"), $"user:{grant.SubjectId:D}",
                new DateTimeOffset(DateTime.SpecifyKind(grant.AccessExpiresAtUtc, DateTimeKind.Utc)));
        await context.SaveChangesAsync();
    }

    public async Task SaveLogoutAsync(CrmOidcLogout ticket)
    {
        await using var db = _connections.Create(connectionString);
        await db.ExecuteAsync(IsPostgreSql ? """
            DELETE FROM public."CrmOidcLogout" WHERE "TicketHash" IN (
              SELECT "TicketHash" FROM public."CrmOidcLogout" WHERE "ExpiresAtUtc"<clock_timestamp() LIMIT 100);
            INSERT INTO public."CrmOidcLogout"("TicketHash","GrantId","RedirectUri","ExpiresAtUtc")
            VALUES(@TicketHash,@GrantId,@RedirectUri,@ExpiresAtUtc)
            """ : """
            DELETE TOP(100) FROM dbo.CrmOidcLogout WHERE ExpiresAtUtc<SYSUTCDATETIME();
            INSERT INTO dbo.CrmOidcLogout(TicketHash,GrantId,RedirectUri,ExpiresAtUtc)
            VALUES(@TicketHash,@GrantId,@RedirectUri,@ExpiresAtUtc)
            """, ticket);
    }

    public async Task<CrmOidcLogout?> ConsumeLogoutAsync(string hash)
    {
        await using var db = _connections.Create(connectionString);
        return await db.QuerySingleOrDefaultAsync<CrmOidcLogout>(IsPostgreSql ? """
            DELETE FROM public."CrmOidcLogout"
            WHERE "TicketHash"=CAST(@hash AS bpchar) AND "ExpiresAtUtc">clock_timestamp()
            RETURNING *
            """ : """
            DELETE FROM dbo.CrmOidcLogout OUTPUT deleted.* WHERE TicketHash=@hash AND ExpiresAtUtc>SYSUTCDATETIME()
            """, new { hash });
    }

}
