using System.Net;
using System.Text.Json;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task VerifyStockTenantBindingAsync(Harness a, Harness b, AuthenticationApiProcess correctHost,
        string connection, string aLogin, string aPassword, string bLogin)
    {
        var aBefore = await State(a);
        var bBefore = await State(b);
        var input = new StockApplyCommandV1
        {
            OperationId = Guid.NewGuid(), TxnType = WmsTxnType.ADJ, WarehouseCd = "W01", LocationCd = "L1",
            ProductCd = "P1", LotNo = "LOT", Qty = .01234567m, UnitPrice = 3m
        };
        var wrongSettings = StockReadSettings();
        wrongSettings["StockReadTenancy__EnableV1Adjust"] = "true";
        ConfigureStockReader(wrongSettings, 0, b, aLogin, aPassword);
        await using (var wrongHost = await AuthenticationApiProcess.Start(connection, wrongSettings))
        {
            var cookies = await Login(wrongHost, b);
            using var denied = await wrongHost.Send(OperationsRequest(cookies, HttpMethod.Post, "api/wms/stock/commands/v1/apply", input));
            Assert.True(denied.StatusCode == HttpStatusCode.ServiceUnavailable, await OperationsFailure(denied));
            Assert.Contains("AUTHORITY_FENCE_UNAVAILABLE", await denied.Content.ReadAsStringAsync());
        }

        var actual = new NpgsqlConnectionStringBuilder(connection)
        {
            Username = aLogin, Password = aPassword, SearchPath = "pg_catalog", Pooling = false, Enlist = false
        };
        await using var ordinary = new NpgsqlConnection(actual.ConnectionString);
        await ordinary.OpenAsync();
        var sessionUser = await CurrentPermissionPostgresFixture.Scalar(ordinary, "SELECT session_user::text");
        Assert.Equal(aLogin, sessionUser);
        var member = await CurrentPermissionPostgresFixture.Scalar(ordinary, $"SELECT pg_has_role(session_user,'{bLogin}','MEMBER')");
        Assert.Equal(false, member);
        var role = await Assert.ThrowsAsync<PostgresException>(() => CurrentPermissionPostgresFixture.Sql(ordinary, $"SET ROLE {bLogin}"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, role.SqlState);
        var session = await Assert.ThrowsAsync<PostgresException>(() =>
            CurrentPermissionPostgresFixture.Sql(ordinary, $"SET SESSION AUTHORIZATION {bLogin}"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, session.SqlState);

        await using var guc = new NpgsqlCommand("SELECT set_config('app.tenant_id',@claimed,false)", ordinary);
        guc.Parameters.AddWithValue("claimed", b.Tenant.ToString("D"));
        Assert.Equal(b.Tenant.ToString("D"), await guc.ExecuteScalarAsync());
        var mapped = await CurrentPermissionPostgresFixture.Scalar(ordinary, "SELECT cp6_tenancy_v1.current_tenant_id()");
        Assert.Equal(a.Tenant, mapped);
        await using (var visibility = new NpgsqlCommand("""SELECT "Id" FROM public."T_Stock" WHERE "Id"=ANY(@ids)""", ordinary))
        {
            visibility.Parameters.AddWithValue("ids", new[] { aBefore.StockId, bBefore.StockId });
            await using var rows = await visibility.ExecuteReaderAsync();
            Assert.True(await rows.ReadAsync());
            Assert.Equal(aBefore.StockId, rows.GetGuid(0));
            Assert.False(await rows.ReadAsync());
        }
        int changed;
        await using (var tx = await ordinary.BeginTransactionAsync())
        {
            await using var fence = new NpgsqlCommand("SELECT cp6_stock_write_fence_v1.acquire(@tenant)", ordinary, tx);
            fence.Parameters.AddWithValue("tenant", a.Tenant);
            Assert.Equal(true, await fence.ExecuteScalarAsync());
            await using var update = new NpgsqlCommand("""
                UPDATE public."T_Stock" SET "PhysicalQty"="PhysicalQty"+1 WHERE "Id"=@stock
                """, ordinary, tx);
            update.Parameters.AddWithValue("stock", bBefore.StockId);
            changed = await update.ExecuteNonQueryAsync();
            Assert.Equal(0, changed);
            await tx.CommitAsync();
        }

        Assert.Equal(aBefore, await State(a));
        Assert.Equal(bBefore, await State(b));
        var bSession = await Login(correctHost, b);
        using var missing = await correctHost.Send(OperationsRequest(bSession, HttpMethod.Get,
            $"api/wms/stock/operations/v1/{input.OperationId:D}"));
        Assert.Equal("UNKNOWN", (await StockCreationResponse(missing)).GetProperty("state").GetString());
        await File.WriteAllTextAsync(Path.Combine(Environment.GetEnvironmentVariable("CP6_PERMISSION_PG_ROOT")!,
            "stock-tenant-binding.json"), JsonSerializer.Serialize(new
            {
                sessionUser, requestedLogin = bLogin, hasRoleMembership = member,
                setRoleSqlState = role.SqlState, setSessionSqlState = session.SqlState,
                claimedTenant = b.Tenant, mappedTenant = mapped, visibleStock = aBefore.StockId, changed
            }));

        static async Task<(Guid StockId, decimal Quantity, int Movements, int Originals, int Outbox)> State(Harness h)
        {
            var row = await h.Db.Stocks.AsNoTracking().Where(x => x.WarehouseCd == "W01" && x.LocationCd == "L1" &&
                x.ProductCd == "P1" && x.LotNo == "LOT").Select(x => new { x.Id, x.PhysicalQty }).SingleAsync();
            return (row.Id, row.PhysicalQty, await h.Db.StockTransactions.CountAsync(), await Operations(h), await StockNotifications(h));
        }
    }
}
