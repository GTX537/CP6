using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.Entity.DomainModels.Sys;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

sealed partial class IdentitySqlFixture
{
    private CP6Context CreateHttpContext(Guid tenant, CrmIdentityRuntime runtime, HttpServiceTokenInsertObserver? observer)
    {
        var profile = DatabaseMigrationProfile.For(database, DatabaseContextKind.Core);
        var options = DatabaseContextOptions.Configure(new DbContextOptionsBuilder<CP6Context>(), database,
            connection, profile.MigrationsAssembly, profile.HistoryTable, profile.HistorySchema);
        if (observer is not null) options.AddInterceptors(observer);
        var context = new CP6Context(options.Options, new TenantContext { CurrentTenantId = tenant }, identity: runtime);
        context.Database.SetCommandTimeout(30);
        return context;
    }

    private async Task AssertHttpTokenIndexAsync(string accessToken, string clientId, Guid tenant)
    {
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
        Require(Guid.TryParseExact(jwt.Id, "D", out var jti) && jwt.Id == jti.ToString("D")
            && jwt.Issuer == Issuer && jwt.Subject == "service:" + clientId
            && jwt.Audiences.SequenceEqual(new[] { "CP6.Services" })
            && jwt.Claims.Single(claim => claim.Type == "client_id").Value == clientId
            && jwt.Claims.Single(claim => claim.Type == "tenant_id").Value == tenant.ToString("D")
            && jwt.Claims.Single(claim => claim.Type == "scope").Value == "cp6.services",
            "C02_HTTP_ISSUED_TOKEN_IDENTITY_MISMATCH");
        await using var verify = Create(tenant);
        var indexed = await verify.CrmServiceTokenRecords.AsNoTracking().SingleAsync(record =>
            record.Issuer == Issuer && record.Jti == jwt.Id);
        Require(indexed.Issuer == jwt.Issuer && indexed.Jti == jwt.Id && indexed.ClientId == clientId
            && indexed.TenantId == tenant && indexed.ExpiresAtUtc.UtcDateTime == jwt.ValidTo
            && indexed.RevokedAtUtc is null && indexed.RowVersion.Length == 8,
            "C02_HTTP_TOKEN_RETURNED_WITHOUT_EXACT_NATIVE_INDEX");
    }

    private async Task AssertHttpIssuanceFailureAsync(HttpClient http, Guid tenant,
        AuthenticationHeaderValue authentication, HttpServiceTokenInsertObserver observer)
    {
        var constraint = "C02_HttpInsertReject_" + tenant.ToString("N");
        await using (var before = Create(tenant))
            Require(!await before.CrmServiceTokenRecords.AsNoTracking().AnyAsync(record =>
                record.TenantId == tenant && record.ClientId == "reader-a"), "C02_HTTP_FAULT_CLIENT_WAS_NOT_NEW");
        try
        {
            await AddHttpServiceTokenRejectAsync(tenant, constraint);
            using var request = new HttpRequestMessage(HttpMethod.Post, "/connect/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                { ["grant_type"] = "client_credentials", ["scope"] = "cp6.services" })
            };
            request.Headers.Authorization = authentication;
            using var response = await http.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            Require(response.StatusCode == HttpStatusCode.ServiceUnavailable
                && !body.Contains("access_token", StringComparison.Ordinal), "C02_HTTP_FAILED_NATIVE_INSERT_RETURNED_TOKEN");
            using var json = JsonDocument.Parse(body);
            Require(json.RootElement.GetProperty("error").GetString() == "temporarily_unavailable",
                "C02_HTTP_FAILED_NATIVE_INSERT_ERROR_CONTRACT_CHANGED");
            var failures = observer.Failures.ToArray();
            Require(failures.Length == 1 && failures[0].Tenant == tenant && failures[0].ClientId == "reader-a"
                && Guid.TryParseExact(failures[0].Jti, "D", out _)
                && failures[0].Failure.Kind == DatabaseFailureKind.CheckConstraint
                && failures[0].Failure.MatchesConstraint(constraint)
                && (database.Provider == DatabaseProvider.PostgreSql
                    ? failures[0].Failure.SqlState == "23514" : failures[0].Failure.DatabaseErrorCode == 547),
                "C02_HTTP_503_WAS_NOT_OWN_NATIVE_INSERT_CHECK_FAILURE");
            await using var verify = Create(tenant);
            Require(!await verify.CrmServiceTokenRecords.AsNoTracking().AnyAsync(record =>
                record.TenantId == tenant && record.ClientId == "reader-a"), "C02_HTTP_FAILED_INSERT_LEFT_NATIVE_TOKEN_INDEX");
            var nativeCode = failures[0].Failure.SqlState ?? failures[0].Failure.DatabaseErrorCode?.ToString();
            Console.WriteLine($"OBSERVE C02 HTTP {database.Provider} ServiceToken INSERT rejected by {constraint}; native={nativeCode}; HTTP=503; committedIndex=0.");
        }
        finally { await DropHttpServiceTokenRejectAsync(constraint); }
    }

