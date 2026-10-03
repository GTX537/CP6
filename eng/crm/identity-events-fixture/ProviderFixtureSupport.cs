using System.Text.Json;
using System.Text.RegularExpressions;
using CP6.Core.Persistence;
using CP6.Core.Services.CrmIdentity;
using CP6.WebApi.Services;
using Dapper;
using Microsoft.EntityFrameworkCore;

sealed partial class IdentitySqlFixture
{
    private async Task<T> ProviderScalarAsync<T>(string sql, object? args)
    {
        // This is an explicit catalogue of the existing fixture's observations, not a SQL translator.
        var query = sql switch
        {
            "SELECT MAX(Version) FROM crm_identity.Snapshot WHERE TenantId=@tenant" =>
                "SELECT MAX(\"Version\") FROM crm_identity.\"Snapshot\" WHERE \"TenantId\"=@tenant",
            "SELECT ScopeType FROM dbo.Sys_RoleDataScope WHERE TenantId=@tenant" =>
                "SELECT \"ScopeType\" FROM public.\"Sys_RoleDataScope\" WHERE \"TenantId\"=@tenant",
            "SELECT Enable FROM dbo.Sys_Users WHERE TenantId=@tenant" =>
                "SELECT \"Enable\" FROM public.\"Sys_Users\" WHERE \"TenantId\"=@tenant",
            "SELECT ManagerId FROM dbo.Sys_Users WHERE TenantId=@tenant" =>
                "SELECT \"ManagerId\" FROM public.\"Sys_Users\" WHERE \"TenantId\"=@tenant",
            "SELECT Version FROM crm_identity.Snapshot WHERE TenantId=@tenant AND AggregateId LIKE N'user:%'" =>
                "SELECT \"Version\" FROM crm_identity.\"Snapshot\" WHERE \"TenantId\"=@tenant AND \"AggregateId\" LIKE 'user:%'",
            "SELECT Revoked FROM dbo.CrmOidcGrant WHERE Id=@Id" =>
                "SELECT \"Revoked\" FROM public.\"CrmOidcGrant\" WHERE \"Id\"=@Id",
            "SELECT PayloadJson FROM crm_identity.Snapshot WHERE TenantId=@tenant AND AggregateId=@aggregate" =>
                "SELECT \"PayloadJson\" FROM crm_identity.\"Snapshot\" WHERE \"TenantId\"=@tenant AND \"AggregateId\"=CAST(@aggregate AS bpchar)",
            "SELECT Id FROM dbo.Sys_Users WHERE TenantId=@tenant" =>
                "SELECT \"Id\" FROM public.\"Sys_Users\" WHERE \"TenantId\"=@tenant",
            "SELECT COUNT(*) FROM crm_identity.Cp6_OutboxMessage WHERE TenantId=@tenant" =>
                "SELECT COUNT(*) FROM crm_identity.\"Cp6_OutboxMessage\" WHERE \"TenantId\"=@tenant",
            "SELECT COUNT(*) FROM crm_identity_priority.Cp6_OutboxMessage WHERE TenantId=@tenant" =>
                "SELECT COUNT(*) FROM crm_identity_priority.\"Cp6_OutboxMessage\" WHERE \"TenantId\"=@tenant",
            "SELECT COUNT(*) FROM dbo.Sys_BrowserSessions WHERE Id=@family AND LoggedOutAtUtc IS NOT NULL" =>
                "SELECT COUNT(*) FROM public.\"Sys_BrowserSessions\" WHERE \"Id\"=@family AND \"LoggedOutAtUtc\" IS NOT NULL",
            "SELECT COUNT(*) FROM dbo.Sys_RefreshTokens WHERE BrowserSessionId=@family AND RevokedAt IS NOT NULL" =>
                "SELECT COUNT(*) FROM public.\"Sys_RefreshTokens\" WHERE \"BrowserSessionId\"=@family AND \"RevokedAt\" IS NOT NULL",
            "SELECT COUNT(*) FROM crm_identity.ServiceToken WHERE TenantId=@tenant AND RevokedAtUtc IS NOT NULL" =>
                "SELECT COUNT(*) FROM crm_identity.\"ServiceToken\" WHERE \"TenantId\"=@tenant AND \"RevokedAtUtc\" IS NOT NULL",
            "SELECT PayloadJson FROM crm_identity.Snapshot WHERE TenantId=@tenant AND AggregateId LIKE N'user:%'" =>
                "SELECT \"PayloadJson\" FROM crm_identity.\"Snapshot\" WHERE \"TenantId\"=@tenant AND \"AggregateId\" LIKE 'user:%'",
            _ => throw new InvalidOperationException("C02_UNMAPPED_PROVIDER_FIXTURE_OBSERVATION")
        };
        await using var native = new DatabaseConnectionFactory(database).Create(connection);
        return await native.QuerySingleAsync<T>(query, args);
    }

