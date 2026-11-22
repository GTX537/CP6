using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Net.Sockets;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Erp;
using CP6.Core.Services.ErpIntegration;
using CP6.Platform.EntityFramework;
using CP6.Platform.Messaging;
using CP6.WebApi.BackgroundServices;
using CP6.WebApi.Configuration;
using CP6.WebApi.Controllers.Internal;
using CP6.WebApi.Controllers.Sys;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CP6.Tests.Platform;

public sealed partial class IamUserAssignmentsPostgreSqlTests
{
    private sealed class AccountResultControllers : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            feature.Controllers.Clear();
            feature.Controllers.Add(typeof(CrmOidcController).GetTypeInfo());
            feature.Controllers.Add(typeof(ErpReadController).GetTypeInfo());
        }
    }

    private static (HttpClient Client, TaskCompletionSource<int> Port) ResultDiscovery(string issuer, string thumbprint)
    {
        var address = new Uri(issuer);
        var port = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new SocketsHttpHandler { UseProxy = false, UseCookies = false, AllowAutoRedirect = false };
        transport.ConnectCallback = async (context, ct) =>
        {
            Assert.Equal(address.Host, context.DnsEndPoint.Host);
            Assert.Equal(address.Port, context.DnsEndPoint.Port);
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(IPAddress.Loopback, await port.Task.WaitAsync(ct), ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch { socket.Dispose(); throw; }
        };
        transport.SslOptions.RemoteCertificateValidationCallback = (_, cert, _, _) => cert?.GetCertHashString() == thumbprint;
        return (new HttpClient(transport) { Timeout = TimeSpan.FromSeconds(10) }, port);
    }

    private static void ConfigureResultProvider(WebApplicationBuilder builder, Harness h, string client,
        AccountC03ServicePrincipalRegistryV1 account, CrmIdentityRuntime identity, CrmOidcOptions oidc,
        CrmOidcCrypto crypto, HttpClient backchannel, bool compatibleSuccess)
    {
        var settings = new Dictionary<string, string?> {
            ["Database:Provider"] = "PostgreSql", ["ConnectionStrings:DefaultConnection"] = h.Db.Database.GetConnectionString(),
            ["CrmIdentity:Enabled"] = "true", ["CrmIdentity:Tenants:" + h.Tenant] = "us",
            ["ErpIntegration:Enabled"] = "true", ["ErpIntegration:Tenants:" + h.Tenant] = "us",
            ["ErpIntegration:ReaderClientIds:0"] = client, ["ErpIntegration:DaprAppToken"] = new string('a', 32),
            ["ErpIntegration:DaprApiToken"] = new string('b', 32)
        };
        const string source = "ErpIntegration:BusinessPartnerOriginalSources:0:";
        settings[source + "TenantId"] = h.Tenant.ToString("D");
        settings[source + "Epoch"] = "owned-result-source-1";
        settings[source + "FromUtc"] = "2020-01-01T00:00:00Z";
        settings[source + "UntilUtc"] = "2100-01-01T00:00:00Z";
        settings[source + "ReaderClientIds:0"] = client;
        if (compatibleSuccess) settings["ErpIntegration:BusinessPartnerSuccessV2Tenants:0"] = h.Tenant.ToString("D");
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddSingleton(oidc);
        builder.Services.AddSingleton(crypto);
        builder.Services.AddSingleton(identity);
        builder.Services.AddSingleton(account);
        builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        builder.Services.AddScoped<ICrmOidcServiceDirectory>(sp => new CrmOidcDirectory(sp.GetRequiredService<CP6Context>(),
            sp.GetRequiredService<ITenantContext>(), null!, null!, oidc, identity));
        builder.Services.AddScoped<ICrmServiceTokenRecordStore, CrmServiceTokenRecordStore>();
        builder.Services.AddScoped<CrmOidcServiceTokens>();
        builder.Services.AddErpIntegration(builder.Configuration, oidc);
        foreach (var worker in builder.Services.Where(d => d.ServiceType == typeof(IHostedService) &&
            d.ImplementationType?.Namespace == "CP6.WebApi.BackgroundServices").ToArray()) builder.Services.Remove(worker);
        builder.Services.Configure<JwtBearerOptions>(ErpReadController.Scheme, o => o.Backchannel = backchannel);
        builder.Services.AddAuthorization();
    }

    private sealed record ResultSample(Guid AccountId, Guid RequestId, int RequestVersion, string OriginalMessageId,
        string OriginalSha256, string EventId, int ResultVersion, string PayloadSha256, string Payload, string EventType);

    // 此传输只保存本轮两个指定事件。模拟首次收到原字节后确认丢失，不对真实代理施加故障。
    private sealed class ResultTransport(IReadOnlyDictionary<string,ResultSample> expected, string loseFirstAck) : ICp6DaprTransport
    {
        internal Dictionary<string,List<byte[]>> Arrivals { get; } = new(StringComparer.Ordinal);

        public Task PublishAsync(string pubsubName, string topicName, ReadOnlyMemory<byte> body, string contentType,
            IReadOnlyDictionary<string,string> metadata, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var json = JsonDocument.Parse(body);
            var id = json.RootElement.GetProperty("id").GetString()!;
            var sample = expected[id];
            Assert.Equal("cp6-kafka-pubsub", pubsubName);
            Assert.Equal(ErpEventContracts.ResultTopic, topicName);
            Assert.Equal(Cp6CloudEventCodec.StructuredContentType, contentType);
            Assert.Equal("true", metadata["rawPayload"]);
            Assert.EndsWith(":" + sample.AccountId.ToString("D"), metadata["partitionKey"]);
            Assert.Equal(sample.PayloadSha256, ErpEventContracts.Hash(body.Span));
            if (!Arrivals.TryGetValue(id, out var received)) Arrivals.Add(id, received = []);
            received.Add(body.ToArray());
            if (id == loseFirstAck && received.Count == 1) throw new Cp6OutboxPublishException("OWNED_ACK_UNAVAILABLE", true);
            return Task.CompletedTask;
        }

        public Task<HttpResponseMessage> InvokeAsync(HttpMethod method, string appId, string methodName,
            HttpContent? content, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static async Task<ResultSample[]> CheckResultProviderAsync(WebApplication app, HttpClient http, Harness h,
        string basic, string client, Guid subject, Guid epoch)
    {
        await using var queue = app.Services.GetRequiredService<IDbContextFactory<ErpIntegrationContext>>().CreateDbContext();
        await queue.Database.MigrateAsync();
        var success = Guid.NewGuid();
        h.Db.BusinessPartners.Add(new() { Id = Guid.NewGuid(), TenantId = h.Tenant, BpCd = "0007-Bp", BpName = "模拟业务伙伴",
            BaseCd = "B01", CustomerFlg = true, Status = 0, AccountsReceivableCd = "AR001", SalesStaffCd = "S01", BusinessStaffCd = "S02" });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        var partner = await h.Db.BusinessPartners.AsNoTracking().SingleAsync(x => x.TenantId == h.Tenant && x.BpCd == "0007-Bp");
        await new ErpCommerceAuthority(h.Db).SetBusinessPartnerProfileAsync("0007-Bp", new(partner.RowVersion!, success, "JPY", false), "test-staff");

        // 两份独立命令先从 CRM 发送方登记，结果由真实 C03 命令入口写入。
        var samples = new List<ResultSample>();
        foreach (var account in new[] { success, Guid.NewGuid() })
        {
            var original = new ErpBusinessPartnerRequested(h.Tenant, Guid.NewGuid(), account, 1);
            var command = ErpEventContracts.Create(h.Tenant, account.ToString("D"), 1, ErpEventContracts.BusinessPartnerRequested,
                original, "us", Guid.NewGuid().ToString("N"), "owned-a12-request");
            using var send = new HttpRequestMessage(HttpMethod.Post, "/internal/erp/v1/events");
            send.Headers.Add("dapr-api-token", new string('a', 32));
            send.Headers.Add("__topic", command.TopicName);
            send.Headers.Add("__key", command.PartitionKey);
            send.Headers.Add("pubsubname", "cp6-kafka-pubsub");
            send.Content = new ByteArrayContent(command.Payload.ToArray());
            send.Content.Headers.ContentType = new("application/cloudevents+json");
            using var applied = await http.SendAsync(send);
            applied.EnsureSuccessStatusCode();
            var stored = await queue.Set<Cp6OutboxMessage>().AsNoTracking().SingleAsync(x => x.CausationId == command.MessageId);
            using var parsed = JsonDocument.Parse(stored.Payload);
            var type = parsed.RootElement.GetProperty("type").GetString()!;
            Assert.Equal(account == success ? ErpEventContracts.BusinessPartnerSynchronized : ErpEventContracts.BusinessPartnerFailed, type);
            if (account == success)
                Assert.Equal("0007-Bp", parsed.RootElement.GetProperty("data").GetProperty("businessPartnerKey").GetString());
            samples.Add(new(account, original.RequestId, 1, command.MessageId, ErpEventContracts.Hash(command.Payload.Span),
                stored.MessageId, stored.AggregateVersion, stored.PayloadSha256, Convert.ToBase64String(stored.Payload), type));
        }

        await Task.Delay(TimeSpan.FromSeconds(1.05));
        using var credentials = new HttpRequestMessage(HttpMethod.Post, "/connect/token") {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"]="client_credentials", ["scope"]="cp6.services" }) };
        credentials.Headers.Add("Authorization", basic);
        using var issued = await http.SendAsync(credentials);
        issued.EnsureSuccessStatusCode();
        using var tokens = JsonDocument.Parse(await issued.Content.ReadAsStringAsync());
        var bearer = tokens.RootElement.GetProperty("access_token").GetString()!;
        async Task<HttpResponseMessage> Read(ResultSample s, Guid? tenant = null)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"/internal/erp/v1/business-partner-result-proofs/{s.AccountId:D}/{s.RequestId:D}/{s.RequestVersion}/{s.EventId}/{s.ResultVersion}");
            req.Headers.Authorization = new("Bearer", bearer);
            req.Headers.Add("tenantid", (tenant ?? h.Tenant).ToString("D"));
            req.Headers.Add("correlationid", "owned-result-proof");
            req.Headers.Add("X-C03-Original-Message", s.OriginalMessageId);
            req.Headers.Add("X-C03-Result-Sha256", s.PayloadSha256);
            return await http.SendAsync(req);
        }
        async Task Denied(ResultSample s, Guid? tenant = null)
        {
            using var rejected = await Read(s, tenant);
            Assert.Equal(tenant.HasValue ? HttpStatusCode.BadRequest : HttpStatusCode.NotFound, rejected.StatusCode);
            var body = await rejected.Content.ReadAsStringAsync();
            Assert.DoesNotContain(s.Payload, body);
            Assert.DoesNotContain(s.PayloadSha256, body);
        }
        foreach (var s in samples)
        {
            await Denied(s);
            // 由夹具作为本地对象权限登记方提供一份模拟授权，真实读取服务没有发放权限的路径。
            await queue.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO erp_integration."BusinessPartnerResultReadV1"
                (tenant_id,client_id,subject_id,authentication_epoch,source_epoch,account_id,request_id,request_version,
                 original_message_id,event_id,result_version,payload_sha256,valid_from,valid_until)
                VALUES ({h.Tenant},{client},{subject},{epoch},'owned-result-source-1',{s.AccountId},{s.RequestId},{s.RequestVersion},
                 {s.OriginalMessageId},{s.EventId},{s.ResultVersion},{s.PayloadSha256},clock_timestamp(),clock_timestamp()+interval '20 minutes')
                """);
            using var read = await Read(s);
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            Assert.True(read.Headers.CacheControl?.NoStore);
            var bytes = await read.Content.ReadAsByteArrayAsync();
            using var body = JsonDocument.Parse(bytes);
            Assert.False(body.RootElement.TryGetProperty("resultContract", out _));
            var proof = JsonSerializer.Deserialize<ErpBusinessPartnerResultProof>(bytes, ErpEventContracts.Json)!;
            Assert.Equal(ErpBusinessPartnerResultReader.Contract, proof.ProofContract);
            Assert.Equal(s.OriginalMessageId, proof.Original.OriginalMessageId);
            Assert.Equal(s.OriginalSha256, proof.Original.OriginalSha256);
            Assert.Equal(s.EventType, proof.EventType);
            Assert.Equal(s.PayloadSha256, proof.PayloadSha256);
            Assert.Equal(s.PayloadSha256, ErpEventContracts.Hash(proof.Payload));
            Assert.Equal(Convert.FromBase64String(s.Payload), proof.Payload);
            // 仅在此用例新建的隔离库中注入存储摘要故障，保留完整的原始载荷。
            var stored = queue.Set<Cp6OutboxMessage>().Where(x => x.TenantId == h.Tenant && x.MessageId == s.EventId);
            Assert.Equal(1, await stored.ExecuteUpdateAsync(set => set.SetProperty(x => x.PayloadSha256, new string('0', 64))));
            await Denied(s);
            Assert.Equal(1, await stored.ExecuteUpdateAsync(set => set.SetProperty(x => x.PayloadSha256, s.PayloadSha256)));
            await queue.Database.ExecuteSqlInterpolatedAsync($"""UPDATE erp_integration."BusinessPartnerResultReadV1" SET revoked=true WHERE event_id={s.EventId}""");
            await Denied(s);
            await queue.Database.ExecuteSqlInterpolatedAsync($"""UPDATE erp_integration."BusinessPartnerResultReadV1" SET revoked=false WHERE event_id={s.EventId}""");
        }
        var first = samples[0];
        await Denied(first, Guid.NewGuid());
        await Denied(first with { AccountId = samples[1].AccountId });
        await Denied(first with { RequestId = samples[1].RequestId });
        await Denied(first with { EventId = Guid.NewGuid().ToString("N") });
        await Denied(first with { ResultVersion = first.ResultVersion + 1 });
        await Denied(first with { OriginalMessageId = samples[1].OriginalMessageId });
        await Denied(first with { PayloadSha256 = new string('a',64) });
        var missing = first with { EventId = Guid.NewGuid().ToString("N") };
        await queue.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE erp_integration."BusinessPartnerResultReadV1" SET event_id={missing.EventId}
            WHERE tenant_id={h.Tenant} AND subject_id={subject} AND event_id={first.EventId}
            """);
        await Denied(missing);
        await queue.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE erp_integration."BusinessPartnerResultReadV1" SET event_id={first.EventId}
            WHERE tenant_id={h.Tenant} AND subject_id={subject} AND event_id={missing.EventId}
            """);
        Assert.Equal(2, await queue.Set<Cp6OutboxMessage>().CountAsync());
        var transport = new ResultTransport(samples.ToDictionary(s => s.EventId), first.EventId);
        var runtime = app.Services.GetRequiredService<ErpIntegrationRuntime>();
        var dispatcher = new Cp6OutboxDispatcher<ErpIntegrationContext>(app.Services.GetRequiredService<IDbContextFactory<ErpIntegrationContext>>(),
            runtime.Validator, new ErpDaprPublisher(transport, runtime.Validator), new()
            {
                DispatchBatchSize = 8, MaxOutboxAttempts = 3, InitialOutboxRetryDelay = TimeSpan.FromMilliseconds(10),
                MaximumOutboxRetryDelay = TimeSpan.FromSeconds(1)
            });
        var initial = await dispatcher.DispatchBatchAsync("owned-a12-first");
        Assert.Equal(1, initial.Published);
        Assert.Equal(1, initial.RetryScheduled);
        await Task.Delay(80);
        var retried = await dispatcher.DispatchBatchAsync("owned-a12-retry");
        Assert.Equal(1, retried.Published);
        Assert.Equal(0, retried.DeadLettered);
        Assert.Equal(2, await queue.Set<Cp6OutboxMessage>().CountAsync(m => m.Status == Cp6OutboxStatus.Published));
        foreach (var sample in samples)
        {
            var deliveries = transport.Arrivals[sample.EventId];
            Assert.Equal(sample.EventId == first.EventId ? 2 : 1, deliveries.Count);
            Assert.All(deliveries, bytes => Assert.Equal(Convert.FromBase64String(sample.Payload), bytes));
        }
        var evidencePath = Environment.GetEnvironmentVariable("CP6_ACCOUNT_C03_RESULT_EVIDENCE");
        if (!string.IsNullOrEmpty(evidencePath))
        {
            await using var evidence = new FileStream(evidencePath, FileMode.CreateNew, FileAccess.Write);
            await JsonSerializer.SerializeAsync(evidence, new { tenantId = h.Tenant, sourceEpoch = "owned-result-source-1",
                contract = ErpBusinessPartnerResultReader.Contract, transportAttempts = transport.Arrivals.ToDictionary(x => x.Key, x => x.Value.Count), samples }, ErpEventContracts.Json);
        }
        return samples.ToArray();
    }
}
