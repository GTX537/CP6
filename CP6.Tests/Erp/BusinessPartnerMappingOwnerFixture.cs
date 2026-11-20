using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.ErpIntegration;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSec.Cryptography;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private const string MappingLegal = "JP-A";
    private static readonly JsonSerializerOptions MappingJson = new(JsonSerializerDefaults.Web);
    private static string MappingSha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string MappingHash(object value) => MappingSha(BusinessPartnerWorkspaceV1.Canonical(value));
    private static string MappingInstant(DateTimeOffset? value = null) =>
        (value ?? DateTimeOffset.UtcNow).UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

    private static async Task EnableMapping(Harness h, MappingOwnerFixture simulated)
    {
        await ProvisionWorkspace(h);
        await GrantPartnerActions(h, "mapping", "mapping.read", "mapping.correct", "mapping.reconcile");
        await EnablePartnerUse(h.Db, h.Tenant);
        foreach (var purpose in new[] { "ACCOUNT_IDENTITY", "BP_MAPPING_DECISION", "CRM_PROJECTION", "ACC_MAPPING_AUTHORITY" })
            await InstallMappingBinding(h, simulated.Binding(purpose));
    }

    private static async Task InstallMappingBinding(Harness h, BusinessPartnerMappingAuthorityBindingV1 binding)
    {
        await using var tx = await h.Db.Database.BeginTransactionAsync();
        await BpUseScopeGate.LockAsync(h.Db, binding.LegalEntityKey, true, default);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO main_bp.mapping_authorities(tenant_id,legal_entity_key,purpose,organization_id,authority,epoch,key_id,public_key,route_key,enabled)
            VALUES({binding.TenantId},{binding.LegalEntityKey},{binding.Purpose},{binding.OrganizationId},
                {binding.Authority},{binding.Epoch},{binding.KeyId},{binding.PublicKey},{binding.RouteKey},true)
            ON CONFLICT (tenant_id,legal_entity_key,purpose) DO UPDATE SET organization_id=EXCLUDED.organization_id,
                authority=EXCLUDED.authority,epoch=EXCLUDED.epoch,key_id=EXCLUDED.key_id,public_key=EXCLUDED.public_key,
                route_key=EXCLUDED.route_key,enabled=true
            """);
        await tx.CommitAsync();
    }

    private static async Task SetEvidenceGuard(Harness h, MappingOwnerFixture owner, Guid account, string purpose,
        string key, string version, bool active = true)
    {
        await using var tx = await h.Db.Database.BeginTransactionAsync();
        await BpUseScopeGate.LockAsync(h.Db, MappingLegal, true, default);
        var digest = BusinessPartnerWorkspaceV1.MappingAuthorityHash(owner.Binding(purpose));
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO main_bp.mapping_evidence_guards(tenant_id,legal_entity_key,account_id,purpose,guard_key,guard_version,authority_hash,active)
            VALUES({h.Tenant},{MappingLegal},{account},{purpose},{key},{version},{digest},{active})
            ON CONFLICT(tenant_id,legal_entity_key,account_id,purpose,guard_key) DO UPDATE
              SET guard_version=EXCLUDED.guard_version,authority_hash=EXCLUDED.authority_hash,active=EXCLUDED.active
            """);
        await tx.CommitAsync();
    }

    private ServiceProvider MappingProtection(Harness h) => new ServiceCollection().AddLogging()
        .AddScoped(_ => server.Context(new CP6.Core.Services.Common.TenantContext { CurrentTenantId = h.Tenant }))
        .AddDataProtection().SetApplicationName("CP6").PersistKeysToDbContext<CP6Context>().Services.BuildServiceProvider();

    private static BusinessPartnerWorkspaceV1 MappingService(Harness h, CP6Context db, IDataProtectionProvider protection,
        BusinessPartnerMappingAuthorityV1 authority, TimeProvider? clock = null)
    {
        var current = OwnedUseAuthority(h, db);
        return new(db, current, protection, new CP6.Core.Services.Fin.BusinessPartnerFinanceReferenceReader(db, current,
            new CP6.Core.Services.Fin.ManufacturingFinanceScopeV1(db, null)), clock ?? TimeProvider.System, authority);
    }

    private sealed record MappingTestRef(string Owner, string Id, string Version, string Digest, string Purpose);
    private sealed record MappingTestRecord(MappingTestRef Reference, string OriginalBodyBase64);
    private static MappingTestRecord TestEvidence(string owner, string id, string purpose, object source, string version = "1")
    {
        var bytes = BusinessPartnerWorkspaceV1.Canonical(source);
        return new(new(owner, id, version, MappingSha(bytes), purpose), Convert.ToBase64String(bytes));
    }

    private sealed class MappingOwnerFixture(Guid tenant) : IAsyncDisposable
    {
        private readonly Key key = Key.Create(SignatureAlgorithm.Ed25519, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        private readonly Guid organization = Guid.NewGuid();
        private readonly string bearer = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        private WebApplication? bridge;
        private readonly ConcurrentDictionary<Guid, (string Code, string Status, string Revision, bool WrongSignature)> accounts = new();
        private readonly ConcurrentDictionary<Guid, bool> forbidden = new();
        private readonly ConcurrentDictionary<Guid, byte[]> latest = new();
        private readonly ConcurrentDictionary<string, (MappingTestRecord Record, string SubjectHash)> evidence = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<Guid, object> conflicts = new();
        private readonly ConcurrentDictionary<Guid, (MappingTestRecord Journal, MappingTestRecord[] Trust)> corrections = new();
        internal readonly ConcurrentDictionary<Guid, byte[]> AccountOriginals = new();
        internal readonly ConcurrentQueue<byte[]> Deliveries = new();
        internal readonly ConcurrentQueue<Func<JsonElement, Task<object?>>> ProjectionResponses = new();
        internal Func<int, int>? DeliveryStatus { get; set; }
        internal Func<int, Task<int>>? DelayDelivery { get; set; }

        internal Guid Organization => organization;
        internal BusinessPartnerMappingAuthorityBindingV1 Binding(string purpose) => new(tenant, MappingLegal, organization, purpose,
            purpose switch
            {
                "ACCOUNT_IDENTITY" => "CRM.Account",
                "BP_MAPPING_DECISION" => "Main.ApprovedEvidence",
                "CRM_PROJECTION" => "CRM.AccountMapping",
                "ACC_MAPPING_AUTHORITY" => BusinessPartnerWorkspaceV1.DirectoryOwner,
                _ => throw new InvalidOperationException("测试目的未知")
            }, "fixture-epoch-1", "fixture-key-1", key.PublicKey.Export(KeyBlobFormat.RawPublicKey), "OwnedMapping");

        internal void Account(Guid id, string code, string status = "ACTIVE", string revision = "9007199254740993", bool wrongSignature = false)
            => accounts[id] = (code, status, revision, wrongSignature);
        internal void DenyAccount(Guid id) => forbidden[id] = true;
        internal void Evidence(MappingTestRecord item, object subject) => evidence[item.Reference.Id] = (item, MappingHash(subject));
        internal void Conflicts(Guid account, object response, MappingTestRecord review, params MappingTestRecord[] trust)
        {
            conflicts[account] = response;
            corrections[account] = (review, trust);
        }

        internal static async Task<MappingOwnerFixture> Start(Guid tenant)
        {
            var fixture = new MappingOwnerFixture(tenant);
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = "Development", Args = [] });
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, 0));
            fixture.bridge = builder.Build();
            fixture.bridge.MapPost("/internal/crm/v1/account-identities/read", (HttpContext ctx) => fixture.Read(ctx, "ACCOUNT_IDENTITY"));
            fixture.bridge.MapPost("/internal/crm/v1/account-erp-mapping-projections/read", (HttpContext ctx) => fixture.Read(ctx, "CRM_PROJECTION"));
            fixture.bridge.MapPost("/internal/crm/v1/account-erp-mapping-conflicts/read", (HttpContext ctx) => fixture.Read(ctx, "CRM_PROJECTION"));
            fixture.bridge.MapPost("/internal/main/v1/bp/decision-evidence/read", (HttpContext ctx) => fixture.Read(ctx, "BP_MAPPING_DECISION"));
            fixture.bridge.MapPost("/internal/main/v1/bp/mapping-issuance-evidence/read", (HttpContext ctx) => fixture.Read(ctx, "BP_MAPPING_DECISION"));
            fixture.bridge.MapPost("/internal/crm/v1/account-erp-mappings", (HttpContext ctx) => fixture.Deliver(ctx));
            await fixture.bridge.StartAsync();
            return fixture;
        }

        internal Dictionary<string, string> Environment()
        {
            var exported = key.Export(KeyBlobFormat.RawPrivateKey);
            try
            {
                return new()
                {
                    ["BusinessPartnerMapping__Routes__OwnedMapping__Endpoint"] = bridge!.Urls.Single(),
                    ["BusinessPartnerMapping__Routes__OwnedMapping__Bearer"] = bearer,
                    ["BusinessPartnerMapping__Routes__OwnedMapping__SigningKey"] = Convert.ToBase64String(exported)
                };
            }
            finally { CryptographicOperations.ZeroMemory(exported); }
        }

        internal BusinessPartnerMappingAuthorityV1 Adapter(HttpClient client)
            => new BusinessPartnerMappingHttpAuthorityV1(client, new ConfigurationBuilder().AddInMemoryCollection(
                Environment().ToDictionary(x => x.Key.Replace("__", ":"), x => (string?)x.Value)).Build(), bridge!.Environment);

        private async Task Read(HttpContext ctx, string purpose)
        {
            if (!Authorized(ctx)) { ctx.Response.StatusCode = 401; return; }
            var bytes = await RequestBytes(ctx);
            using var doc = JsonDocument.Parse(bytes);
            var request = doc.RootElement;
            var path = ctx.Request.Path.Value!;
            Guid account;
            object? result;
            var invalid = false;
            switch (path)
            {
                case "/internal/crm/v1/account-identities/read":
                    account = request.GetProperty("accountId").GetGuid();
                    if (forbidden.ContainsKey(account)) result = new { availability = "NOT_AUTHORIZED" };
                    else if (accounts.TryGetValue(account, out var selected))
                    {
                        invalid = selected.WrongSignature;
                        result = new
                        {
                            availability = "FOUND", accountId = account, accountCode = selected.Code, accountStatus = selected.Status,
                            identityBusinessRevision = selected.Revision, immutableIdentity = true, sourceAsOf = MappingInstant(),
                            originalUnpublishedFields = new { internalName = "模拟原件的私有资料", order = new[] { 4, 3, 2 } }
                        };
                    }
                    else result = new { availability = "NOT_FOUND" };
                    break;
                case "/internal/crm/v1/account-erp-mapping-projections/read":
                    account = request.GetProperty("accountId").GetGuid();
                    if (ProjectionResponses.TryDequeue(out var simulated)) result = await simulated(request);
                    else if (latest.TryGetValue(account, out var raw))
                    {
                        using var prior = JsonDocument.Parse(raw);
                        var projection = prior.RootElement;
                        result = Projection(account, projection.GetProperty("mappingRevision").GetString()!,
                            projection.GetProperty("businessPartnerKey").GetString(), false, projection.GetProperty("sourceAsOf").GetString());
                    }
                    else result = new { availability = "NOT_FOUND" };
                    break;
                case "/internal/crm/v1/account-erp-mapping-conflicts/read":
                    account = request.GetProperty("accountId").GetGuid();
                    result = conflicts.GetValueOrDefault(account) ?? new { availability = "UNPROVEN", records = Array.Empty<object>(),
                        coverageRef = (object?)null, asOf = MappingInstant() };
                    break;
                case "/internal/main/v1/bp/decision-evidence/read":
                    var subject = request.GetProperty("subject");
                    account = subject.GetProperty("accountId").GetGuid();
                    var requested = request.GetProperty("evidence").EnumerateArray().Select(x => x.Deserialize<MappingTestRef>(MappingJson)!).ToArray();
                    var subjectHash = MappingHash(subject);
                    var records = requested.Select(x => evidence.GetValueOrDefault(x.Id)).ToArray();
                    result = records.Zip(requested).All(x => x.First.Record?.Reference == x.Second && x.First.SubjectHash == subjectHash) ? new
                    {
                        availability = "VERIFIED", subjectHash, guardKey = EvidenceGuard(account),
                        guardVersion = "1", records = records.Select(x => x.Record).ToArray()
                    } : new { availability = "UNPROVEN" };
                    break;
                case "/internal/main/v1/bp/mapping-issuance-evidence/read":
                    account = request.GetProperty("main").GetProperty("accountId").GetGuid();
                    result = corrections.TryGetValue(account, out var review) ? new
                    {
                        availability = "VERIFIED", reviewedInputHash = MappingHash(request), guardKey = JournalGuard(account), guardVersion = "1",
                        ownerEvidence = new[] { review.Journal }, receivedTrustRecords = review.Trust
                    } : new { availability = "UNPROVEN" };
                    break;
                default: throw new InvalidOperationException("测试桥路径未登记");
            }
            if (result is null) { ctx.Response.StatusCode = 503; return; }
            // 保留有序属性、空白和原 UTF-8，便于验证 Main 没有重编码原件。
            var rawBody = JsonSerializer.SerializeToUtf8Bytes(result, new JsonSerializerOptions(MappingJson) { WriteIndented = true });
            if (purpose == "ACCOUNT_IDENTITY") AccountOriginals[account] = rawBody;
            var binding = Binding(purpose);
            var signature = SignatureAlgorithm.Ed25519.Sign(key, Encoding.UTF8.GetBytes(string.Join('\n', "BP-OWNER-ORIGINAL/1",
                tenant.ToString("D"), organization.ToString("D"), MappingLegal, purpose, binding.Authority, binding.Epoch,
                binding.KeyId, path, ctx.Request.Headers["X-BP-Owner-Nonce"].ToString(), MappingSha(bytes), MappingSha(rawBody))));
            if (invalid) signature[0] ^= 1;
            ctx.Response.Headers["X-BP-Owner-Proof"] = Convert.ToBase64String(signature);
            ctx.Response.ContentType = "application/json";
            await ctx.Response.Body.WriteAsync(rawBody);
        }

        internal static object Projection(Guid account, string revision, string? code, bool conflict, string? source = null)
        {
            var fact = new { accountId = account, legalEntityKey = MappingLegal, mappingRevision = revision,
                state = code is null ? "UNLINKED" : "LINKED", businessPartnerKey = code };
            return new
            {
                availability = "FOUND", fact.accountId, fact.legalEntityKey, fact.mappingRevision, fact.state, fact.businessPartnerKey,
                semanticHash = MappingHash(fact), sourceAsOf = source ?? MappingInstant(), receivedAt = MappingInstant(), conflict,
                proofRef = new MappingTestRef("CRM.AccountMapping", "模拟实时投影", "1", MappingHash(fact), "BP_MAPPING_PROJECTION")
            };
        }

        internal static string EvidenceGuard(Guid account) => "decision-" + account.ToString("D");
        internal static string JournalGuard(Guid account) => "journal-" + account.ToString("D");

        private async Task Deliver(HttpContext ctx)
        {
            if (!Authorized(ctx)) { ctx.Response.StatusCode = 401; return; }
            var raw = await RequestBytes(ctx);
            Deliveries.Enqueue(raw);
            using var json = JsonDocument.Parse(raw);
            var coordinates = json.RootElement;
            var account = coordinates.GetProperty("accountId").GetGuid();
            long Rev(byte[] original) => JsonSerializer.Deserialize<JsonElement>(original).GetProperty("mappingRevision").GetString() is { } revision
                ? long.Parse(revision, System.Globalization.CultureInfo.InvariantCulture) : 0;
            latest.AddOrUpdate(account, raw, (_, previous) => Rev(previous) >= Rev(raw) ? previous : raw);
            var number = Deliveries.Count;
            var status = DelayDelivery is { } delayed ? await delayed(number) : DeliveryStatus?.Invoke(number) ?? 200;
            ctx.Response.StatusCode = status;
            if (status == 200) await ctx.Response.WriteAsJsonAsync(new { accepted = true });
        }

        private bool Authorized(HttpContext ctx)
            => ctx.Request.Headers.Authorization == "Bearer " + bearer && ctx.Request.Headers["X-BP-Legal-Entity"] == MappingLegal;

        private static async Task<byte[]> RequestBytes(HttpContext ctx)
        {
            using var stream = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(stream);
            return stream.ToArray();
        }

        public async ValueTask DisposeAsync()
        {
            if (bridge is not null) await bridge.DisposeAsync();
            key.Dispose();
        }
    }
}
