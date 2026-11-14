using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Pub;
using CP6.Core.Services.Pur;
using CP6.Core.Services.Wf;
using CP6.Core.Services.Wf.Executors;
using CP6.Entity.DomainModels.Pur;
using CP6.Tests.Platform;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData("pr")]
    [InlineData("po")]
    public async Task PGT017_purchase_approval_sends_committed_job_and_recovers_lost_reply(string document)
    {
        await using var h = await Seed();
        var db = h.Db;
        var request = document == "pr";
        var schema = """
            {"start":"start","nodes":[{"id":"start","type":"start"},
            {"id":"external","type":"serviceTask","serviceKind":"webApi","serviceMode":"sync",
             "serviceConnectorName":"purchase-test","servicePath":"decision","serviceHttpMethod":"POST",
             "serviceParamsJson":"{\"purpose\":\"purchase\"}"},
            {"id":"end","type":"end"}],
            "edges":[{"from":"start","to":"external"},{"from":"external","to":"end"}]}
            """;
        var (number, _, _) = await SeedPurchaseApproval(h, document, schema);
        var receipts = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        var sent = new ConcurrentQueue<(string Key, string Payload, bool AlreadyCommitted)>();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var remote = builder.Build();
        remote.MapPost("/decision", async (HttpContext context) =>
        {
            var key = context.Request.Headers["Idempotency-Key"].ToString();
            using var reader = new StreamReader(context.Request.Body);
            var content = await reader.ReadToEndAsync(context.RequestAborted);
            await using var observed = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
            var admitted = request
                ? await observed.PurchaseRequests.AnyAsync(x => x.PrNo == number && x.Status == PrStatus.Submitted)
                : await observed.PurchaseOrders.AnyAsync(x => x.PoNo == number && x.Status == PoStatus.PendingApproval);
            var id = Guid.TryParse(key.AsSpan().StartsWith("wf-service-job-")
                ? key["wf-service-job-".Length..] : "", out var parsed) ? parsed : Guid.Empty;
            var claimed = await observed.Wf_ServiceJobs.AnyAsync(x => x.Id == id
                && x.Status == ServiceJobStatus.Running && x.AttemptCount > 0);
            sent.Enqueue((key, content, admitted && claimed));
            if (receipts.TryAdd(key, content))
            {
                context.Abort();
                return;
            }
            await context.Response.WriteAsJsonAsync(new { approved = true });
        });
        await remote.StartAsync();
        var remoteOrigin = remote.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        db.Wf_Connectors.Add(new()
        {
            Name = "purchase-test", BaseUrl = remoteOrigin, TimeoutSec = 5, Enabled = true
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await using var host = await AuthenticationApiProcess.Start(db.Database.GetConnectionString()!,
            IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant));
        var writer = await Login(host, h);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION public.cp6_test_purchase_submit_reject() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'purchase_fixture_commit_rejected' USING ERRCODE='23514'; END $$;
            CREATE CONSTRAINT TRIGGER cp6_test_purchase_submit_reject
            AFTER INSERT ON public."Wf_FlowInstance" DEFERRABLE INITIALLY DEFERRED
            FOR EACH ROW EXECUTE FUNCTION public.cp6_test_purchase_submit_reject();
            """);
        try
        {
            using var failed = await Submit();
            Assert.Empty(sent);
            Assert.True(failed.StatusCode == HttpStatusCode.ServiceUnavailable, await OperationsFailure(failed));
            Assert.Empty(await db.Wf_FlowInstances.ToListAsync());
            Assert.Empty(await db.Wf_ServiceJobs.ToListAsync());
            Assert.Equal(0, await DocumentStatus());
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER cp6_test_purchase_submit_reject ON public."Wf_FlowInstance";
                DROP FUNCTION public.cp6_test_purchase_submit_reject();
                """);
        }

        var originalConnection = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString())
        {
            SslMode = SslMode.Disable
        };
        await using (var relay = new StockPostgresCommitRelay(originalConnection.Port, originalConnection.Username!))
        {
            originalConnection.Port = relay.Port;
            await using var forwarded = await AuthenticationApiProcess.Start(originalConnection.ConnectionString,
                IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant));
            var forwardedSession = await Login(forwarded, h);
            relay.Arm();
            using var unknown = await forwarded.Send(OperationsRequest(forwardedSession, HttpMethod.Post,
                $"api/pur/{document}/{number}/submit"));
            Assert.True(unknown.StatusCode == HttpStatusCode.ServiceUnavailable, await OperationsFailure(unknown));
            Assert.Contains("IAM_OPERATION_UNKNOWN", await unknown.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            await relay.Dropped.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.Empty(sent);
        var instance = await db.Wf_FlowInstances.AsNoTracking().SingleAsync(x => x.BizId == number);
        var job = await db.Wf_ServiceJobs.AsNoTracking().SingleAsync(x => x.InstanceId == instance.Id);
        Assert.Equal(0, job.AttemptCount);
        Assert.Equal(ServiceJobStatus.Pending, job.Status);
        Assert.Equal(request ? (int)PrStatus.Submitted : (int)PoStatus.PendingApproval, await DocumentStatus());
        var approval = request
            ? await db.PurchaseRequests.Where(x => x.PrNo == number).Select(x => x.ApprovalRef).SingleAsync()
            : await db.PurchaseOrders.Where(x => x.PoNo == number).Select(x => x.ApprovalRef).SingleAsync();
        Assert.Equal(instance.Id.ToString(), approval);
        using (var duplicate = await Submit())
            Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Equal(1, await db.Wf_ServiceJobs.CountAsync());

        await RunWorker(job.NextAttemptAtUtc.AddSeconds(1));
        var retry = await db.Wf_ServiceJobs.AsNoTracking().SingleAsync();
        Assert.Equal((job.Id, ServiceJobStatus.Pending, 1), (retry.Id, retry.Status, retry.AttemptCount));
        Assert.Single(sent);
        await RunWorker(retry.NextAttemptAtUtc.AddSeconds(1));
        var completed = await db.Wf_ServiceJobs.AsNoTracking().SingleAsync();
        Assert.Equal((job.Id, ServiceJobStatus.Succeeded, 2), (completed.Id, completed.Status, completed.AttemptCount));
        Assert.Equal(2, sent.Count);
        Assert.All(sent, observed =>
        {
            Assert.Equal($"wf-service-job-{job.Id}", observed.Key);
            Assert.True(observed.AlreadyCommitted);
            using var body = JsonDocument.Parse(observed.Payload);
            Assert.Equal("purchase", body.RootElement.GetProperty("purpose").GetString());
        });
        Assert.Single(receipts);
        Assert.Equal(request ? (int)PrStatus.Approved : (int)PoStatus.Confirmed, await DocumentStatus());
        Assert.Equal(FlowInstanceStatus.Approved, await db.Wf_FlowInstances.Where(x => x.Id == instance.Id)
            .Select(x => x.Status).SingleAsync());
        await RunWorker(DateTime.UtcNow.AddHours(1), expected: 0);
        Assert.Equal(2, sent.Count);
        await remote.StopAsync();

        Task<HttpResponseMessage> Submit() => host.Send(OperationsRequest(writer, HttpMethod.Post, $"api/pur/{document}/{number}/submit"));

        Task<int> DocumentStatus() => request
            ? db.PurchaseRequests.Where(x => x.PrNo == number).Select(x => (int)x.Status).SingleAsync()
            : db.PurchaseOrders.Where(x => x.PoNo == number).Select(x => (int)x.Status).SingleAsync();

        async Task RunWorker(DateTime clock, int expected = 1)
        {
            await using var workerDb = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
            await using var services = new ServiceCollection()
                .AddSingleton(workerDb)
                .AddSingleton<IPurchaseRequestService, PurchaseRequestService>()
                .AddSingleton<IPurchaseOrderService, PurchaseOrderService>()
                .AddSingleton<CP6.Core.Services.Pur.Contracts.IApprovalService, CP6.Core.Services.Pur.Contracts.StubApprovalService>()
                .AddSingleton<ISeqService, SeqService>()
                .AddSingleton<ISupplierPriceService, SupplierPriceService>()
                .AddSingleton<IFxRateService, FxRateService>()
                .AddHttpClient()
                .BuildServiceProvider();
            var executor = new WebApiExecutor(new TenantConnectorResolver(workerDb, [],
                httpFactory: services.GetRequiredService<IHttpClientFactory>()));
            var engine = new FlowEngine(workerDb, new ApproverResolver(workerDb), dispatcher:
                new ApprovalDispatcher([new PrApprovalCallback(services), new PoApprovalCallback(services)]));
            var worker = new WfServiceJobService(workerDb, engine, [executor]);
            Assert.Equal(expected, await worker.ScanOnceAsync(clock, "purchase-fixture"));
        }
    }
}
