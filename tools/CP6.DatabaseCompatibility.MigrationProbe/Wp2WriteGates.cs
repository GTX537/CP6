using System.Data.Common;
using CP6.Core.EFDbContext;
using CP6.Core.Services.ErpIntegration;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Space;
using CP6.Platform.EntityFramework;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

internal static class Wp2WriteGates
{
    public static async Task<string> TokensAsync(DbContext context, DbConnection connection, bool pg, string kind)
    {
        object entity;
        string mutable;
        switch (kind)
        {
            case "Core":
                entity = new CrmIdentitySnapshot { TenantId = ((CP6Context)context).CurrentTenantId,
                    AggregateId = "wp2:" + Guid.NewGuid().ToString("N"), EventType = "wp2.test", Version = 1,
                    PayloadJson = "{}", PayloadSha256 = new string('a', 64), UpdatedAtUtc = DateTimeOffset.UtcNow };
                mutable = nameof(CrmIdentitySnapshot.Version); break;
            case "Space":
                entity = SpaceModel.Create(((SpaceContext)context).CurrentTenantId, Guid.NewGuid());
                mutable = nameof(SpaceModel.CutoverState); break;
            case "IdentityPriority":
                entity = Activator.CreateInstance(typeof(Cp6InboxAggregateCheckpoint), nonPublic: true)!;
                var checkpoint = context.Entry(entity);
                checkpoint.Property("Id").CurrentValue = Guid.NewGuid();
                checkpoint.Property("TenantId").CurrentValue = Guid.NewGuid();
                checkpoint.Property("ConsumerName").CurrentValue = "wp2-test";
                checkpoint.Property("AggregateId").CurrentValue = Guid.NewGuid().ToString("N");
                checkpoint.Property("AggregateVersion").CurrentValue = 1;
                checkpoint.Property("UpdatedAtUtc").CurrentValue = DateTimeOffset.UtcNow;
                mutable = "AggregateVersion"; break;
            case "ErpIntegration":
                entity = new ErpIntegrationAggregate { TenantId = Guid.NewGuid(), Kind = ErpRequestKind.Order,
                    AggregateId = Guid.NewGuid(), LastRequestVersion = 1 };
                mutable = nameof(ErpIntegrationAggregate.LastRequestVersion); break;
            default: throw new MigrationAssertionException("Unknown actual token context.");
        }
        await using var transaction = await context.Database.BeginTransactionAsync();
        var entry = context.Add(entity);
        var modelType = entry.Metadata;
        var store = StoreObjectIdentifier.Table(modelType.GetTableName()!, modelType.GetSchema());
        var table = $"{Q(pg, modelType.GetSchema() ?? (pg ? "public" : "dbo"))}.{Q(pg, modelType.GetTableName()!)}";
        var parameters = new DynamicParameters();
        var keys = modelType.FindPrimaryKey()!.Properties;
        var where = string.Join(" AND ", keys.Select((property, index) =>
        {
            parameters.Add("key" + index, entry.Property(property.Name).CurrentValue);
            return Q(pg, property.GetColumnName(store)!) + "=@key" + index;
        }));
        var tokenColumn = Q(pg, modelType.FindProperty("RowVersion")!.GetColumnName(store)!);
        var mutableColumn = Q(pg, modelType.FindProperty(mutable)!.GetColumnName(store)!);
        byte[] Token() => ((byte[]?)entry.Property("RowVersion").CurrentValue)?.ToArray() ?? [];
        await context.SaveChangesAsync();
        var inserted = Token();
        Require(inserted.Length == 8, "Actual EF INSERT must hydrate the installed eight-byte database token.");
        var native = await connection.QuerySingleAsync<byte[]>($"SELECT {tokenColumn} FROM {table} WHERE {where}", parameters, transaction.GetDbTransaction());
        Require(inserted.SequenceEqual(native), "Actual EF token must match exact stored bytes.");
        await connection.ExecuteAsync($"UPDATE {table} SET {mutableColumn}={mutableColumn} WHERE {where}", parameters, transaction.GetDbTransaction());
        native = await connection.QuerySingleAsync<byte[]>($"SELECT {tokenColumn} FROM {table} WHERE {where}", parameters, transaction.GetDbTransaction());
        Require(native.Length == 8 && !native.SequenceEqual(inserted), "Native same-value maintenance UPDATE must replace the installed token.");
        entry.Property(mutable).IsModified = true;
        var rejected = false;
        try { await context.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { rejected = true; }
        Require(rejected, "Actual stale EF UPDATE must be rejected after native maintenance UPDATE.");
        await entry.ReloadAsync();
        Require(Token().SequenceEqual(native), "Reload must hydrate the winning database token.");
        entry.Property(mutable).IsModified = true;
        await context.SaveChangesAsync();
        Require(Token().Length == 8 && !Token().SequenceEqual(native), "Actual fresh EF UPDATE must hydrate a new token.");
        var beforeBulk = Token();
        var count = kind switch
        {
            "Core" => await ((CP6Context)context).CrmIdentitySnapshots.Where(x => x.TenantId == ((CrmIdentitySnapshot)entity).TenantId && x.AggregateId == ((CrmIdentitySnapshot)entity).AggregateId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Version, x => x.Version)),
            "Space" => await ((SpaceContext)context).Models.Where(x => x.Id == ((SpaceModel)entity).Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.CutoverState, x => x.CutoverState)),
            "IdentityPriority" => await context.Set<Cp6InboxAggregateCheckpoint>().Where(x => x.Id == (Guid)entry.Property("Id").CurrentValue!)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.AggregateVersion, x => x.AggregateVersion)),
            "ErpIntegration" => await ((ErpIntegrationContext)context).Aggregates.Where(x => x.TenantId == ((ErpIntegrationAggregate)entity).TenantId && x.AggregateId == ((ErpIntegrationAggregate)entity).AggregateId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.LastRequestVersion, x => x.LastRequestVersion)),
            _ => 0
        };
        Require(count == 1, "Actual ExecuteUpdate must affect exactly its fixture row.");
        native = await connection.QuerySingleAsync<byte[]>($"SELECT {tokenColumn} FROM {table} WHERE {where}", parameters, transaction.GetDbTransaction());
        Require(native.Length == 8 && !native.SequenceEqual(beforeBulk), "Actual ExecuteUpdate must advance the installed database token.");
        entry.Property(mutable).IsModified = true;
        rejected = false;
        try { await context.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { rejected = true; }
        Require(rejected, "Actual stale EF UPDATE must also be rejected after ExecuteUpdate.");
        await transaction.RollbackAsync();
        context.ChangeTracker.Clear();
        Require(await connection.QuerySingleAsync<int>($"SELECT COUNT(*) FROM {table} WHERE {where}", parameters) == 0, "All token fixture data must roll back.");
        return "Installed actual context EF insert/update exact eight-byte readback, native and ExecuteUpdate token advance, both stale EF rejections and complete fixture rollback.";
    }

    public static async Task<string> GenerationAsync(DbConnection connection, bool pg)
    {
        var oldTenant = Guid.NewGuid(); var newTenant = Guid.NewGuid(); var aggregate = "wp2:generation";
        var snapshot = $"{Q(pg, "crm_identity")}.{Q(pg, "Snapshot")}";
        var generation = $"{Q(pg, pg ? "public" : "dbo")}.{Q(pg, "CrmIdentityTenantGenerations")}";
        await using var transaction = await connection.BeginTransactionAsync();
        var parameters = new { oldTenant, newTenant, aggregate, now = DateTimeOffset.UtcNow, hash = new string('a', 64), deleted = false };
        Task<long?> Read(Guid tenant) => connection.QuerySingleOrDefaultAsync<long?>($"SELECT {Q(pg, "Generation")} FROM {generation} WHERE {Q(pg, "TenantId")}=@tenant", new { tenant }, transaction);
        await connection.ExecuteAsync($"INSERT INTO {snapshot}({Q(pg, "TenantId")},{Q(pg, "AggregateId")},{Q(pg, "EventType")},{Q(pg, "Version")},{Q(pg, "PayloadJson")},{Q(pg, "PayloadSha256")},{Q(pg, "IsDeleted")},{Q(pg, "UpdatedAtUtc")}) VALUES(@oldTenant,@aggregate,'wp2.test',1,'{{}}',@hash,@deleted,@now)", parameters, transaction);
        var inserted = await Read(oldTenant);
        Require(inserted is > 0, "Actual snapshot INSERT must seed a durable tenant generation.");
        await connection.ExecuteAsync($"UPDATE {snapshot} SET {Q(pg, "Version")}=2 WHERE {Q(pg, "TenantId")}=@oldTenant AND {Q(pg, "AggregateId")}=@aggregate", parameters, transaction);
        var updated = await Read(oldTenant);
        Require(updated > inserted, "Actual snapshot UPDATE must advance its tenant generation.");
        await connection.ExecuteAsync($"UPDATE {snapshot} SET {Q(pg, "TenantId") }=@newTenant WHERE {Q(pg, "TenantId")}=@oldTenant AND {Q(pg, "AggregateId")}=@aggregate", parameters, transaction);
        Require(await Read(oldTenant) > updated && await Read(newTenant) is > 0, "Moving a snapshot must advance old and new tenants atomically.");
        var moved = await Read(newTenant);
        await connection.ExecuteAsync($"DELETE FROM {snapshot} WHERE {Q(pg, "TenantId")}=@newTenant AND {Q(pg, "AggregateId")}=@aggregate", parameters, transaction);
        Require(await Read(newTenant) > moved, "Physical deletion must advance a retained tenant generation.");
        await transaction.RollbackAsync();
        Require(await connection.QuerySingleAsync<int>($"SELECT COUNT(*) FROM {generation} WHERE {Q(pg, "TenantId")} IN(@oldTenant,@newTenant)", parameters) == 0,
            "Snapshot mutation and tenant generations must roll back in the same real transaction.");
        return "Installed INSERT/UPDATE/tenant move/physical DELETE advanced opaque generations; both tenant versions rolled back with the snapshot transaction.";
    }

    public static async Task<string> LanguageUniqueAsync(DbConnection connection, bool pg)
    {
        var table = $"{Q(pg, pg ? "public" : "dbo")}.{Q(pg, "Sys_Langs")}";
        var key = "wp2:" + Guid.NewGuid().ToString("N");
        await using var transaction = await connection.BeginTransactionAsync();
        var insert = $"INSERT INTO {table}({Q(pg, "TenantId")},{Q(pg, "LangKey")},{Q(pg, "Status")}) VALUES(@tenant,@key,'reviewed')";
        await connection.ExecuteAsync(insert, new { tenant = (int?)null, key }, transaction);
        async Task RejectDuplicate(int? tenant)
        {
            await transaction.SaveAsync("unique_case");
            var rejected = false;
            try { await connection.ExecuteAsync(insert, new { tenant, key = key.ToUpperInvariant() + "  " }, transaction); }
            catch (PostgresException exception) when (pg && exception.SqlState == "23505") { rejected = true; }
            catch (SqlException exception) when (!pg && exception.Number is 2601 or 2627) { rejected = true; }
            await transaction.RollbackAsync("unique_case");
            Require(rejected, "Installed language uniqueness must reject case/padding-equivalent duplicate keys in the same NULL or explicit tenant.");
        }
        await RejectDuplicate(null);
        await connection.ExecuteAsync(insert, new { tenant = 900000001, key }, transaction);
        await connection.ExecuteAsync(insert, new { tenant = 900000002, key }, transaction);
        await RejectDuplicate(900000001);
        Require(await connection.QuerySingleAsync<int>($"SELECT COUNT(*) FROM {table} WHERE {Q(pg, "LangKey")}=@key", new { key }, transaction) == 3,
            "Global key and two distinct tenant overrides must coexist.");
        await transaction.RollbackAsync();
        return "Actual NULL global/case/padding duplicate rejection and distinct tenant override coexistence; fixtures rolled back.";
    }

    public static async Task<string> TimeAndNumbersAsync(CP6Context context, DbConnection connection, bool pg)
    {
        await using var transaction = await context.Database.BeginTransactionAsync();
        // Common exact precision is microseconds. Native PG timestamp cannot promise SQL's extra 100ns digit.
        var instant = new DateTime(2026, 2, 28, 23, 59, 59, DateTimeKind.Utc).AddTicks(1_234_560);
        var wall = DateTime.SpecifyKind(instant, DateTimeKind.Local);
        var session = new Sys_BrowserSession { UserId = Guid.NewGuid(), AuthenticationVersion = "wp2-time",
            LoggedOutAtUtc = instant, CreateDate = wall };
        var token = new CrmServiceTokenRecord { Issuer = "wp2:" + Guid.NewGuid().ToString("N"), Jti = Guid.NewGuid().ToString("D"),
            ClientId = "wp2-time", TenantId = context.CurrentTenantId, ExpiresAtUtc = new DateTimeOffset(instant), RevokedAtUtc = null };
        var abc = new Space_AbcSnapshot { SiteId = Guid.NewGuid(), WarehouseCd = "WP2", WindowFrom = wall, WindowTo = wall.AddDays(1),
            CalculatedAt = wall, ScheduledDate = new DateOnly(2024, 2, 29), WindowDays = 1, ThresholdA = 1.234567m, ThresholdB = 9.876543m };
        context.AddRange(session, token, abc);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var actualSession = await context.Sys_BrowserSessions.AsNoTracking().SingleAsync(row => row.Id == session.Id);
        var actualToken = await context.CrmServiceTokenRecords.AsNoTracking().SingleAsync(row => row.Issuer == token.Issuer && row.Jti == token.Jti);
        var actualAbc = await context.Space_AbcSnapshots.AsNoTracking().SingleAsync(row => row.Id == abc.Id);
        Require(actualSession.LoggedOutAtUtc?.Ticks == instant.Ticks && actualSession.CreateDate.Ticks == wall.Ticks,
            "Audited UTC instant and local wall-clock must retain the common microsecond value without shifting the calendar clock.");
        if (pg) Require(actualSession.LoggedOutAtUtc?.Kind == DateTimeKind.Utc && actualSession.CreateDate.Kind == DateTimeKind.Unspecified,
            "PG actual UTC and wall-clock read Kind must match their separate contracts.");
        Require(actualToken.ExpiresAtUtc == new DateTimeOffset(instant) && actualToken.ExpiresAtUtc.Offset == TimeSpan.Zero && actualToken.RevokedAtUtc is null,
            "Actual UTC DateTimeOffset must retain the instant and nullable value.");
        Require(actualAbc.ScheduledDate == new DateOnly(2024, 2, 29), "Actual DateOnly leap date must round-trip without timezone conversion.");
        Require(actualAbc.ThresholdA == 1.23457m && actualAbc.ThresholdB == 9.87654m, "Actual numeric(6,5) storage must retain SQL reference rounding.");
        await transaction.CreateSavepointAsync("numeric_overflow");
        var overflow = false;
        var abcTable = $"{Q(pg, pg ? "public" : "dbo")}.{Q(pg, "Space_AbcSnapshot")}";
        try { await connection.ExecuteAsync($"UPDATE {abcTable} SET {Q(pg, "ThresholdA") }=@value WHERE {Q(pg, "Id")}=@id", new { value = 10m, id = abc.Id }, transaction.GetDbTransaction()); }
        catch (PostgresException exception) when (pg && exception.SqlState == "22003") { overflow = true; }
        catch (SqlException exception) when (!pg && exception.Number == 8115) { overflow = true; }
        await transaction.RollbackToSavepointAsync("numeric_overflow");
        Require(overflow, "Actual numeric(6,5) storage must reject integer-capacity overflow.");
        foreach (var sentinel in new[] { DateTime.MinValue, DateTime.MaxValue })
        {
            var current = await context.Sys_BrowserSessions.SingleAsync(row => row.Id == session.Id);
            current.LoggedOutAtUtc = sentinel;
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            var sentinelRead = await context.Sys_BrowserSessions.AsNoTracking().SingleAsync(row => row.Id == session.Id);
            Require(sentinelRead.LoggedOutAtUtc?.Ticks == sentinel.Ticks, "Existing Min/Max UTC sentinels must round-trip exactly.");
        }
        if (pg)
        {
            var current = await context.Sys_BrowserSessions.SingleAsync(row => row.Id == session.Id);
            current.LoggedOutAtUtc = wall;
            var rejected = false;
            try { await context.SaveChangesAsync(); }
            catch (Exception exception) when (HasGuard(exception, "PostgreSQL UTC DateTime requires DateTimeKind.Utc.")) { rejected = true; }
            Require(rejected, "Audited PG instant must reject a non-UTC caller rather than reinterpret local time.");
            context.ChangeTracker.Clear();
            var currentToken = await context.CrmServiceTokenRecords.SingleAsync(row => row.Issuer == token.Issuer && row.Jti == token.Jti);
            currentToken.ExpiresAtUtc = new DateTimeOffset(instant).ToOffset(TimeSpan.FromHours(2));
            rejected = false;
            try { await context.SaveChangesAsync(); }
            catch (Exception exception) when (HasGuard(exception, "PostgreSQL DateTimeOffset requires Offset=0.")) { rejected = true; }
            Require(rejected, "PG DateTimeOffset must reject nonzero offset before caller normalization.");
        }
        await transaction.RollbackAsync();
        context.ChangeTracker.Clear();
        Require(!await context.Sys_BrowserSessions.AnyAsync(row => row.Id == session.Id) && !await context.Space_AbcSnapshots.AnyAsync(row => row.Id == abc.Id),
            "Time, precision and audit fixtures must roll back completely.");
        return "Actual microsecond UTC/wall-clock/offset and DateOnly leap-date round trips; numeric scale rounding and overflow; Min/Max sentinels; PG invalid-kind/offset rejection; full rollback. Extra SQL 100ns precision is not promised by PG.";
    }

    private static bool HasGuard(Exception exception, string message)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is InvalidOperationException && current.Message == message) return true;
        return false;
    }

    private static string Q(bool pg, string name) => pg ? $"\"{name.Replace("\"", "\"\"")}\"" : $"[{name.Replace("]", "]]")}]";
    private static void Require(bool condition, string detail) { if (!condition) throw new MigrationAssertionException(detail); }
}
