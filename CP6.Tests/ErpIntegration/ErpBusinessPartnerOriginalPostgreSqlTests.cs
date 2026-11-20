using System.Net;
using System.Net.Http.Json;
using System.Text;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.ErpIntegration;
using CP6.Platform.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CP6.Tests.ErpIntegration;

public sealed partial class ErpHttpAuthorizationTests
{
    [CurrentPermissionPostgresFact]
    public async Task Business_partner_original_proof_matches_real_commands_and_denies_unverified_sources()
    {
        var server = new CurrentPermissionPostgresFixture();
        await server.InitializeAsync();
        try
        {
            await using var source = server.Context(First, CurrentPermissionPostgresFixture.Database);
            await using var host = await Fixture.StartPostgreSqlAsync(source.Database.GetConnectionString()!);
            var firstAccount = Guid.NewGuid();
            var secondAccount = Guid.NewGuid();
            await host.AddPartner(firstAccount);
            var success = await host.SubmitOriginal(firstAccount);
            var failure = await host.SubmitOriginal(secondAccount);

            foreach (var original in new[] { success, failure })
            {
                using var response = await host.Proof(original, host.ServiceToken());
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var body = Assert.IsType<ErpBusinessPartnerOriginal>(await response.Content.ReadFromJsonAsync<ErpBusinessPartnerOriginal>());
                Assert.Equal(new ErpBusinessPartnerOriginal(ErpBusinessPartnerOriginalReader.ProofContract,
                    ErpEventContracts.ResultSource, "ACC-C03-V1", "1.0.0", "test-source-1", First,
                    original.Command.AccountId, original.Command.RequestId, original.Command.RequestVersion,
                    original.Envelope.MessageId, ErpEventContracts.Hash(original.Envelope.Payload.Span),
                    ErpEventContracts.Hash(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(original.Command, ErpEventContracts.Json))), body);
            }
            await using var queue = host.Queue.CreateDbContext();
            var initial = await queue.Requests.AsNoTracking().ToArrayAsync();
            Assert.Equal(2, initial.Length);
            var completed = initial.Single(x => x.RequestId == success.Command.RequestId);
            Assert.True(completed.Succeeded, completed.ResultDataJson);
            Assert.Equal("0007-Bp", System.Text.Json.JsonDocument.Parse(completed.ResultDataJson).RootElement.GetProperty("businessPartnerKey").GetString());
            Assert.False(initial.Single(x => x.RequestId == failure.Command.RequestId).Succeeded);

            using var absent = await host.Proof(success, host.ServiceToken(), id: Guid.NewGuid().ToString("N"));
            using var account = await host.Proof(success, host.ServiceToken(), account: secondAccount);
            using var other = await host.Proof(success, host.ServiceToken(Second, "reader-b"), tenant: Second);
            using var unauthenticated = await host.Proof(success, null);
            Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, account.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

            // 通过原处理入口制造同消息键的内容冲突，凭据查询不能继续出具证明。
            var changed = Encoding.UTF8.GetString(success.Envelope.Payload.Span).Replace(
                success.Command.RequestId.ToString("D"), Guid.NewGuid().ToString("D"), StringComparison.Ordinal);
            await host.SendOriginal(success.Envelope, Encoding.UTF8.GetBytes(changed));
            using var conflict = await host.Proof(success, host.ServiceToken());
            Assert.Equal(HttpStatusCode.NotFound, conflict.StatusCode);
            var saved = await queue.Inbox.AsNoTracking().SingleAsync(x => x.MessageId == success.Envelope.MessageId);
            Assert.Equal(1, saved.ConflictCount);

            var journal = await queue.Requests.SingleAsync(x => x.RequestId == failure.Command.RequestId);
            journal.InputSha256 = new string('0', 64);
            await queue.SaveChangesAsync();
            using var drift = await host.Proof(failure, host.ServiceToken());
            Assert.Equal(HttpStatusCode.NotFound, drift.StatusCode);
            Assert.Equal(2, await queue.Requests.CountAsync());
            Assert.Equal(2, await queue.Set<Cp6OutboxMessage>().CountAsync());
        }
        finally { await server.DisposeAsync(); }
    }

    private sealed record OriginalSample(ErpBusinessPartnerRequested Command, Cp6OutboxEnvelope Envelope);

    private sealed partial class Fixture
    {
        private sealed class PostgreSqlQueue(DbContextOptions<ErpIntegrationContext> options) : IDbContextFactory<ErpIntegrationContext>
        {
            public ErpIntegrationContext CreateDbContext() => new(options);
        }