    private async Task AddHttpServiceTokenRejectAsync(Guid tenant, string constraint)
    {
        ValidateHttpInsertConstraint(constraint);
        Require(constraint == "C02_HttpInsertReject_" + tenant.ToString("N"), "C02_HTTP_FAULT_TENANT_MISMATCH");
        var sql = database.Provider == DatabaseProvider.PostgreSql
            ? $"ALTER TABLE crm_identity.\"ServiceToken\" ADD CONSTRAINT \"{constraint}\" CHECK (\"TenantId\" <> '{tenant:D}'::uuid OR \"ClientId\" <> 'reader-a') NOT VALID;"
            : $"ALTER TABLE crm_identity.ServiceToken WITH NOCHECK ADD CONSTRAINT [{constraint}] CHECK (TenantId <> '{tenant:D}' OR ClientId <> N'reader-a');";
        await using var native = new DatabaseConnectionFactory(database).Create(connection);
        await native.ExecuteAsync(sql, commandTimeout: 30);
    }

    private async Task DropHttpServiceTokenRejectAsync(string constraint)
    {
        ValidateHttpInsertConstraint(constraint);
        await using var native = new DatabaseConnectionFactory(database).Create(connection);
        var query = database.Provider == DatabaseProvider.PostgreSql ? """
            SELECT COUNT(*) FROM pg_catalog.pg_constraint c
            JOIN pg_catalog.pg_class t ON t.oid=c.conrelid
            JOIN pg_catalog.pg_namespace n ON n.oid=t.relnamespace
            WHERE n.nspname='crm_identity' AND t.relname='ServiceToken' AND c.conname=@constraint
            """ : """
            SELECT COUNT(*) FROM sys.check_constraints
            WHERE parent_object_id=OBJECT_ID(N'crm_identity.ServiceToken') AND name=@constraint
            """;
        var count = await native.QuerySingleAsync<int>(query, new { constraint }, commandTimeout: 30);
        Require(count is 0 or 1, "C02_HTTP_FAULT_CONSTRAINT_IDENTITY_AMBIGUOUS");
        if (count == 1)
            await native.ExecuteAsync(database.Provider == DatabaseProvider.PostgreSql
                ? $"ALTER TABLE crm_identity.\"ServiceToken\" DROP CONSTRAINT \"{constraint}\";"
                : $"ALTER TABLE crm_identity.ServiceToken DROP CONSTRAINT [{constraint}];", commandTimeout: 30);
        Require(await native.QuerySingleAsync<int>(query, new { constraint }, commandTimeout: 30) == 0,
            "C02_HTTP_NATIVE_INSERT_CONSTRAINT_NOT_REMOVED");
        Console.WriteLine("OBSERVE C02 HTTP exact native INSERT fault constraint removed; remaining=0.");
    }

    private static void ValidateHttpInsertConstraint(string constraint)
        => Require(Regex.IsMatch(constraint, "\\AC02_HttpInsertReject_[0-9a-f]{32}\\z", RegexOptions.CultureInvariant),
            "C02_HTTP_INVALID_INSERT_CONSTRAINT");

    private sealed record HttpNativeInsertFailure(Guid Tenant, string ClientId, string Jti, DatabaseFailure Failure);

    // Observe the actual EF failure without suppressing it or replacing the production store.
    private sealed class HttpServiceTokenInsertObserver(Guid tenant, string clientId) : SaveChangesInterceptor
    {
        public ConcurrentQueue<HttpNativeInsertFailure> Failures { get; } = new();

        public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            var row = eventData.Context?.ChangeTracker.Entries<CrmServiceTokenRecord>().SingleOrDefault(entry =>
                entry.State == EntityState.Added && entry.Entity.TenantId == tenant && entry.Entity.ClientId == clientId);
            if (row is not null)
                Failures.Enqueue(new(tenant, clientId, row.Entity.Jti, DatabaseFailureClassifier.Classify(eventData.Exception)));
            return Task.CompletedTask;
        }
    }
}
