using System.Net;
using System.Text.Json;
using CP6.Core.Services.ErpIntegration;
using CP6.Platform.EntityFramework;
using CP6.Tests.Erp;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CP6.Tests.Platform;

public sealed partial class IamUserAssignmentsPostgreSqlTests
{
    private static async Task CheckCompatiblePartnerOriginalAsync(WebApplication app, HttpClient http, Harness h,
        string basic, string client, Guid subject, Guid epoch)
    {
        const string partnerKey = "BP-2026/0007";
        var account = Guid.NewGuid();
        h.Db.BusinessPartners.Add(new() { Id = Guid.NewGuid(), TenantId = h.Tenant, BpCd = partnerKey, BpName = "模拟兼容伙伴",
            BaseCd = "B01", CustomerFlg = true, Status = 0, AccountsReceivableCd = "AR001", SalesStaffCd = "S01", BusinessStaffCd = "S02" });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        var partner = await h.Db.BusinessPartners.AsNoTracking().SingleAsync(x => x.TenantId == h.Tenant && x.BpCd == partnerKey);
        await new ErpCommerceAuthority(h.Db).SetBusinessPartnerProfileAsync(partnerKey, new(partner.RowVersion!, account, "JPY", false), "test-staff");

        await using var queue = app.Services.GetRequiredService<IDbContextFactory<ErpIntegrationContext>>().CreateDbContext();
        await queue.Database.MigrateAsync();
        var original = new ErpBusinessPartnerRequested(h.Tenant, Guid.NewGuid(), account, 3);
        var command = ErpEventContracts.Create(h.Tenant, account.ToString("D"), 7,
            ErpEventContracts.BusinessPartnerRequested, original, "us", Guid.NewGuid().ToString("N"), "owned-bp-compat-request");
        await queue.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS pg_walinspect SCHEMA public");
        var beforeLsn = await queue.Database.SqlQueryRaw<string>("""
            SELECT pg_current_wal_insert_lsn()::text AS "Value"
            """).SingleAsync();

        async Task SubmitOriginal()
        {
            using var send = new HttpRequestMessage(HttpMethod.Post, "/internal/erp/v1/events");
            send.Headers.Add("dapr-api-token", new string('a', 32));
            send.Headers.Add("__topic", command.TopicName);
            send.Headers.Add("__key", command.PartitionKey);
            send.Headers.Add("pubsubname", "cp6-kafka-pubsub");
            send.Content = new ByteArrayContent(command.Payload.ToArray());
            send.Content.Headers.ContentType = new("application/cloudevents+json");
            using var applied = await http.SendAsync(send);
            applied.EnsureSuccessStatusCode();
            using var body = JsonDocument.Parse(await applied.Content.ReadAsByteArrayAsync());
            Assert.Equal("SUCCESS", body.RootElement.GetProperty("status").GetString());
        }
        await SubmitOriginal();
        await SubmitOriginal();
        var stored = await queue.Set<Cp6OutboxMessage>().AsNoTracking().SingleAsync(x => x.CausationId == command.MessageId);
        using var parsed = JsonDocument.Parse(stored.Payload);
        var root = parsed.RootElement;
        Assert.Equal(ErpEventContracts.BusinessPartnerSynchronizedV2, root.GetProperty("type").GetString());
        Assert.Equal("2.0.0", root.GetProperty("schemaversion").GetString());
        Assert.Equal(partnerKey, root.GetProperty("data").GetProperty("businessPartnerKey").GetString());
        Assert.Equal(command.CorrelationId, stored.CorrelationId);
        Assert.Equal(1, stored.AggregateVersion);

        var inbox = await queue.Inbox.AsNoTracking().SingleAsync(x => x.MessageId == command.MessageId);
        var journal = await queue.Requests.AsNoTracking().SingleAsync(x => x.RequestId == original.RequestId);
        var committedPartner = await h.Db.BusinessPartners.AsNoTracking().SingleAsync(x => x.Id == partner.Id);
        Assert.Equal(command.Payload.ToArray(), inbox.Payload);
        Assert.Equal(ErpEventContracts.Hash(inbox.Payload), inbox.PayloadSha256);
        Assert.Equal(original.RequestVersion, journal.RequestVersion);
        Assert.Equal(ErpEventContracts.BusinessPartnerInputSha256(original), journal.InputSha256);
        Assert.Equal(ErpEventContracts.BusinessPartnerSynchronizedV2, journal.ResultType);
        Assert.True(journal.Succeeded);
        Assert.True(journal.Terminal);
        Assert.Equal(1, committedPartner.Status);
        Assert.Equal(account, committedPartner.CrmAccountId);
        var xids = await queue.Database.SqlQuery<string>($"""
            SELECT i.xmin::text || ':' || r.xmin::text || ':' || o.xmin::text || ':' || bp.xmin::text AS "Value"
            FROM erp_integration."CommandInbox" i
            JOIN erp_integration."Request" r ON r."TenantId"=i."TenantId" AND r."AggregateId"=i."AggregateId"
            JOIN erp_integration."Cp6_OutboxMessage" o ON o."TenantId"=i."TenantId" AND o."MessageId"=CAST({stored.MessageId} AS bpchar)
            JOIN public."T_WebBusinessPartner" bp ON bp."TenantId"=r."TenantId" AND bp."CrmAccountId"=r."AccountId"
            WHERE i."TenantId"={h.Tenant} AND i."MessageId"=CAST({command.MessageId} AS bpchar) AND r."RequestId"={original.RequestId}
            """).SingleAsync();
        var rowXids = xids.Split(':');
        var outerTransactionId = await OrderPriceCorrectionPostgreSqlTests.UseOuterCommit(queue, beforeLsn, rowXids);

        await Task.Delay(TimeSpan.FromSeconds(1.05));
        using var credentials = new HttpRequestMessage(HttpMethod.Post, "/connect/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
                { ["grant_type"] = "client_credentials", ["scope"] = "cp6.services" })
        };
        credentials.Headers.Add("Authorization", basic);
        using var issued = await http.SendAsync(credentials);
        issued.EnsureSuccessStatusCode();
        using var tokens = JsonDocument.Parse(await issued.Content.ReadAsByteArrayAsync());
        var bearer = tokens.RootElement.GetProperty("access_token").GetString()!;
        using var originalLookup = new HttpRequestMessage(HttpMethod.Get,
            $"/internal/erp/v1/business-partner-request-proofs/{account:D}/{original.RequestId:D}/{original.RequestVersion}");
        originalLookup.Headers.Authorization = new("Bearer", bearer);
        originalLookup.Headers.Add("tenantid", h.Tenant.ToString("D"));
        originalLookup.Headers.Add("correlationid", "owned-bp-compat-request-proof");
        originalLookup.Headers.Add("X-C03-Original-Message", command.MessageId);
        using var originalResponse = await http.SendAsync(originalLookup);
        Assert.Equal(HttpStatusCode.OK, originalResponse.StatusCode);
        var sourceProof = JsonSerializer.Deserialize<ErpBusinessPartnerOriginal>(await originalResponse.Content.ReadAsByteArrayAsync(),
            ErpEventContracts.Json);
        const string sourceEpoch = "owned-result-source-1";
        var digest = stored.PayloadSha256;
        Assert.Equal(digest, ErpEventContracts.Hash(stored.Payload));
        async Task<HttpResponseMessage> Read(int protocol = 2, int? revision = null, string? hash = null,
            Guid? tenant = null, string? originalId = null, string? eventId = null, Guid? target = null)
        {
            var path = $"/internal/erp/v{protocol}/business-partner-result-proofs/{target ?? account:D}/{original.RequestId:D}/{original.RequestVersion}/{eventId ?? stored.MessageId}/{revision ?? stored.AggregateVersion}";
            using var lookup = new HttpRequestMessage(HttpMethod.Get, path);
            lookup.Headers.Authorization = new("Bearer", bearer);
            lookup.Headers.Add("tenantid", (tenant ?? h.Tenant).ToString("D"));
            lookup.Headers.Add("correlationid", "owned-bp-compat-proof");
            lookup.Headers.Add("X-C03-Original-Message", originalId ?? command.MessageId);
            lookup.Headers.Add("X-C03-Result-Sha256", hash ?? digest);
            return await http.SendAsync(lookup);
        }
        async Task Denied(Task<HttpResponseMessage> lookup, HttpStatusCode status = HttpStatusCode.NotFound)
        {
            using var denied = await lookup;
            Assert.Equal(status, denied.StatusCode);
            var body = await denied.Content.ReadAsStringAsync();
            Assert.DoesNotContain(digest, body);
            Assert.DoesNotContain(Convert.ToBase64String(stored.Payload), body);
            Assert.DoesNotContain(partnerKey, body);
        }
        async Task Grant(string eventId) => await queue.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO erp_integration."BusinessPartnerResultReadV1"
            (tenant_id,client_id,subject_id,authentication_epoch,source_epoch,account_id,request_id,request_version,
             original_message_id,event_id,result_version,payload_sha256,valid_from,valid_until)
            VALUES ({h.Tenant},{client},{subject},{epoch},{sourceEpoch},{account},{original.RequestId},{original.RequestVersion},
             {command.MessageId},{eventId},{stored.AggregateVersion},{digest},clock_timestamp(),clock_timestamp()+interval '20 minutes')
            """);
        await Denied(Read());
        await Grant(stored.MessageId);

        using var first = await Read();
        using var repeat = await Read();
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(first.StatusCode, repeat.StatusCode);
        Assert.True(first.Headers.CacheControl?.NoStore);
        var proofBytes = await first.Content.ReadAsByteArrayAsync();
        Assert.Equal(proofBytes, await repeat.Content.ReadAsByteArrayAsync());
        var proof = JsonSerializer.Deserialize<ErpBusinessPartnerResultProof>(proofBytes, ErpEventContracts.Json)!;
        Assert.Equal("C03-BP-RESULT-PROOF/2", proof.ProofContract);
        Assert.Equal(sourceProof, proof.Original);
        var binding = Assert.IsType<ErpBusinessPartnerResultContract>(proof.ResultContract);
        Assert.Equal("ACC-C03-V2", binding.Id);
        Assert.Equal("2.0.0", binding.Version);
        Assert.Equal("urn:cp6:erp", binding.Source);
        Assert.Equal(ErpEventContracts.BusinessPartnerV2BundleSha256, binding.BundleSha256);
        Assert.Equal("C03-BP-REQUEST-PROOF/1", proof.Original.ProofContract);
        Assert.Equal("ACC-C03-V1", proof.Original.ContractId);
        Assert.Equal("1.0.0", proof.Original.ContractVersion);
        Assert.Equal(sourceEpoch, proof.Original.Epoch);
        Assert.Equal(h.Tenant, proof.Original.TenantId);
        Assert.Equal(account, proof.Original.AccountId);
        Assert.Equal(original.RequestId, proof.Original.RequestId);
        Assert.Equal(original.RequestVersion, proof.Original.RequestVersion);
        Assert.Equal(command.MessageId, proof.Original.OriginalMessageId);
        Assert.Equal(inbox.PayloadSha256, proof.Original.OriginalSha256);
        Assert.Equal(journal.InputSha256, proof.Original.InputSha256);
        Assert.Equal(stored.MessageId, proof.EventId);
        Assert.Equal(stored.AggregateVersion, proof.ResultVersion);
        Assert.Equal(ErpEventContracts.BusinessPartnerSynchronizedV2, proof.EventType);
        Assert.Equal(digest, proof.PayloadSha256);
        Assert.Equal(digest, ErpEventContracts.Hash(proof.Payload));
        Assert.Equal(stored.Payload, proof.Payload);
        var decoder = app.Services.GetRequiredService<ErpIntegrationRuntime>().Validator;
        var decoded = decoder.DecodeBusinessPartnerSuccessV2(proof.Payload);
        Assert.Equal(new ErpBusinessPartnerSynchronized(h.Tenant, original.RequestId, account, original.RequestVersion,
            stored.AggregateVersion, partnerKey), decoded);

        await Denied(Read(protocol: 1));
        await Denied(Read(revision: stored.AggregateVersion + 1));
        await Denied(Read(hash: new string('a', 64)));
        await Denied(Read(tenant: Guid.NewGuid()), HttpStatusCode.BadRequest);
        await Denied(Read(target: Guid.NewGuid()));
        await Denied(Read(originalId: "owned-missing-request"));
        var missingEventId = Guid.NewGuid().ToString("N");
        await Grant(missingEventId);
        await Denied(Read(eventId: missingEventId));

        // 只在本轮隔离数据库注入摘要失配，再恢复声明摘要。
        var store = queue.Set<Cp6OutboxMessage>().Where(x => x.TenantId == h.Tenant && x.MessageId == stored.MessageId);
        Assert.Equal(1, await store.ExecuteUpdateAsync(set => set.SetProperty(x => x.PayloadSha256, new string('0', 64))));
        await Denied(Read());
        Assert.Equal(1, await store.ExecuteUpdateAsync(set => set.SetProperty(x => x.PayloadSha256, digest)));
        Assert.Equal(1, await queue.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE erp_integration."BusinessPartnerResultReadV1" SET revoked=true
            WHERE tenant_id={h.Tenant} AND event_id={stored.MessageId}
            """));
        await Denied(Read());
        var after = await h.Db.BusinessPartners.AsNoTracking().SingleAsync(x => x.Id == partner.Id);
        Assert.Equal(committedPartner.RowVersion, after.RowVersion);
        Assert.Equal(1, await queue.Inbox.CountAsync());
        Assert.Equal(1, await queue.Requests.CountAsync());
        Assert.Equal(1, await queue.Set<Cp6OutboxMessage>().CountAsync());
        Assert.Equal(stored.Payload, (await store.AsNoTracking().SingleAsync()).Payload);

        var evidence = Environment.GetEnvironmentVariable("CP6_C03_COMPAT_EVIDENCE");
        if (!string.IsNullOrEmpty(evidence))
        {
            Directory.CreateDirectory(evidence);
            await File.WriteAllBytesAsync(Path.Combine(evidence, "request.v1.cloudevent.json"), inbox.Payload);
            await File.WriteAllBytesAsync(Path.Combine(evidence, "result.v2.cloudevent.json"), proof.Payload);
            await using var output = File.Create(Path.Combine(evidence, "observation.json"));
            await JsonSerializer.SerializeAsync(output, new
            {
                proof.ProofContract, proof.Original, proof.EventId, proof.ResultVersion, proof.EventType, proof.PayloadSha256,
                binding, rootTime = root.GetProperty("time").GetDateTimeOffset(), authenticationEpoch = epoch,
                commandAggregateVersion = command.AggregateVersion, decoded, outerTransactionId, transactionIds = rowXids,
                repeat = 200, wrongProofVersion = 404, wrongResultVersion = 404, wrongDigest = 404, wrongTenant = 400,
                missingOriginal = 404, missingOutbox = 404, storedDigestMismatch = 404, revoked = 404
            }, ErpEventContracts.Json);
        }
    }
}
