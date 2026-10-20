using System.Data;
using System.Data.Common;
using System.Security.Claims;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Platform;

[Collection("TenantManagementPostgres")]
public sealed class FieldPermissionSourcePostgreSqlTests(TenantManagementPostgresFixture server)
{
    private sealed record Harness(CP6Context Db, Guid Tenant, Guid User, AuthorizationCatalogStoreV1 Catalog,
        DefaultHttpContext Context) : IAsyncDisposable
    {
        internal CurrentFieldPermissionSourceReaderV1 Reader => new(Db, new HttpContextAccessor { HttpContext = Context }, Catalog.Capture);
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private async Task<Harness> Seed(params IInterceptor[] interceptors)
    {
        var tenant = Guid.NewGuid();
        var db = server.Context(new TenantContext { CurrentTenantId = tenant }, null, interceptors);
        var user = new Sys_User { Id = Guid.NewGuid(), UserName = "field-source", Password = "fixture", RoleId = 0 };
        db.Sys_Tenants.Add(new() { Id = tenant, TenantCode = tenant.ToString("N"), TenantName = "owned" });
        db.Sys_Users.Add(user);
        db.Sys_Roles.AddRange(new Sys_Role { RoleId = 0, RoleName = "primary" },
            new Sys_Role { RoleId = int.MinValue, RoleName = "additional" },
            new Sys_Role { RoleId = 9, RoleName = "disabled", Enable = false });
        db.Sys_UserRoles.AddRange(new Sys_UserRole { UserId = user.Id, RoleId = int.MinValue },
            new Sys_UserRole { UserId = user.Id, RoleId = 9 },
            new Sys_UserRole { UserId = user.Id, RoleId = 88 }); // Historical absent role is not effective.
        db.Sys_RoleFieldPerms.AddRange(
            new Sys_RoleFieldPerm { RoleId = int.MinValue, ResourceKey = "order", FieldName = "Amount", Access = 3 },
            new Sys_RoleFieldPerm { RoleId = 9, ResourceKey = "order", FieldName = "Amount", Access = 1 },
            new Sys_RoleFieldPerm { RoleId = 88, ResourceKey = "order", FieldName = "Amount", Access = 1 });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var catalog = new AuthorizationCatalogStoreV1();
        catalog.RegisterFields("order", [new("Amount", "Amount"), new("UnitPrice", "Price")]); catalog.Seal();
        return new(db, tenant, user.Id, catalog,
            new DefaultHttpContext { User = CurrentPermissionContextTests.Principal(user.Id, tenant) });
    }

    private static FieldPermissionDecisionV1 Calculate(FieldPermissionSourceV1 source, FieldPermissionVersionsV1? expected = null)
        // Test-only Owner approval/constraints. Production reader neither invents nor grants these.
        => source.Evaluate(expected ?? source.Versions("fixture-mapping"), "fixture-mapping",
            source.Fields.SelectMany(f => Enum.GetValues<FieldPermissionUseV1>()
                .Select(u => new FieldPermissionMappingV1(f, u, "/fixture/" + u + "/" + f, true, []))),
            source.Fields.SelectMany(f => Enum.GetValues<FieldPermissionUseV1>()
                .Select(u => new FieldPermissionConstraintsV1(f, u, FieldPermissionCheckV1.Allowed,
                    FieldPermissionCheckV1.Allowed, FieldPermissionCheckV1.NotApplicable, FieldPermissionCheckV1.NotApplicable,
                    FieldPermissionCheckV1.NotApplicable, FieldPermissionCheckV1.NotApplicable, FieldPermissionCheckV1.NotApplicable))));

    [CurrentPermissionPostgresFact]
    public async Task Source_uses_bound_subject_effective_full_int_roles_and_actual_rows_before_defaults()
    {
        await using var h = await Seed();
        var source = await h.Reader.ReadAsync("order");
        Assert.Equal(h.Tenant, source.TenantId); Assert.Equal(h.User, source.SubjectId);
        Assert.Equal(new[] { int.MinValue, 0 }, source.Roles.Select(x => x.RoleId));
        Assert.Single(source.Rows); Assert.NotEqual(Guid.Empty, source.Rows[0].Id);
        var result = Calculate(source);
        Assert.True(result.IsAvailable); Assert.Equal(3, result.Fields["Amount"].Access);
        Assert.Equal(FieldPermissionOriginV1.REGISTERED_DEFAULT, result.Fields["UnitPrice"].Origin);
        Assert.False(result.Fields["Amount"].CanRead); Assert.True(result.Fields["UnitPrice"].CanWrite);
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 0, ResourceKey = "order", FieldName = "Amount", Access = 1 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var explicitOne = await h.Reader.ReadAsync("order");
        Assert.Equal(1, Calculate(explicitOne).Fields["Amount"].Access);
        Assert.Equal(FieldPermissionOriginV1.EXPLICIT_AGGREGATE, Calculate(explicitOne).Fields["Amount"].Origin);
        Assert.Equal(2, explicitOne.Rows.Count);
        Assert.Equal(3, Calculate(source).Fields["Amount"].Access); // Prior immutable facts did not mutate.
        Assert.Throws<NotSupportedException>(() => ((IList<string>)source.Fields).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<FieldPermissionRowV1>)source.Rows).Clear());
        var absentMapping = source.Evaluate(source.Versions("fixture-mapping"), "fixture-mapping", [], []);
        Assert.False(absentMapping.Fields["UnitPrice"].CanRead);
        Assert.Contains("MAPPING_REQUIRED", absentMapping.Fields["UnitPrice"].Uses[FieldPermissionUseV1.Read].Reasons);
    }

    [CurrentPermissionPostgresFact]
    public async Task Source_versions_change_after_raw_field_and_membership_ABA_and_reject_stale_expected_version()
    {
        await using var h = await Seed();
        var first = await h.Reader.ReadAsync("order");
        Assert.Equal(first.SourcePermissionVersion, (await h.Reader.ReadAsync("order")).SourcePermissionVersion);
        await h.Db.Sys_RoleFieldPerms.Where(x => x.RoleId == int.MinValue).ExecuteUpdateAsync(x => x.SetProperty(f => f.Access, 2));
        await h.Db.Sys_RoleFieldPerms.Where(x => x.RoleId == int.MinValue).ExecuteUpdateAsync(x => x.SetProperty(f => f.Access, 3));
        var after = await h.Reader.ReadAsync("order");
        Assert.NotEqual(first.SourcePermissionVersion, after.SourcePermissionVersion);
        Assert.NotEqual(first.Roles[0].Version, after.Roles[0].Version);
        Assert.Contains("SOURCE_VERSION_MISMATCH", Calculate(after, first.Versions("fixture-mapping")).Errors);
        await h.Db.Sys_UserRoles.Where(x => x.UserId == h.User && x.RoleId == int.MinValue).ExecuteDeleteAsync();
        var revoked = await h.Reader.ReadAsync("order"); Assert.Empty(revoked.Rows);
        h.Db.Sys_UserRoles.Add(new() { UserId = h.User, RoleId = int.MinValue }); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var restored = await h.Reader.ReadAsync("order");
        Assert.NotEqual(after.SourcePermissionVersion, restored.SourcePermissionVersion);
        Assert.Equal(3, Calculate(restored).Fields["Amount"].Access);
        Assert.Contains("SOURCE_VERSION_MISMATCH", restored.Evaluate(restored.Versions("old-mapping"), "fixture-mapping", [], []).Errors);
    }

    [CurrentPermissionPostgresFact]
    public async Task Source_rejects_unsealed_unknown_corrupt_or_unversioned_facts_without_default_fallback()
    {
        await using var h = await Seed();
        var unsealed = new AuthorizationCatalogStoreV1(); unsealed.RegisterFields("order", [new("Amount", "Amount")]);
        await Assert.ThrowsAsync<CurrentPermissionUnavailableException>(() => new CurrentFieldPermissionSourceReaderV1(h.Db, new HttpContextAccessor { HttpContext = h.Context }, unsealed.Capture).ReadAsync("order"));
        await Assert.ThrowsAsync<CurrentPermissionUnavailableException>(() => h.Reader.ReadAsync("missing"));
        foreach (var access in new[] { 0, 4 })
        {
            await h.Db.Sys_RoleFieldPerms.Where(x => x.RoleId == int.MinValue).ExecuteUpdateAsync(x => x.SetProperty(f => f.Access, access));
            await Assert.ThrowsAsync<CurrentPermissionUnavailableException>(() => h.Reader.ReadAsync("order"));
        }
        await h.Db.Sys_RoleFieldPerms.Where(x => x.RoleId == int.MinValue).ExecuteUpdateAsync(x => x.SetProperty(f => f.Access, 3).SetProperty(f => f.FieldName, "Unknown"));
        await Assert.ThrowsAsync<CurrentPermissionUnavailableException>(() => h.Reader.ReadAsync("order"));
        await h.Db.Sys_RoleFieldPerms.Where(x => x.RoleId == int.MinValue).ExecuteUpdateAsync(x => x.SetProperty(f => f.FieldName, "Amount"));
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cp6_authentication_v1.identity_admission_revisions WHERE tenant_id={h.Tenant} AND object_key='role:0'");
        await Assert.ThrowsAsync<CurrentPermissionUnavailableException>(() => h.Reader.ReadAsync("order"));
    }

    [CurrentPermissionPostgresFact]
    public async Task Source_does_not_mix_tenants_claim_identities_or_use_disabled_locked_subjects()
    {
        await using var h = await Seed(); await using var foreign = await Seed();
        await foreign.Db.Sys_RoleFieldPerms.ExecuteUpdateAsync(x => x.SetProperty(f => f.Access, 1));
        Assert.Equal(3, Calculate(await h.Reader.ReadAsync("order")).Fields["Amount"].Access);
        h.Context.User = CurrentPermissionContextTests.Principal(h.User, foreign.Tenant);
        await Assert.ThrowsAsync<CurrentPermissionUnavailableException>(() => h.Reader.ReadAsync("order"));
        h.Context.User = CurrentPermissionContextTests.Principal(foreign.User, h.Tenant);
        await Assert.ThrowsAsync<CurrentPermissionUnavailableException>(() => h.Reader.ReadAsync("order"));
        h.Context.User = CurrentPermissionContextTests.Principal(h.User, h.Tenant);
        ((ClaimsIdentity)h.Context.User.Identity!).AddClaim(new("token_use", "pending"));
        await Assert.ThrowsAsync<CurrentPermissionUnavailableException>(() => h.Reader.ReadAsync("order"));
        h.Context.User = CurrentPermissionContextTests.Principal(h.User, h.Tenant);
        h.Context.User.AddIdentity(new ClaimsIdentity([new Claim("sub", foreign.User.ToString())]));
        await Assert.ThrowsAsync<CurrentPermissionUnavailableException>(() => h.Reader.ReadAsync("order"));
        h.Context.User = CurrentPermissionContextTests.Principal(h.User, h.Tenant);
        await h.Db.Sys_Users.Where(x => x.Id == h.User).ExecuteUpdateAsync(x => x.SetProperty(u => u.LockedUntil, DateTime.Now.AddHours(1)));
        await Assert.ThrowsAsync<CurrentPermissionUnavailableException>(() => h.Reader.ReadAsync("order"));
        await h.Db.Sys_Users.Where(x => x.Id == h.User).ExecuteUpdateAsync(x => x.SetProperty(u => u.LockedUntil, (DateTime?)null).SetProperty(u => u.Enable, false));
        await Assert.ThrowsAsync<CurrentPermissionUnavailableException>(() => h.Reader.ReadAsync("order"));
        await h.Db.Sys_Users.Where(x => x.Id == h.User).ExecuteUpdateAsync(x => x.SetProperty(u => u.Enable, true));
        await h.Db.Sys_Tenants.Where(x => x.Id == h.Tenant).ExecuteUpdateAsync(x => x.SetProperty(t => t.Enable, false));
        await Assert.ThrowsAsync<CurrentPermissionUnavailableException>(() => h.Reader.ReadAsync("order"));
        await h.Db.Sys_Tenants.Where(x => x.Id == h.Tenant).ExecuteUpdateAsync(x => x.SetProperty(t => t.Enable, true).SetProperty(t => t.ExpireDate, DateTime.Now.AddDays(-1)));
        await Assert.ThrowsAsync<CurrentPermissionUnavailableException>(() => h.Reader.ReadAsync("order"));
    }

    [CurrentPermissionPostgresFact]
    public async Task Source_reads_facts_and_revisions_in_one_statement_and_respects_callers_transaction()
    {
        var commands = new CaptureCommands(); await using var h = await Seed(commands);
        commands.Armed = true;
        var first = await h.Reader.ReadAsync("order");
        Assert.Single(commands.Sql); Assert.Contains("identity_admission_revisions", commands.Sql[0]);
        commands.Armed = false;
        await using var transaction = await h.Db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        var inside = await h.Reader.ReadAsync("order");
        await using (var writer = server.Context(new TenantContext { CurrentTenantId = h.Tenant }))
            await writer.Sys_Roles.Where(x => x.RoleId == int.MinValue).ExecuteUpdateAsync(x => x.SetProperty(r => r.Enable, false));
        Assert.Equal(inside.SourcePermissionVersion, (await h.Reader.ReadAsync("order")).SourcePermissionVersion);
        Assert.Same(transaction, h.Db.Database.CurrentTransaction); await transaction.RollbackAsync();
        var current = await h.Reader.ReadAsync("order");
        Assert.NotEqual(first.SourcePermissionVersion, current.SourcePermissionVersion); Assert.Empty(current.Rows);
        Assert.Empty((await h.Reader.ReadAsync("order")).Rows);
    }

    private sealed class CaptureCommands : DbCommandInterceptor
    {
        internal bool Armed; internal List<string> Sql = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { if (Armed) Sql.Add(command.CommandText); return ValueTask.FromResult(result); }
    }

    [CurrentPermissionPostgresFact]
    public async Task Source_concurrent_revoke_cannot_splice_old_rows_with_new_revisions()
    {
        var race = new AfterRead(); await using var h = await Seed(race);
        var old = await h.Reader.ReadAsync("order");
        race.Once = async () =>
        {
            await using var writer = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
            await writer.Sys_Roles.Where(x => x.RoleId == int.MinValue).ExecuteUpdateAsync(x => x.SetProperty(r => r.Enable, false));
        };
        var raced = await h.Reader.ReadAsync("order");
        Assert.True(race.Fired);
        Assert.Equal(old.SourcePermissionVersion, raced.SourcePermissionVersion);
        Assert.Equal(old.Rows, raced.Rows);
        var current = await h.Reader.ReadAsync("order");
        Assert.NotEqual(old.SourcePermissionVersion, current.SourcePermissionVersion);
        Assert.Empty(current.Rows); Assert.Single(current.Roles);
    }

    [CurrentPermissionPostgresFact]
    public async Task Source_read_failure_or_cancellation_never_reuses_an_earlier_allow()
    {
        var failure = new ReadFailure(); await using var h = await Seed(failure);
        Assert.True(Calculate(await h.Reader.ReadAsync("order")).Fields["UnitPrice"].CanWrite);
        failure.Armed = true;
        var error = await Assert.ThrowsAsync<CurrentPermissionUnavailableException>(() => h.Reader.ReadAsync("order"));
        Assert.IsType<IOException>(error.InnerException); Assert.True(failure.Fired);
        failure.Armed = false;
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.Reader.ReadAsync("order", canceled.Token));
        h.Context.RequestAborted = canceled.Token;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.Reader.ReadAsync("order"));
    }

    private sealed class AfterRead : DbCommandInterceptor
    {
        internal Func<Task>? Once; internal bool Fired;
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (Once is { } action && command.CommandText.Contains("WITH field_subject", StringComparison.Ordinal))
            { Once = null; Fired = true; await action(); }
            return result;
        }
    }

    private sealed class ReadFailure : DbCommandInterceptor
    {
        internal bool Armed, Fired;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Armed && command.CommandText.Contains("WITH field_subject", StringComparison.Ordinal))
            { Fired = true; throw new IOException("owned field source read fault"); }
            return ValueTask.FromResult(result);
        }
    }
}
