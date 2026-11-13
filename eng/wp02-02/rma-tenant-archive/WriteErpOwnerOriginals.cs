using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CP6.Core.Persistence;
using CP6.Core.Services.Erp;
using CP6.Core.Services.ErpIntegration;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Erp;
using CP6.Platform.EntityFramework;
using CP6.Platform.Messaging;
using Microsoft.EntityFrameworkCore;
using Npgsql;

// Local fixture driver. Actual writes are exclusively through the original C03 Owner.
internal static class WriteErpOwnerOriginals
{
    private const int RequestVersion = 3;
    private const int AggregateVersion = 7;
    private const string Failure = "C03_BUSINESS_PARTNER_NOT_FOUND";
    private sealed record Input(string DataDirectory, string Database, int Port, long DatabaseOid,
        string SystemIdentifier, string Username, string BinaryRoot, Guid Installation, Guid TenantA, Guid TenantB,
        Guid RequestId, Guid AccountId, Dictionary<string, string> ModulePins, Guid? OpportunityId = null);

    private sealed class ForbiddenOrderEffects : IPowerEggWorkflowService, IWmsBridgeHook, IMesBridgeHook
    {
        private static InvalidOperationException Unexpected() => new("a terminal order without a partner must not invoke an external effect");
        public Task<bool> RequestPriceCorrectionAsync(OrderDetail order, string? user, CancellationToken ct = default) => throw Unexpected();
        public Task<WmsBridgeResult> OnOrderCreatedAsync(string order, string? user) => throw Unexpected();
        public Task<WmsBridgeResult> OnWorkOrderIssuedAsync(string workOrder, string? user) => throw Unexpected();
        public Task<WmsBridgeResult> OnProductionCompletedAsync(string order, decimal quantity, string? user) => throw Unexpected();
        Task<MesBridgeResult> IMesBridgeHook.OnOrderCreatedAsync(string order, string? user) => throw Unexpected();
    }

    private sealed class QueueFactory(NpgsqlConnection connection) : IDbContextFactory<ErpIntegrationContext>
    {
        public ErpIntegrationContext CreateDbContext()
        {
            var database = new DatabaseOptions(DatabaseProvider.PostgreSql);
            var profile = DatabaseMigrationProfile.For(database, DatabaseContextKind.ErpIntegration);
            var options = DatabaseContextOptions.Configure(new DbContextOptionsBuilder<ErpIntegrationContext>(),
                database, connection, profile.MigrationsAssembly, profile.HistoryTable, profile.HistorySchema);
            return new(options.Options);
        }
    }

