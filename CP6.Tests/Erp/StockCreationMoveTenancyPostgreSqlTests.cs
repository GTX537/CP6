using System.Net;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task<StockTenantPairResult> VerifyOrdinaryV2StockMoveAsync(Harness h, MappingHarness mapping, NpgsqlConnection control,
        string connection, string login, string password, Guid historicalStock,
        (Harness Tenant, string Login, string Password) other)
    {
        await StandaloneScope(h, mapping, 3);
        var source = await h.Db.Stocks.SingleAsync(x => x.Id == historicalStock);
        source.OwnerType = "CUSTOMER";
        source.OwnerCd = "CUST-A";
        source.UnitCd = "EA";
        source.UnitPrice = 3.125m;
        source.PaperRollNo = "ROLL-A";
        source.QcStatus = StockQcStatus.Passed;
        source.RecallFlag = true;
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var settings = StockReadSettings();
        settings["StockReadTenancy__EnableV1Adjust"] = "true";
        settings["StockReadTenancy__EnableV2Adjust"] = "true";
        settings["StockReadTenancy__EnableV2Move"] = "true";
        ConfigureStockReader(settings, 0, h, login, password);
        ConfigureStockReader(settings, 1, other.Tenant, other.Login, other.Password);
        await using var host = await AuthenticationApiProcess.Start(connection, settings);
        var session = await Login(host, h);
        const string root = "api/wms/stock/";
        const string creation = root + "creation-context/move?warehouseCd=W01&locationCd=L3&productCd=P1&lotNo=LOT";
        const decimal quantity = .12345678m;
        var operation = Guid.NewGuid();

        await CurrentPermissionPostgresFixture.Sql(control, $"""REVOKE SELECT ("DeptName") ON public."Sys_Dept" FROM {login}""");
        using (var denied = await host.Send(OperationsRequest(session, HttpMethod.Get, creation)))
            Assert.True(denied.StatusCode == HttpStatusCode.ServiceUnavailable, await OperationsFailure(denied));
        await CurrentPermissionPostgresFixture.Sql(control, $"""GRANT SELECT ("DeptName") ON public."Sys_Dept" TO {login}""");
        using var contextResponse = await host.Send(OperationsRequest(session, HttpMethod.Get, creation));
        var context = await StockCreationResponse(contextResponse);
        Assert.True(context.GetProperty("requiresCreation").GetBoolean());
        Assert.Contains(context.GetProperty("departments").EnumerateArray(), department => department.GetProperty("id").GetGuid() == mapping.Root);
        var version = context.GetProperty("authorityVersion").GetString()!;

        var previousNumber = await NextTransactionNumber();
        var previousAudits = await Audits();
        var previousMappings = await NewStockMappings(h);
        var previousOperations = await Operations(h);
        var previousOutbox = await StockNotifications(h);
        // This fixture-only counter advances if the actual ordinary transaction reaches its second leg with
        // both the source movement and the new target's chosen mapping already visible inside that transaction.
        await CurrentPermissionPostgresFixture.Sql(control, $"""
            CREATE SEQUENCE public.cp6_w337_move_fault_probe;
            GRANT USAGE ON SEQUENCE public.cp6_w337_move_fault_probe TO {login};
            CREATE FUNCTION public.cp6_w337_move_fault() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $test$
            BEGIN
                IF session_user='{login}' AND EXISTS (SELECT FROM public."T_StockTransaction" source
                    WHERE source."TenantId"=NEW."TenantId" AND source."TxnNo"=NEW."RelatedNo"
                        AND source."LocationCd"='L1' AND source."Qty"=-NEW."Qty")
                    AND EXISTS (SELECT FROM cp6_operations_owner_v1.department_mappings m
                        JOIN public."T_Stock" s ON s."Id"=m.object_id AND s."TenantId"=m.tenant_id
                        WHERE m.tenant_id=NEW."TenantId" AND m.object_kind='stock' AND m.department_id='{mapping.Root:D}'
                            AND s."WarehouseCd"=NEW."WarehouseCd" AND s."LocationCd"=NEW."LocationCd"
                            AND s."ProductCd"=NEW."ProductCd" AND s."LotNo"=NEW."LotNo") THEN
                    PERFORM nextval('public.cp6_w337_move_fault_probe');
                END IF;
                RAISE EXCEPTION 'W337_DESTINATION_LEG_FAULT';
            END $test$;
            CREATE TRIGGER cp6_w337_move_fault BEFORE INSERT ON public."T_StockTransaction"
                FOR EACH ROW WHEN (NEW."TenantId"='{h.Tenant:D}' AND NEW."LocationCd"='L3' AND NEW."ProductCd"='P1'
                    AND NEW."TxnType"='MOVE' AND NEW."Qty">0) EXECUTE FUNCTION public.cp6_w337_move_fault();
            """);
        try
        {
            using var failed = await Move();
            Assert.True(failed.StatusCode == HttpStatusCode.ServiceUnavailable, await OperationsFailure(failed));
            Assert.Equal(true, await CurrentPermissionPostgresFixture.Scalar(control, "SELECT is_called FROM public.cp6_w337_move_fault_probe"));
        }
        finally
        {
            await CurrentPermissionPostgresFixture.Sql(control, """
                DROP TRIGGER cp6_w337_move_fault ON public."T_StockTransaction";
                DROP FUNCTION public.cp6_w337_move_fault();
                DROP SEQUENCE public.cp6_w337_move_fault_probe;
                """);
        }
        Assert.Equal(source.PhysicalQty, await SourceQuantity());
        Assert.False(await Target().AnyAsync());
        Assert.False(await Legs().AnyAsync());
        Assert.Equal(previousNumber, await NextTransactionNumber());
        Assert.Equal(previousAudits, await Audits());
        Assert.Equal(previousMappings, await NewStockMappings(h));
        Assert.Equal(previousOperations, await Operations(h));
        Assert.Equal(previousOutbox, await StockNotifications(h));
        using (var unknown = await Original())
            Assert.Equal("UNKNOWN", (await StockCreationResponse(unknown)).GetProperty("state").GetString());

        using var moved = await Move();
        var result = await StockCreationResponse(moved);
        using var replay = await Move();
        Assert.Equal(result.GetRawText(), (await StockCreationResponse(replay)).GetRawText());
        using var original = await Original();
        var recovered = await StockCreationResponse(original);
        Assert.Equal("COMMITTED", recovered.GetProperty("state").GetString());
        Assert.Equal(result.GetRawText(), recovered.GetProperty("result").GetRawText());
        var target = await Target().SingleAsync();
        Assert.Equal(quantity, target.PhysicalQty);
        Assert.Equal(source.PhysicalQty - quantity, await SourceQuantity());
        Assert.Equal((source.OwnerType, source.OwnerCd, source.UnitCd, source.UnitPrice, source.PaperRollNo, source.QcStatus, source.RecallFlag),
            (target.OwnerType, target.OwnerCd, target.UnitCd, target.UnitPrice, target.PaperRollNo, target.QcStatus, target.RecallFlag));
        var legs = await Legs().OrderBy(x => x.LocationCd).ToArrayAsync();
        Assert.Equal(new[] { -quantity, quantity }, legs.Select(x => x.Qty));
        Assert.Equal(legs[0].TxnNo, legs[1].RelatedNo);
        Assert.All(legs, leg =>
        {
            Assert.Equal("price-owner", leg.OperatorCd);
            Assert.Equal((source.OwnerType, source.OwnerCd, source.UnitCd, source.UnitPrice, source.PaperRollNo),
                (leg.OwnerType, leg.OwnerCd, leg.UnitCd, leg.UnitPrice, leg.PaperRollNo));
        });
        Assert.Equal(3, await h.Db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM cp6_operations_owner_v1.department_mappings
            WHERE tenant_id={h.Tenant} AND (
                object_kind='stock' AND object_id={target.Id} AND department_id={mapping.Root} OR
                object_kind='stock-transaction' AND (
                    object_id={legs[0].Id} AND department_id={mapping.Child} OR
                    object_id={legs[1].Id} AND department_id={mapping.Root}))
            """).SingleAsync());
        Assert.Equal(previousAudits + 3, await Audits());
        Assert.Equal(previousOperations + 1, await Operations(h));
        Assert.Equal(previousOutbox + 1, await StockNotifications(h));
        var pair = await VerifyStockTenantPairAsync(h, other.Tenant, control, host, session, login, other.Login);
        await VerifyStockTenantBindingAsync(h, other.Tenant, host, connection, login, password, other.Login);
        return pair;

        Task<HttpResponseMessage> Move() => host.Send(OperationsRequest(session, HttpMethod.Post, root + "commands/v2/move", new
        {
            operationId = operation, warehouseCd = "W01", fromLocationCd = "L1", toLocationCd = "L3",
            productCd = "P1", lotNo = "LOT", qty = "0.12345678", operatorCd = "forged",
            newStock = new { departmentId = mapping.Root, expectedAuthorityVersion = version }
        }));
        Task<HttpResponseMessage> Original() =>
            host.Send(OperationsRequest(session, HttpMethod.Get, root + $"operations/v1/{operation:D}"));
        IQueryable<Stock> Target() => h.Db.Stocks.AsNoTracking().Where(x => x.ProductCd == "P1" && x.LocationCd == "L3");
        IQueryable<StockTransaction> Legs() => h.Db.StockTransactions.AsNoTracking().Where(x => x.ProductCd == "P1");
        Task<decimal> SourceQuantity() => h.Db.Stocks.Where(x => x.Id == source.Id).Select(x => x.PhysicalQty).SingleAsync();
        Task<int> NextTransactionNumber() => h.Db.WmsSequences.Where(x => x.Prefix == "TXN").Select(x => x.NextNo).SingleAsync();
        Task<int> Audits() => h.Db.Sys_SecurityLogs.CountAsync(x => x.Reason == OperationsAccessV1.StockCreationAuditReason ||
            x.Reason == StockTransactionInheritanceV1.AuditReason);
    }
}