    private async Task ProviderExecuteAsync(string sql)
    {
        // Only the original outbox CHECK fault fixtures are mapped. SQL-only historical DDL is rejected.
        var add = Regex.Match(sql,
            "\\AALTER TABLE (crm_identity(?:_priority)?)\\.Cp6_OutboxMessage WITH NOCHECK ADD CONSTRAINT (C02_FixtureRejectOutbox|C02_FamilyReject) CHECK \\(TenantId <> '([a-f0-9-]{36})'\\);\\z",
            RegexOptions.CultureInvariant);
        if (add.Success && Guid.TryParseExact(add.Groups[3].Value, "D", out var tenant))
        {
            await AddOutboxRejectAsync(tenant, add.Groups[1].Value == "crm_identity_priority", add.Groups[2].Value);
            return;
        }
        var drop = Regex.Match(sql,
            "\\AALTER TABLE (crm_identity(?:_priority)?)\\.Cp6_OutboxMessage DROP CONSTRAINT (C02_FixtureRejectOutbox|C02_FamilyReject);?\\z",
            RegexOptions.CultureInvariant);
        if (drop.Success)
        {
            await DropOutboxRejectAsync(drop.Groups[1].Value == "crm_identity_priority", drop.Groups[2].Value);
            return;
        }
        throw new InvalidOperationException("C02_UNMAPPED_PROVIDER_FIXTURE_COMMAND");
    }

    private async Task AddOutboxRejectAsync(Guid tenant, bool priority, string constraint)
    {
        ValidateFixtureConstraint(constraint);
        var schema = priority ? "crm_identity_priority" : "crm_identity";
        var sql = database.Provider == DatabaseProvider.PostgreSql
            ? $"ALTER TABLE {schema}.\"Cp6_OutboxMessage\" ADD CONSTRAINT \"{constraint}\" CHECK (\"TenantId\" <> '{tenant:D}'::uuid) NOT VALID;"
            : $"ALTER TABLE {schema}.Cp6_OutboxMessage WITH NOCHECK ADD CONSTRAINT [{constraint}] CHECK (TenantId <> '{tenant:D}');";
        await using var native = new DatabaseConnectionFactory(database).Create(connection);
        await native.ExecuteAsync(sql);
    }

    private async Task DropOutboxRejectAsync(bool priority, string constraint)
    {
        ValidateFixtureConstraint(constraint);
        var schema = priority ? "crm_identity_priority" : "crm_identity";
        await using var native = new DatabaseConnectionFactory(database).Create(connection);
        await native.ExecuteAsync(database.Provider == DatabaseProvider.PostgreSql
            ? $"ALTER TABLE {schema}.\"Cp6_OutboxMessage\" DROP CONSTRAINT \"{constraint}\";"
            : $"ALTER TABLE {schema}.Cp6_OutboxMessage DROP CONSTRAINT [{constraint}];");
        var count = await native.QuerySingleAsync<int>(database.Provider == DatabaseProvider.PostgreSql ? """
            SELECT COUNT(*) FROM pg_catalog.pg_constraint c
            JOIN pg_catalog.pg_class t ON t.oid=c.conrelid
            JOIN pg_catalog.pg_namespace n ON n.oid=t.relnamespace
            WHERE n.nspname=@schema AND t.relname='Cp6_OutboxMessage' AND c.conname=@constraint
            """ : """
            SELECT COUNT(*) FROM sys.check_constraints
            WHERE parent_object_id=OBJECT_ID(@table) AND name=@constraint
            """, new { schema, constraint, table = schema + ".Cp6_OutboxMessage" });
        Require(count == 0, "C02_PROVIDER_FAULT_CONSTRAINT_NOT_REMOVED");
    }

    private static void ValidateFixtureConstraint(string constraint)
        => Require(Regex.IsMatch(constraint, "\\AC02_[A-Za-z0-9_]{1,45}\\z", RegexOptions.CultureInvariant),
            "C02_INVALID_FIXTURE_CONSTRAINT");

