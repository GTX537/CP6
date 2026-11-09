using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.Persistence.Tenancy;
using CP6.Core.Services.Common;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Persistence.Tenancy;

[CollectionDefinition("StockReadInspectionPostgres", DisableParallelization = true)]
public sealed class StockReadInspectionPostgresCollection : ICollectionFixture<TenantManagementPostgresFixture>;

[Collection("StockReadInspectionPostgres")]
public sealed class StockReadInspectionPostgreSqlTests(TenantManagementPostgresFixture server)
{
    [CurrentPermissionPostgresFact]
    public async Task Recovers_discarded_stock_installation_receipt_on_the_verified_target()
    {
        const string inspect = "inspect-stock-read-v1";
        const string install = "install-stock-read-v1";
        var tenants = new[] { Guid.NewGuid(), Guid.NewGuid() };
        TenantLoginV1[] logins = [new(tenants[0], "stock_inspect_a"), new(tenants[1], "stock_inspect_b")];
        await using var db = server.Context(new TenantContext { CurrentTenantId = tenants[0] });
        foreach (var tenant in tenants)
            db.Sys_Tenants.Add(new() { Id = tenant, TenantCode = tenant.ToString("N"), TenantName = "stock inspection", Enable = true });
        await db.SaveChangesAsync();
        var stockIds = new List<Guid>();
        foreach (var tenant in tenants)
        {
            await using var stockDb = server.Context(new TenantContext { CurrentTenantId = tenant });
            var stock = new Stock
            {
                Id = Guid.NewGuid(), WarehouseCd = "W01", LocationCd = "L1",
                ProductCd = "READ-RECEIPT", LotNo = "LOT", PhysicalQty = 1m, AvailableQty = 1m
            };
            stockDb.Stocks.Add(stock);
            await stockDb.SaveChangesAsync();
            stockIds.Add(stock.Id);
        }

        var settings = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString()!);
        await using var control = new NpgsqlConnection(settings.ConnectionString);
        await control.OpenAsync();
        foreach (var login in logins)
        {
            var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            await CurrentPermissionPostgresFixture.Sql(control, $"CREATE ROLE {login.Login} LOGIN PASSWORD '{password}'");
        }

        var oid = (uint)(await CurrentPermissionPostgresFixture.Scalar(control,
            "SELECT oid FROM pg_catalog.pg_database WHERE datname=current_database()"))!;
        var systemIdentifier = (string)(await CurrentPermissionPostgresFixture.Scalar(control,
            "SELECT system_identifier::text FROM pg_catalog.pg_control_system()"))!;
        var jsonOptions = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        string Input(TenantLoginV1[] requested, uint? databaseOid = null, Guid? readInstallationId = null)
            => JsonSerializer.Serialize(new
            {
                Server = settings.Host,
                settings.Port,
                settings.Database,
                DatabaseOid = databaseOid ?? oid,
                SystemIdentifier = systemIdentifier,
                AdminUsername = settings.Username,
                AdminPassword = settings.Password,
                Tenants = requested,
                SslMode = "Disable",
                ReadInstallationId = readInstallationId
            }, jsonOptions);

        var input = Input(logins);
        Assert.Equal("ABSENT", (await Success(inspect, input)).GetProperty("Outcome").GetString());
        await Rejected(inspect, Input(logins, oid + 1));

        var lost = await Cli(install, input, discardOutput: true);
        Assert.Equal(0, lost.Exit);
        Assert.Empty(lost.Output);
        Assert.Empty(lost.Error);
        var original = (Guid)(await CurrentPermissionPostgresFixture.Scalar(control,
            "SELECT installation_id FROM cp6_stock_read_v1.installation"))!;
        Assert.NotEqual(Guid.Empty, original);
        var verified = await Success(inspect, Input([logins[1], logins[0]]));
        Assert.Equal("VERIFIED", verified.GetProperty("Outcome").GetString());
        var recovered = verified.GetProperty("InstallationId").GetGuid();
        Assert.Equal(original, recovered);
        Assert.Equal(oid, verified.GetProperty("DatabaseOid").GetUInt32());
        Assert.Equal(systemIdentifier, verified.GetProperty("SystemIdentifier").GetString());

        await Rejected(install, input);
        await Rejected(inspect, Input([logins[0]]));
        await Rejected(inspect, Input([new(tenants[0], logins[1].Login), new(tenants[1], logins[0].Login)]));

        var writerInput = Input([logins[0]], readInstallationId: recovered);
        foreach (var command in new[]
        {
            "install-stock-write-fence-v1", "install-stock-original-journal-v1", "install-stock-outbox-writer-v1",
            "install-stock-movement-writer-v1", "install-stock-creation-writer-v2", "install-stock-adjust-owner-v1"
        })
            Assert.Equal("INSTALLED", (await Success(command, writerInput)).GetProperty("Outcome").GetString());
        Assert.Equal(recovered, (await Success(inspect, input)).GetProperty("InstallationId").GetGuid());

        await CurrentPermissionPostgresFixture.Sql(control, "ALTER SCHEMA cp6_stock_read_v1 RENAME TO cp6_stock_read_v1_fixture");
        var orphaned = await Cli(inspect, input);
        await CurrentPermissionPostgresFixture.Sql(control, "ALTER SCHEMA cp6_stock_read_v1_fixture RENAME TO cp6_stock_read_v1");