    public static async Task<int> Main(string[] args)
    {
        try
        {
            Need(args.Length == 1, "expected one private fixture input");
            var input = JsonSerializer.Deserialize<Input>(File.ReadAllBytes(args[0]))
                ?? throw new InvalidOperationException("missing private fixture input");
            var root = Path.GetFullPath(input.BinaryRoot);
            AssemblyLoadContext.Default.Resolving += (_, name) =>
            {
                var path = Path.Combine(root, name.Name + ".dll");
                return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
            };
            var available = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            Need(!GCSettings.IsServerGC && available is > 0 and <= 536870912, "private workstation GC cap was not applied");
            await Execute(input, available);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static async Task Execute(Input input, long gcLimit)
    {
        Need(input.TenantA != input.TenantB && input.TenantA != Guid.Empty && input.TenantB != Guid.Empty
             && input.RequestId != Guid.Empty && input.AccountId != Guid.Empty, "invalid synthetic C03 identities");
        Need(input.OpportunityId is null || (input.OpportunityId != Guid.Empty && input.OpportunityId != input.AccountId),
            "the C03 order fixture requires a distinct opportunity");
        var modules = new Dictionary<string, string>();
        foreach (var assembly in new[] { typeof(ErpRequestHandler).Assembly, typeof(Cp6OutboxMessage).Assembly,
                     typeof(Cp6ContractBundle).Assembly, typeof(DbContext).Assembly, typeof(NpgsqlConnection).Assembly,
                     Assembly.Load("Npgsql.EntityFrameworkCore.PostgreSQL") })
            PinModule(input, modules, assembly);

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = "127.0.0.1", Port = input.Port, Database = input.Database, Username = input.Username,
            Pooling = false, Timeout = 5, CommandTimeout = 20, SslMode = SslMode.Disable,
            ApplicationName = "W419-real-C03-Owner", Options = "-c default_transaction_read_only=off"
        };
        var database = new DatabaseOptions(DatabaseProvider.PostgreSql);
        await using var connection = (NpgsqlConnection)new DatabaseConnectionFactory(database).Create(builder.ConnectionString);
        await connection.OpenAsync();
        await Guard(connection, input);
        var contracts = Cp6ContractBundle.Load(Path.Combine(input.BinaryRoot, "contracts", "events", "erp"));
        var validator = new ErpEventValidator(contracts);
        var runtime = new ErpIntegrationRuntime(new ErpIntegrationOptions
        {
            Enabled = true, Tenants = new() { [input.TenantA] = "local", [input.TenantB] = "local" },
            ReaderClientIds = ["w419-local-fixture"], DaprAppToken = new string('t', 48), DaprApiToken = new string('a', 48)
        }, validator);
        var effects = new ForbiddenOrderEffects();
        var owner = new ErpRequestHandler(new QueueFactory(connection), runtime, db => input.OpportunityId.HasValue
            ? new OrderService(db, effects, effects, mesBridge: effects)
            : throw new InvalidOperationException("business-partner failure must not create an OrderService"));

        var a = Envelope(input, input.TenantA);
        var b = Envelope(input, input.TenantB);
        Need(a.MessageId != b.MessageId, "different tenants must have distinct global message IDs");
        var businessBefore = input.OpportunityId.HasValue ? await OrderSnapshot(connection, input) : null;
        var firstA = await Consume(owner, a, Cp6InboxDisposition.Applied);
        var firstB = await Consume(owner, b, Cp6InboxDisposition.Applied);
        var beforeDuplicate = await Snapshot(connection, input);
        var duplicateA = await Consume(owner, a, Cp6InboxDisposition.Duplicate);
        var duplicateB = await Consume(owner, b, Cp6InboxDisposition.Duplicate);
        var afterDuplicate = await Snapshot(connection, input);
        Need(beforeDuplicate == afterDuplicate, "same original messages changed a native ERP row or token sequence");
        if (businessBefore is not null)
            Need(await OrderSnapshot(connection, input) == businessBefore, "terminal C03 order changed Core order or sequence rows");

        await using var queue = new QueueFactory(connection).CreateDbContext();
        var evidenceA = await ReadResult(queue, validator, input, input.TenantA, a, firstA, duplicateA);
        var evidenceB = await ReadResult(queue, validator, input, input.TenantB, b, firstB, duplicateB);
        Console.WriteLine(JsonSerializer.Serialize(new { Backend = connection.ProcessID, GcLimitBytes = gcLimit,
            Modules = modules, input.RequestId, input.AccountId, RequestVersion, AggregateVersion,
            DuplicateSnapshotSha256 = ErpEventContracts.Hash(Encoding.UTF8.GetBytes(beforeDuplicate)),
            A = evidenceA, B = evidenceB }));
    }

    private static void PinModule(Input input, IDictionary<string, string> modules, Assembly assembly)
    {
        var file = assembly.GetName().Name + ".dll";
        var expected = Path.GetFullPath(Path.Combine(input.BinaryRoot, file));
        Need(Path.GetFullPath(assembly.Location).Equals(expected, StringComparison.OrdinalIgnoreCase),
            "sealed assembly location differs for " + file);
        using var stream = File.OpenRead(assembly.Location);
        var pin = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        Need(input.ModulePins.TryGetValue(file, out var known) && pin == known, "sealed assembly digest differs for " + file);
        modules.Add(file, pin);
    }

    private static Cp6OutboxEnvelope Envelope(Input input, Guid tenant)
        => input.OpportunityId is { } opportunity ? ErpEventContracts.Create(tenant, opportunity.ToString("D"), AggregateVersion,
            ErpEventContracts.OrderRequested, new ErpOrderRequested(tenant, input.RequestId, input.AccountId, RequestVersion,
                opportunity, "BP-MISSING", "Q-ORIGINAL", "AAAAAAAAAAE=", 1250.50m, "JPY"),
            "local", Guid.NewGuid().ToString("N"), "w419-local-fixture")
        : ErpEventContracts.Create(tenant, input.AccountId.ToString("D"), AggregateVersion,
            ErpEventContracts.BusinessPartnerRequested,
            new ErpBusinessPartnerRequested(tenant, input.RequestId, input.AccountId, RequestVersion),
            "local", Guid.NewGuid().ToString("N"), "w419-local-fixture");

    private static async Task<Cp6InboxProcessingResult> Consume(ErpRequestHandler owner, Cp6OutboxEnvelope original,
        Cp6InboxDisposition expected)
    {
        var result = await owner.ConsumeAsync(original.Payload, original.TopicName, original.PartitionKey);
        Need(result.Disposition == expected, $"original C03 disposition {result.Disposition} differs from {expected}");
        return result;
    }

    private static async Task<object> ReadResult(ErpIntegrationContext queue, ErpEventValidator validator, Input input,
        Guid tenant, Cp6OutboxEnvelope original, Cp6InboxProcessingResult first, Cp6InboxProcessingResult duplicate)
    {
        var row = await queue.Set<Cp6OutboxMessage>().AsNoTracking()
            .SingleAsync(x => x.TenantId == tenant && x.CausationId == original.MessageId);
        var actual = new Cp6OutboxEnvelope(row.MessageId, row.TenantId, row.TopicName, row.PartitionKey, row.Payload,
            row.CorrelationId, row.CausationId, row.AggregateId, row.AggregateVersion);
        Need(validator.Validate(actual).IsValid && row.Status == Cp6OutboxStatus.Pending,
            "original ERP Outbox row is not a valid Pending C03 result");
        using var document = JsonDocument.Parse(row.Payload);
        var root = document.RootElement;
        var data = root.GetProperty("data");
        Need(root.GetProperty("type").GetString() == (input.OpportunityId.HasValue ? ErpEventContracts.OrderFailed : ErpEventContracts.BusinessPartnerFailed)
            && data.GetProperty("tenantId").GetGuid() == tenant && data.GetProperty("requestId").GetGuid() == input.RequestId
            && data.GetProperty("accountId").GetGuid() == input.AccountId && data.GetProperty("requestVersion").GetInt32() == RequestVersion
            && data.GetProperty("resultVersion").GetInt32() == 1 && data.GetProperty("errorCode").GetString() == Failure
            && !data.GetProperty("retryable").GetBoolean(), "original typed terminal C03 result differs");
        if (input.OpportunityId is { } opportunity)
        {
            Need(data.GetProperty("opportunityId").GetGuid() == opportunity && row.AggregateId == opportunity.ToString("D")
                && row.CausationId == original.MessageId && row.CorrelationId == original.CorrelationId,
                "original order identity, causation or correlation differs");
            var journal = await queue.Requests.AsNoTracking().SingleAsync(x => x.TenantId == tenant && x.RequestId == input.RequestId);
            Need(journal.Kind == ErpRequestKind.Order && journal.AggregateId == opportunity && journal.AccountId == input.AccountId
                && journal.Terminal && !journal.Succeeded && journal.ResultType == ErpEventContracts.OrderFailed
                && JsonSerializer.Serialize(data, ErpEventContracts.Json) == journal.ResultDataJson,
                "actual order journal differs from the original result");
            return new { Tenant = tenant, First = first.Disposition.ToString(), Duplicate = duplicate.Disposition.ToString(),
                CommandId = original.MessageId, CommandSha256 = ErpEventContracts.Hash(original.Payload.Span),
                journal.InputSha256, ResultId = actual.MessageId, ResultSha256 = ErpEventContracts.Hash(row.Payload), Result = data.Clone() };
        }
        var request = new ErpBusinessPartnerRequested(tenant, input.RequestId, input.AccountId, RequestVersion);
        return new { Tenant = tenant, First = first.Disposition.ToString(), Duplicate = duplicate.Disposition.ToString(),
            CommandId = original.MessageId, CommandSha256 = ErpEventContracts.Hash(original.Payload.Span),
            InputSha256 = ErpEventContracts.Hash(JsonSerializer.SerializeToUtf8Bytes(request, ErpEventContracts.Json)),
            ResultId = actual.MessageId, ResultSha256 = ErpEventContracts.Hash(row.Payload), Result = data.Clone() };
    }

    private static async Task Guard(NpgsqlConnection connection, Input input)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT jsonb_build_object('server',host(inet_server_addr()),'port',inet_server_port(),
              'database',d.datname,'oid',d.oid::bigint,'system',p.system_identifier::text,
              'data',current_setting('data_directory'),'login',session_user,'owner',pg_get_userbyid(d.datdba),
              'current',current_user,'recovery',pg_is_in_recovery(),
              'installation',(SELECT installation_id FROM cp6_rma_tenant_v1.installation WHERE singleton),
              'ready',(SELECT count(*) FROM cp6_rma_tenant_v1.tenant_migrations
                WHERE tenant_id IN (@a,@b) AND lineage='rma-application-v1' AND state='Ready'),
              'active',(SELECT count(*) FROM public."Sys_Tenants" WHERE "Id" IN (@a,@b) AND "Enable"
                AND ("ExpireDate" IS NULL OR "ExpireDate">now())),
              'partners',(SELECT count(*) FROM public."T_WebBusinessPartner"
                WHERE "TenantId" IN (@a,@b) AND ("CrmAccountId"=@account OR (@order AND "BpCd"=CAST('BP-MISSING' AS bpchar)))))::text
            FROM pg_database d CROSS JOIN pg_control_system() p WHERE d.datname=current_database();
            """;
        command.Parameters.AddWithValue("a", input.TenantA);
        command.Parameters.AddWithValue("b", input.TenantB);
        command.Parameters.AddWithValue("account", input.AccountId);
        command.Parameters.AddWithValue("order", input.OpportunityId.HasValue);
        using var identity = JsonDocument.Parse((string)(await command.ExecuteScalarAsync())!);
        var actual = identity.RootElement;
        Need(actual.GetProperty("server").GetString() == "127.0.0.1" && actual.GetProperty("port").GetInt32() == input.Port
            && actual.GetProperty("database").GetString() == input.Database && actual.GetProperty("oid").GetInt64() == input.DatabaseOid
            && actual.GetProperty("system").GetString() == input.SystemIdentifier && actual.GetProperty("data").GetString() == input.DataDirectory
            && actual.GetProperty("login").GetString() == input.Username && actual.GetProperty("owner").GetString() == input.Username
            && actual.GetProperty("current").GetString() == input.Username && !actual.GetProperty("recovery").GetBoolean()
            && actual.GetProperty("installation").GetGuid() == input.Installation && actual.GetProperty("ready").GetInt32() == 2
            && actual.GetProperty("active").GetInt32() == 2 && actual.GetProperty("partners").GetInt32() == 0,
            "wrong writable clone or missing A/B C03 prerequisites");
    }

    private static async Task<string> Snapshot(NpgsqlConnection connection, Input input)
    {
        await using var command = connection.CreateCommand();
        var projections = new List<string>();
        foreach (var table in new[] { "Aggregate", "CommandInbox", "Cp6_DeadLetterRecord", "Cp6_OutboxMessage",
                     "DeliveryReplayAudit", "InboxReplayAudit", "OrderBridgeDispatch", "Request" })
            projections.Add($"(SELECT jsonb_agg(to_jsonb(r) ORDER BY to_jsonb(r)) FROM erp_integration.\"{table}\" r WHERE r.\"TenantId\" IN (@a,@b))");
        projections.Add("(SELECT jsonb_build_array(last_value,is_called) FROM cp6_storage_erp.rowversion_v1)");
        command.CommandText = "SELECT jsonb_build_array(" + string.Join(",", projections) + ")::text;";
        command.Parameters.AddWithValue("a", input.TenantA);
        command.Parameters.AddWithValue("b", input.TenantB);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<string> OrderSnapshot(NpgsqlConnection connection, Input input)
    {
        await using var command = connection.CreateCommand();
        var projections = new List<string>();
        foreach (var table in new[] { "T_WebBusinessPartner", "T_Order", "T_OrderDetail", "T_OrderProcess", "T_OrderMaterial" })
            projections.Add($"(SELECT jsonb_agg(to_jsonb(r) ORDER BY to_jsonb(r)) FROM public.\"{table}\" r WHERE r.\"TenantId\" IN (@a,@b))");
        projections.Add("(SELECT jsonb_agg(to_jsonb(r) ORDER BY to_jsonb(r)) FROM public.\"T_DocSequence\" r WHERE r.\"FuncCode\"='ORD')");
        command.CommandText = "SELECT jsonb_build_array(" + string.Join(",", projections) + ")::text;";
        command.Parameters.AddWithValue("a", input.TenantA);
        command.Parameters.AddWithValue("b", input.TenantB);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static void Need(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