    private async Task ExpectProviderCheckFailureAsync(Func<Task> run, string constraint)
    {
        try { await run(); }
        catch (Exception exception)
        {
            var failure = DatabaseFailureClassifier.Classify(exception);
            Require(failure.Kind == DatabaseFailureKind.CheckConstraint && failure.MatchesConstraint(constraint)
                && (database.Provider == DatabaseProvider.PostgreSql ? failure.SqlState == "23514" : failure.DatabaseErrorCode == 547),
                "C02_EXPECTED_NATIVE_CHECK_FAILURE");
            return;
        }
        throw new InvalidOperationException("C02_EXPECTED_NATIVE_CHECK_NOT_RAISED");
    }

    public async Task ProviderDirectRevocationAsync()
    {
        var tenant = await SeedAsync();
        var grant = await GrantAsync(tenant);
        var store = new SqlCrmOidcGrantStore(database, connection, Runtime(tenant));
        const string constraint = "C02_DirectGrantReject";
        await AddOutboxRejectAsync(tenant, true, constraint);
        try
        {
            await ExpectProviderCheckFailureAsync(() => store.RevokeAsync(grant.Id), constraint);
            Require(!await Scalar<bool>("SELECT Revoked FROM dbo.CrmOidcGrant WHERE Id=@Id", grant), "failed direct revoke committed its grant");
            await using var read = Create(tenant);
            var aggregate = IdentityEventContracts.TokenAggregate(Issuer, grant.Id.ToString("D"));
            Require(!await read.CrmIdentitySnapshots.AnyAsync(x => x.TenantId == tenant && x.AggregateId == aggregate)
                && await CountOutbox(tenant, true) == 0, "failed direct revoke committed a snapshot or priority message");
        }
        finally { await DropOutboxRejectAsync(true, constraint); }
        await store.RevokeAsync(grant.Id);
        await store.RevokeAsync(grant.Id);
        Require(await CountOutbox(tenant, true) == 1, "direct repeated revoke was not idempotent");
        Require(await Scalar<bool>("SELECT Revoked FROM dbo.CrmOidcGrant WHERE Id=@Id", grant), "direct grant update missing");
    }

    public async Task ProviderDirectFamilyAsync()
    {
        var tenant = await SeedAsync();
        var grant = await GrantAsync(tenant);
        var family = await BindFamilyAsync(tenant, grant);
        var store = new SqlCrmOidcGrantStore(database, connection, Runtime(tenant));
        const string constraint = "C02_FamilyReject";
        await AddOutboxRejectAsync(tenant, true, constraint);
        try
        {
            await ExpectProviderCheckFailureAsync(() => store.RevokeSourceFamilyAsync(grant), constraint);
            Require(!await Scalar<bool>("SELECT Revoked FROM dbo.CrmOidcGrant WHERE Id=@Id", grant), "failed family command committed grant revocation");
            Require(await Scalar<int>("SELECT COUNT(*) FROM dbo.Sys_BrowserSessions WHERE Id=@family AND LoggedOutAtUtc IS NOT NULL", new { family }) == 0,
                "failed family command committed logout");
            Require(await Scalar<int>("SELECT COUNT(*) FROM dbo.Sys_RefreshTokens WHERE BrowserSessionId=@family AND RevokedAt IS NOT NULL", new { family }) == 0,
                "failed family command committed refresh revocation");
            await using var read = Create(tenant);
            var aggregate = IdentityEventContracts.TokenAggregate(Issuer, grant.Id.ToString("D"));
            Require(!await read.CrmIdentitySnapshots.AnyAsync(x => x.TenantId == tenant && x.AggregateId == aggregate),
                "failed family command committed token snapshot");
            Require(await CountOutbox(tenant, true) == 0, "failed family command committed priority message");
        }
        finally { await DropOutboxRejectAsync(true, constraint); }
        await store.RevokeSourceFamilyAsync(grant);
        await store.RevokeSourceFamilyAsync(grant);
        Require(await CountOutbox(tenant, true) == 1
            && await Scalar<int>("SELECT COUNT(*) FROM dbo.Sys_RefreshTokens WHERE BrowserSessionId=@family AND RevokedAt IS NOT NULL", new { family }) == 1,
            "successful family command did not commit exactly once");
    }