        const string outsideLogin = "stock_inspect_external";
        var outsidePassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await CurrentPermissionPostgresFixture.Sql(control, $$"""
            CREATE ROLE {{outsideLogin}} LOGIN PASSWORD '{{outsidePassword}}';
            GRANT USAGE ON SCHEMA public TO {{outsideLogin}};
            GRANT SELECT ON public."T_Stock" TO {{outsideLogin}};
            CREATE POLICY stock_inspect_outside ON public."T_Stock" FOR SELECT TO {{outsideLogin}} USING (true);
            """);
        await using (var outside = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(settings.ConnectionString)
        {
            Username = outsideLogin, Password = outsidePassword, Pooling = false, SearchPath = "pg_catalog"
        }.ConnectionString))
        {
            await outside.OpenAsync();
            await using var read = new NpgsqlCommand("""
                SELECT count(*) FROM public."T_Stock" WHERE "Id"=ANY(@ids)
                """, outside);
            read.Parameters.AddWithValue("ids", stockIds.ToArray());
            Assert.Equal(2L, await read.ExecuteScalarAsync());
        }
        var outsidePolicy = await Cli(inspect, input);
        await CurrentPermissionPostgresFixture.Sql(control, """DROP POLICY stock_inspect_outside ON public."T_Stock" """);

        Assert.Equal((1, 1), (orphaned.Exit, outsidePolicy.Exit));
        AssertRejected(inspect, orphaned);
        AssertRejected(inspect, outsidePolicy);

        await CurrentPermissionPostgresFixture.Sql(control, """
            CREATE POLICY stock_inspect_leak ON public."T_Stock" FOR SELECT TO stock_inspect_a USING (true)
            """);
        await Rejected(inspect, input);
        await CurrentPermissionPostgresFixture.Sql(control, """DROP POLICY stock_inspect_leak ON public."T_Stock" """);

        await CurrentPermissionPostgresFixture.Sql(control, """
            ALTER TABLE public."T_Stock" NO FORCE ROW LEVEL SECURITY
            """);
        await Rejected(inspect, input);
        await CurrentPermissionPostgresFixture.Sql(control, """ALTER TABLE public."T_Stock" FORCE ROW LEVEL SECURITY""");

        await CurrentPermissionPostgresFixture.Sql(control, """
            GRANT SELECT ("DeptName") ON public."Sys_Dept" TO stock_inspect_b
            """);
        await Rejected(inspect, input);
        await CurrentPermissionPostgresFixture.Sql(control, """
            REVOKE SELECT ("DeptName") ON public."Sys_Dept" FROM stock_inspect_b
            """);

        await CurrentPermissionPostgresFixture.Sql(control,
            "ALTER FUNCTION cp6_tenancy_v1.current_tenant_id() SECURITY INVOKER");
        await Rejected(inspect, input);
        await CurrentPermissionPostgresFixture.Sql(control,
            "ALTER FUNCTION cp6_tenancy_v1.current_tenant_id() SECURITY DEFINER");

        await CurrentPermissionPostgresFixture.Sql(control,
            "ALTER TABLE cp6_tenancy_v1.login_tenant DISABLE TRIGGER login_tenant_write_v1");
        await Rejected(inspect, input);
        await CurrentPermissionPostgresFixture.Sql(control,
            "ALTER TABLE cp6_tenancy_v1.login_tenant ENABLE TRIGGER login_tenant_write_v1");

        await CurrentPermissionPostgresFixture.Sql(control, """
            UPDATE cp6_stock_read_v1.installation SET model_version='other-layout'
            """);
        await Rejected(inspect, input);
        Assert.Equal(original, await CurrentPermissionPostgresFixture.Scalar(control,
            "SELECT installation_id FROM cp6_stock_read_v1.installation"));
    }

    private static async Task<JsonElement> Success(string operation, string input)
    {
        var result = await Cli(operation, input);
        Assert.True(result.Exit == 0, $"{operation} exited {result.Exit}: {result.Error}");
        Assert.Empty(result.Error);
        using var document = JsonDocument.Parse(result.Output);
        return document.RootElement.Clone();
    }

    private static async Task Rejected(string operation, string input)
        => AssertRejected(operation, await Cli(operation, input));

    private static void AssertRejected(string operation, (int Exit, string Output, string Error) result)
    {
        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        using var document = JsonDocument.Parse(result.Error);
        Assert.Equal(operation == "inspect-stock-read-v1" ? "REJECTED" : "NOT_APPLIED",
            document.RootElement.GetProperty("Outcome").GetString());
    }

    private static async Task<(int Exit, string Output, string Error)> Cli(string operation, string input,
        bool discardOutput = false)
    {
        var path = Environment.GetEnvironmentVariable("CP6_TENANCY_CLI")
            ?? throw new InvalidOperationException("CP6_TENANCY_CLI must point to the built tool for this requested test.");
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add(path);
        start.ArgumentList.Add(operation);
        start.ArgumentList.Add("--stdin");
        using var process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var output = ReadOutput();
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            try { await process.StandardInput.WriteAsync(input.AsMemory(), timeout.Token); }
            catch (IOException) when (process.HasExited) { }
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            return (process.ExitCode, await output, await error);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill();
                await process.WaitForExitAsync();
            }
        }

        async Task<string> ReadOutput()
        {
            if (!discardOutput) return await process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.StandardOutput.BaseStream.CopyToAsync(Stream.Null, timeout.Token);
            return "";
        }
    }
}