        private static DbContextOptions<T> PostgreSqlOptions<T>(string connection, DatabaseContextKind kind) where T : DbContext
        {
            var storage = new DatabaseOptions(DatabaseProvider.PostgreSql);
            var profile = DatabaseMigrationProfile.For(storage, kind);
            return DatabaseContextOptions.Configure(new DbContextOptionsBuilder<T>(), storage, connection,
                profile.MigrationsAssembly, profile.HistoryTable, profile.HistorySchema).Options;
        }

        public static async Task<Fixture> StartPostgreSqlAsync(string connection,
            Action<IServiceCollection>? configure = null)
        {
            var fixture = new Fixture
            {
                dbOptions = PostgreSqlOptions<CP6Context>(connection, DatabaseContextKind.Core),
                Queue = new PostgreSqlQueue(PostgreSqlOptions<ErpIntegrationContext>(connection, DatabaseContextKind.ErpIntegration))
            };
            try
            {
                await using var core = new CP6Context(fixture.dbOptions);
                await core.Database.MigrateAsync();
                await using var queue = fixture.Queue.CreateDbContext();
                await queue.Database.MigrateAsync();
                await fixture.InitializeAsync(false, connection, configure);
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }

        private async Task SeedProofCallers()
        {
            await ChangeDbAsync(async db =>
            {
                if (!await db.Sys_Tenants.IgnoreQueryFilters().AnyAsync(t => t.Id == First))
                    db.Sys_Tenants.Add(new() { Id = First, TenantCode = "proof-a", TenantName = "模拟组织甲", Enable = true });
                if (!await db.Sys_Tenants.IgnoreQueryFilters().AnyAsync(t => t.Id == Second))
                    db.Sys_Tenants.Add(new() { Id = Second, TenantCode = "proof-b", TenantName = "模拟组织乙", Enable = true });
                db.CrmServiceTokenRecords.AddRange(TokenRecord(First, "reader-a", FirstJti), TokenRecord(Second, "reader-b", SecondJti));
            });
        }

        public async Task AddPartner(Guid account)
        {
            await using var db = new CP6Context(dbOptions, new TenantContext { CurrentTenantId = First });
            const string key = "0007-Bp";
            // 预先存在的 ERP 主数据是模拟业务输入，原命令、账和结果都由真实 C03 入口产生。
            db.BusinessPartners.Add(new()
            {
                Id = Guid.NewGuid(), TenantId = First, BpCd = key, BpName = "模拟业务伙伴", BaseCd = "B01",
                CustomerFlg = true, Status = 0, AccountsReceivableCd = "AR001", SalesStaffCd = "S01", BusinessStaffCd = "S02"
            });
            await db.SaveChangesAsync();
            var partner = await db.BusinessPartners.AsNoTracking().SingleAsync(x => x.BpCd == key);
            await new ErpCommerceAuthority(db).SetBusinessPartnerProfileAsync(key,
                new(partner.RowVersion!, account, "JPY", false), "test-staff");
        }

        public async Task<OriginalSample> SubmitOriginal(Guid account)
        {
            var command = new ErpBusinessPartnerRequested(First, Guid.NewGuid(), account, 1);
            var envelope = ErpEventContracts.Create(First, account.ToString("D"), 1,
                ErpEventContracts.BusinessPartnerRequested, command, "us", Guid.NewGuid().ToString("N"), "proof-test");
            await SendOriginal(envelope, envelope.Payload.ToArray());
            return new(command, envelope);
        }

        public async Task SendOriginal(Cp6OutboxEnvelope envelope, byte[] payload)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/erp/v1/events");
            request.Headers.Add("dapr-api-token", new string('x', 32));
            request.Headers.Add("__topic", envelope.TopicName);
            request.Headers.Add("__key", envelope.PartitionKey);
            request.Headers.Add("pubsubname", "cp6-kafka-pubsub");
            request.Content = new ByteArrayContent(payload);
            request.Content.Headers.ContentType = new("application/cloudevents+json");
            using var received = await http.SendAsync(request);
            received.EnsureSuccessStatusCode();
        }

        public Task<HttpResponseMessage> Proof(OriginalSample sample, string? token, string? id = null,
            Guid? account = null, Guid? tenant = null)
            => ReadAsync($"/internal/erp/v1/business-partner-request-proofs/{account ?? sample.Command.AccountId:D}/{sample.Command.RequestId:D}/{sample.Command.RequestVersion}",
                token, tenant, message => message.Headers.Add("X-C03-Original-Message", id ?? sample.Envelope.MessageId));
    }
}