    public async Task ProviderExternalTransactionRollbackAsync()
    {
        foreach (var asynchronous in new[] { false, true })
        {
            var tenant = await SeedAsync();
            await using var db = Create(tenant);
            await using var transaction = await db.Database.BeginTransactionAsync();
            (await db.Sys_RoleDataScopes.SingleAsync()).ScopeType = 6;
            await ExpectFailure(() => asynchronous ? db.SaveChangesAsync() : Task.FromResult(db.SaveChanges()), "C02_IDENTITY_CONTRACT_INVALID");
            await CommitOtherCallerWorkAsync(db, tenant, asynchronous);
            await transaction.CommitAsync();
            Require(await Scalar<int>("SELECT ScopeType FROM dbo.Sys_RoleDataScope WHERE TenantId=@tenant", new { tenant }) == 2
                && await CountOutbox(tenant) == 5, "failed identity command escaped caller-owned transaction rollback");
            await AssertOtherCallerWorkAsync(tenant);
        }
        // A real PostgreSQL CHECK error aborts the subtransaction: only rollback to the caller's
        // savepoint can make the same outer transaction usable for the subsequent business save.
        foreach (var asynchronous in new[] { false, true })
        {
            var tenant = await SeedAsync();
            const string constraint = "C02_CallerSavepointReject";
            await AddOutboxRejectAsync(tenant, false, constraint);
            try
            {
                await using var db = Create(tenant);
                await using var transaction = await db.Database.BeginTransactionAsync();
                (await db.Sys_Users.SingleAsync()).ManagerId = Guid.NewGuid();
                await ExpectProviderCheckFailureAsync(() => asynchronous ? db.SaveChangesAsync() : Task.FromResult(db.SaveChanges()), constraint);
                await CommitOtherCallerWorkAsync(db, tenant, asynchronous);
                await transaction.CommitAsync();
                Require(await Scalar<Guid?>("SELECT ManagerId FROM dbo.Sys_Users WHERE TenantId=@tenant", new { tenant }) is null
                    && await CountOutbox(tenant) == 5
                    && await Scalar<int>("SELECT MAX(Version) FROM crm_identity.Snapshot WHERE TenantId=@tenant", new { tenant }) == 1,
                    "native CHECK failure escaped caller-owned savepoint rollback");
                await AssertOtherCallerWorkAsync(tenant);
            }
            finally { await DropOutboxRejectAsync(false, constraint); }
        }
        // MARS is exercised solely by the unchanged legacy SQL ExternalTransactionRollbackAsync.
    }

    private static async Task CommitOtherCallerWorkAsync(CP6.Core.EFDbContext.CP6Context db, Guid tenant, bool asynchronous)
    {
        db.ChangeTracker.Clear();
        (await db.Sys_Tenants.SingleAsync(x => x.Id == tenant)).Remark = "provider caller work committed";
        if (asynchronous) await db.SaveChangesAsync();
        else db.SaveChanges();
    }

    private async Task AssertOtherCallerWorkAsync(Guid tenant)
    {
        await using var read = Create(tenant);
        Require(await read.Sys_Tenants.AsNoTracking().Where(x => x.Id == tenant).Select(x => x.Remark).SingleAsync()
            == "provider caller work committed", "caller transaction could not commit independent work after rejection");
    }

    public async Task ProviderConcurrentBootstrapAsync()
    {
        var tenant = await SeedAsync();
        var manager = Guid.NewGuid();
        async Task Bootstrap()
        {
            await using var db = Create(tenant);
            await new IdentityBootstrapService(db, Runtime(tenant)).InitializeAsync(tenant);
        }
        async Task Change()
        {
            await using var db = Create(tenant);
            (await db.Sys_Users.SingleAsync()).ManagerId = manager;
            await db.SaveChangesAsync();
        }
        await Task.WhenAll(RetryProviderTransactionAsync(Bootstrap), RetryProviderTransactionAsync(Bootstrap), RetryProviderTransactionAsync(Change));
        var row = await Scalar<string>("SELECT PayloadJson FROM crm_identity.Snapshot WHERE TenantId=@tenant AND AggregateId LIKE N'user:%'", new { tenant });
        using var data = JsonDocument.Parse(row);
        Require(data.RootElement.GetProperty("managerId").GetGuid() == manager
            && data.RootElement.GetProperty("version").GetInt32() == 2 && await CountOutbox(tenant) == 6,
            "bootstrap overwrote or duplicated concurrent user state");
    }

    private static async Task RetryProviderTransactionAsync(Func<Task> command)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { await command(); return; }
            catch (Exception exception) when (attempt < 4 && DatabaseFailureClassifier.Classify(exception).CanRetryTransaction)
            {
                // Each delegate creates/disposes its entire context/transaction before this retry.
                await Task.Delay(50 * (attempt + 1));
            }
        }
    }
}
