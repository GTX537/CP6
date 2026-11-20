using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Services.Erp;
using CP6.Core.Services.ErpIntegration;
using CP6.Core.Services.Fin;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Erp;
using CP6.Platform.EntityFramework;
using CP6.Platform.Messaging;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static readonly JsonSerializerOptions UseJson = new(JsonSerializerDefaults.Web);
    private const string UseLegal = "JP-A";

    private sealed class UseQueue(DbContextOptions<ErpIntegrationContext> options) : IDbContextFactory<ErpIntegrationContext>
    {
        public ErpIntegrationContext CreateDbContext() => new(options);
    }

    // 模拟 CRM 映射协调者。返回值仍需来自消费者的原 PostgreSQL 连接、事务和当前 BP 共享门。
    private sealed class UseMapping : IErpOrderPartnerUseSource
    {
        private sealed record Witness
        {
            public Guid Tenant { get; init; }
            public Guid Account { get; init; }
            public Guid Partner { get; init; }
            public string Legal { get; init; } = "";
            public string Code { get; init; } = "";
            public long Expected { get; init; }
            public long Current { get; init; }
            public string State { get; init; } = "";
            public bool Conflict { get; init; }
        }

        public async Task<BpUseMappingProof?> ReadAsync(CP6Context db, DbTransaction transaction, ErpOrderRequested original,
            Guid bp, string legalEntityKey, CancellationToken ct)
        {
            BpUseScopeGate.RequireTransaction(db, transaction, serializable: true);
            Assert.True(await db.Database.SqlQuery<bool>($"""
                SELECT main_bp.use_scope_held({db.CurrentTenantId},{legalEntityKey},false) AS "Value"
                """).SingleAsync(ct));
            var row = (await db.Database.SqlQuery<Witness>($"""
                SELECT m.tenant_id AS "Tenant",m.account_id AS "Account",m.bp_id AS "Partner",
                  m.legal_key AS "Legal",m.bp_code AS "Code",i.expected_revision AS "Expected",
                  m.current_revision AS "Current",m.state AS "State",m.owner_conflict AS "Conflict"
                FROM wp0402_fixture.mapping m JOIN wp0402_fixture.intent i USING(tenant_id,account_id)
                WHERE m.tenant_id={db.CurrentTenantId} AND i.request_id={original.RequestId} AND m.account_id={original.AccountId}
                FOR SHARE OF m,i
                """).ToArrayAsync(ct)).SingleOrDefault();
            return row is null ? null : new(transaction, row.Tenant, row.Partner, row.Expected,
                new(row.Account, row.Legal, row.Current.ToString(CultureInfo.InvariantCulture), row.State, row.Code,
                    new string('a', 64)), row.Conflict);
        }
    }

    private sealed class HoldUseCommit : DbTransactionInterceptor
    {
        public bool Enabled { get; set; }
        public TaskCompletionSource Staged { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (Enabled)
            {
                Enabled = false;
                Staged.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(12), cancellationToken);
            }
            return result;
        }
    }

    private static OperationsOwnerV1 OwnedUseAuthority(Harness h, CP6Context db)
    {
        var catalog = new AuthorizationCatalogStoreV1();
        catalog.RegisterScope("erp-business-partner", "业务伙伴", [1, 2, 3, 4, 5], 5);
        catalog.RegisterFields("erp-business-partner", BusinessPartnerWorkspaceV1.PermissionFields);
        catalog.Seal();
        return new(db, new OperationsHttp { HttpContext = h.Http }, _ => Task.FromResult(h.User), catalog.Capture);
    }

    private static BusinessPartnerWorkspaceV1 UseWorkspace(Harness h, CP6Context db, IDataProtectionProvider protection,
        string? financeEnvironment = null)
    {
        var authority = OwnedUseAuthority(h, db);
        return new(db, authority, protection, new BusinessPartnerFinanceReferenceReader(db, authority,
            new ManufacturingFinanceScopeV1(db, financeEnvironment)), TimeProvider.System);
    }

    private static JsonElement UseElement(object? value) => JsonSerializer.SerializeToElement(value, UseJson);
    private static async Task<(Guid Id, string Code)> CreateUsePartner(Harness h, IDataProtectionProvider protection,
        string[]? roles = null, string[]? slots = null, IReadOnlyDictionary<string, Guid>? targets = null,
        string registration = "REGISTERED", Dictionary<string, object?>? values = null, string legal = UseLegal)
    {
        var ws = UseWorkspace(h, h.Db, protection);
        var options = await ws.ReadAsync("OPTIONS", null, legal, null, default);
        var wrapped = new WorkspaceResponse(UseElement(options.Body), options.ETag, 200);
        var code = "USE" + Guid.NewGuid().ToString("N")[..13].ToUpperInvariant();
        var body = WorkspaceInput(wrapped, code, "模拟用途伙伴" + code, registration: registration,
            complete: true, useRoles: roles, useSelf: slots, useTargets: targets, overrides: values,
            legal: legal, site: legal == "JP-B" ? "BPB" : "BPA");
        var preview = await ws.PostAsync("CREATE_PREVIEW", UseElement(body), null, null, null, default);
        var payload = UseElement(preview.Body);
        Assert.True(payload.GetProperty("canApply").GetBoolean(), payload.GetRawText());
        var apply = await ws.PostAsync("CREATE", UseElement(new { preview = payload.GetProperty("reference") }),
            preview.ETag, Guid.NewGuid().ToString("D"), null, default);
        return (UseElement(apply.Body).GetProperty("receipt").GetProperty("bp").GetProperty("bpId").GetGuid(), code);
    }

    private static async Task<(BusinessPartnerWorkspaceV1.HttpResult Context, JsonElement Body)> PrepareUseStatus(
        BusinessPartnerWorkspaceV1 ws, Guid id, string hold)
    {
        var status = await ws.ReadAsync("STATUS_CONTEXT", id, null, null, default);
        return (status, UseElement(new { bpId = id,
            expectedRevision = UseElement(status.Body).GetProperty("state").GetProperty("revision").GetString(),
            hold, reason = "模拟资格变化" }));
    }

    private static Task<BusinessPartnerWorkspaceV1.HttpResult> ApplyUseStatus(BusinessPartnerWorkspaceV1 workspace,
        (BusinessPartnerWorkspaceV1.HttpResult Context, JsonElement Body) input, Guid key)
        => workspace.PostAsync("HOLD", input.Body, input.Context.ETag, key.ToString("D"), null, default);

    private static async Task<UseQueue> PrepareC03Queue(Harness h)
    {
        var provider = new DatabaseOptions(DatabaseProvider.PostgreSql);
        var profile = DatabaseMigrationProfile.For(provider, DatabaseContextKind.ErpIntegration);
        var options = DatabaseContextOptions.Configure(new DbContextOptionsBuilder<ErpIntegrationContext>(), provider,
            h.Db.Database.GetConnectionString()!, profile.MigrationsAssembly, profile.HistoryTable, profile.HistorySchema).Options;
        var factory = new UseQueue(options);
        await using var queue = factory.CreateDbContext();
        await queue.Database.MigrateAsync();
        await h.Db.Database.ExecuteSqlRawAsync("""
            CREATE SCHEMA IF NOT EXISTS wp0402_fixture;
            CREATE TABLE IF NOT EXISTS wp0402_fixture.mapping (
              tenant_id uuid NOT NULL,account_id uuid NOT NULL,bp_id uuid NOT NULL,legal_key text NOT NULL,bp_code text NOT NULL,
              current_revision bigint NOT NULL,state text NOT NULL,owner_conflict boolean NOT NULL,
              PRIMARY KEY(tenant_id,account_id));
            CREATE TABLE IF NOT EXISTS wp0402_fixture.intent (
              tenant_id uuid NOT NULL,account_id uuid NOT NULL,request_id uuid NOT NULL,expected_revision bigint NOT NULL,
              PRIMARY KEY(tenant_id,request_id));
            """);
        return factory;
    }

    private static ErpRequestHandler UseHandler(Harness h, UseQueue queue, IDataProtectionProvider protection,
        Func<string, CancellationToken, Task>? observe = null, IErpOrderPartnerUseSource? source = null)
    {
        var options = new ErpIntegrationOptions
        {
            Enabled = true, Tenants = new() { [h.Tenant] = "local" }, ReaderClientIds = ["模拟读取方"],
            DaprAppToken = new('x', 32), DaprApiToken = new('y', 32)
        };
        var validator = new ErpEventValidator(Cp6ContractBundle.Load(Path.Combine(AppContext.BaseDirectory, "contracts/events/erp")));
        return new(queue, new(options, validator), db => new OrderService(db, h.Workflow, new NoOpWmsBridgeHook(),
            fxRate: new FxRateService(db)), observeStage: observe) { PartnerProtection = protection, PartnerUseSource = source };
    }

    private static async Task<ErpOrderRequested> PrepareUseOrder(Harness h, (Guid Id, string Code) partner, string baseCd = "BPA")
    {
        var tag = Guid.NewGuid().ToString("N")[..12];
        var quote = new Quotation
        {
            TenantId = h.Tenant, QtnNo = "Q" + tag, BaseCd = baseCd, StaffCd = "STA", CustomerCd = partner.Code,
            CurrencyCd = "JPY", OrderType = "1", EstimateCheckFlg = 9, ValidUntilUtc = DateTimeOffset.UtcNow.AddDays(1),
            TotalAmount = 25m, OrderDeliveryDate = DateTime.Today.AddDays(10)
        };
        var estimate = new EstimateCalc
        {
            QtnCalcNo = "E" + tag, QtnBaseCd = baseCd, OrderBaseCd = baseCd, StaffCd = "STA", CustomerCd = partner.Code
        };
        quote.Details.Add(new()
        {
            TenantId = h.Tenant, QtnNo = quote.QtnNo, DetailNo = 1, QtnCalcNo = estimate.QtnCalcNo,
            Unit = "EA", Quantity = 2m, UnitPrice = 12.5m, Amount = 25m
        });
        h.Db.AddRange(estimate, quote, new ProductMaster
        {
            ProductCd = "P" + tag, ItemCd = "P" + tag, Branch1 = "0001", SetProductCd = "P" + tag, Status = 1,
            WfApprovalFlg = true, EstimateCalcNo = estimate.QtnCalcNo, QuotationNo = quote.QtnNo, CustomerCd = partner.Code,
            QtyUnit = "EA", UnitPriceUnit = "EA"
        });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        var authority = new ErpCommerceAuthority(h.Db);
        await authority.AcceptQuotationAsync(quote.QtnNo, new(quote.RowVersion!, "模拟客户明确接受"), "fixture-sales");
        var version = await h.Db.Quotations.AsNoTracking().Where(row => row.QtnNo == quote.QtnNo)
            .Select(row => row.RowVersion!).SingleAsync();
        h.Db.ChangeTracker.Clear();

        var request = new ErpOrderRequested(h.Tenant, Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), partner.Code,
            quote.QtnNo, Convert.ToBase64String(version), 25m, quote.CurrencyCd!);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO wp0402_fixture.mapping VALUES
              ({h.Tenant},{request.AccountId},{partner.Id},{UseLegal},{partner.Code},7,'LINKED',false);
            INSERT INTO wp0402_fixture.intent VALUES ({h.Tenant},{request.AccountId},{request.RequestId},7);
            """);
        return request;
    }

    private static Cp6OutboxEnvelope UseOrderEnvelope(ErpOrderRequested request) =>
        ErpEventContracts.Create(request.TenantId, request.OpportunityId.ToString("D"), request.RequestVersion,
            ErpEventContracts.OrderRequested, request, "local", Guid.NewGuid().ToString("N"), "fixture-use");

    private static Task<Cp6InboxProcessingResult> ConsumeUseOrder(ErpRequestHandler handler, Cp6OutboxEnvelope envelope)
        => handler.ConsumeAsync(envelope.Payload, envelope.TopicName, envelope.PartitionKey);

    private static async Task WaitForUseLock(CP6Context observer, Guid tenant, string mode)
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (!limit.IsCancellationRequested)
        {
            if (await observer.Database.SqlQuery<bool>($"""
                SELECT EXISTS (SELECT FROM pg_catalog.pg_locks
                  WHERE locktype='advisory' AND objsubid=1 AND NOT granted AND mode={mode}
                    AND classid::bigint=((main_bp.use_scope_key({tenant},{UseLegal}) >> 32) & 4294967295)
                    AND objid::bigint=(main_bp.use_scope_key({tenant},{UseLegal}) & 4294967295)) AS "Value"
                """).SingleAsync(limit.Token)) return;
            await Task.Delay(35, limit.Token);
        }
        Assert.Fail("未观察到相同租户和法人的 PostgreSQL Scope 锁等待。");
    }

    private static Task<string[]> UseEffectXids(CP6Context db, ErpOrderRequested command)
        => db.Database.SqlQuery<string>($"""
            SELECT xmin::text AS "Value" FROM main_bp.use_receipts WHERE tenant_id={command.TenantId} AND use_key={command.RequestId}
            UNION ALL SELECT xmin::text FROM public."T_Order" WHERE "TenantId"={command.TenantId} AND "CrmRequestId"={command.RequestId}
            UNION ALL SELECT d.xmin::text FROM public."T_OrderDetail" d JOIN public."T_Order" o
                ON d."TenantId"=o."TenantId" AND d."WebOrderNo"=o."WebOrderNo"
                WHERE o."TenantId"={command.TenantId} AND o."CrmRequestId"={command.RequestId}
            UNION ALL SELECT b.xmin::text FROM erp_integration."OrderBridgeDispatch" b JOIN public."T_Order" o
                ON b."TenantId"=o."TenantId" AND b."OrderKey"=rtrim(o."WebOrderNo"::text) COLLATE "C"
                WHERE o."TenantId"={command.TenantId} AND o."CrmRequestId"={command.RequestId}
            UNION ALL SELECT xmin::text FROM public."Sys_OperLogs" WHERE "TenantId"={command.TenantId}
                AND "Action"='CreateOrderWithPartnerUse' AND "AuditEvidenceJson"::jsonb->>'operationId'={command.RequestId.ToString("D")}
            UNION ALL SELECT xmin::text FROM erp_integration."Request" WHERE "TenantId"={command.TenantId}
                AND "RequestId"={command.RequestId} AND "Succeeded"
            UNION ALL SELECT xmin::text FROM erp_integration."Cp6_OutboxMessage"
                WHERE "TenantId"={command.TenantId} AND "AggregateId"={command.OpportunityId.ToString("D")}
                  AND convert_from("Payload",'UTF8')::jsonb->>'type'={ErpEventContracts.OrderCreated}
            """).ToArrayAsync();

    private sealed record UseWalCommit
    {
        public string Xid { get; init; } = "";
        public string Description { get; init; } = "";
    }

    private static async Task<string> UseOuterCommit(CP6Context db, string before, string[] rowXids)
    {
        // EF 采用保存点写入。xmin 可为子事务号，按真实 WAL COMMIT 核对其外层归属。
        var after = await db.Database.SqlQueryRaw<string>("""
            SELECT pg_current_wal_flush_lsn()::text AS "Value"
            """).SingleAsync();
        var commits = await db.Database.SqlQuery<UseWalCommit>($"""
            SELECT xid::text AS "Xid",description AS "Description"
              FROM public.pg_get_wal_records_info(CAST({before} AS pg_lsn),CAST({after} AS pg_lsn))
            WHERE resource_manager='Transaction' AND record_type='COMMIT'
            """).ToArrayAsync();
        return Assert.Single(commits, commit =>
        {
            var nested = Regex.Match(commit.Description, @"\bsubxacts: ([0-9]+(?: [0-9]+)*)").Groups[1].Value.Split(' ');
            return rowXids.All(xid => xid == commit.Xid || nested.Contains(xid, StringComparer.Ordinal));
        }).Xid;
    }

    private static async Task<string> UseFailure(UseQueue queue, Guid request)
    {
        await using var db = queue.CreateDbContext();
        var row = await db.Requests.AsNoTracking().SingleAsync(row => row.RequestId == request);
        Assert.False(row.Succeeded);
        using var json = JsonDocument.Parse(row.ResultDataJson);
        return json.RootElement.GetProperty("errorCode").GetString()!;
    }

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_wp04_02_actual_C03_use_commits_before_freeze_and_keeps_one_original()
    {
        await using var h = await Seed();
        await ProvisionWorkspace(h);
        await EnablePartnerUse(h.Db, h.Tenant);
        var protection = new EphemeralDataProtectionProvider();
        var partner = await CreateUsePartner(h, protection);
        var queue = await PrepareC03Queue(h);
        var request = await PrepareUseOrder(h, partner);
        var envelope = UseOrderEnvelope(request);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = UseHandler(h, queue, protection, async (stage, ct) =>
        {
            Assert.Equal("order-staged", stage);
            ready.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(14), ct);
        }, new UseMapping());

        await using var writer = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        var ws = UseWorkspace(h, writer, protection);
        var status = await PrepareUseStatus(ws, partner.Id, "FROZEN");
        await h.Db.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS pg_walinspect SCHEMA public");
        var beforeLsn = await h.Db.Database.SqlQueryRaw<string>("""
            SELECT pg_current_wal_insert_lsn()::text AS "Value"
            """).SingleAsync();
        var consume = ConsumeUseOrder(handler, envelope);
        Task<BusinessPartnerWorkspaceV1.HttpResult>? freeze = null;
        try
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(12));
            freeze = ApplyUseStatus(ws, status, Guid.NewGuid());
            await WaitForUseLock(h.Db, h.Tenant, "ExclusiveLock");
            Assert.False(freeze.IsCompleted);
            Assert.Empty(await UseEffectXids(h.Db, request));
        }
        finally
        {
            release.TrySetResult();
            await consume;
            if (freeze is not null) await freeze;
        }

        Assert.Equal(Cp6InboxDisposition.Applied, (await consume).Disposition);
        var xids = await UseEffectXids(h.Db, request);
        Assert.Equal(7, xids.Length);
        var outer = await UseOuterCommit(h.Db, beforeLsn, xids);
        var partnerState = await h.Db.BusinessPartners.AsNoTracking().SingleAsync(row => row.Id == partner.Id);
        Assert.True(partnerState.IsFrozen);
        Assert.Equal(Cp6InboxDisposition.Duplicate, (await ConsumeUseOrder(handler, envelope)).Disposition);
        Assert.Equal(xids.Order(), (await UseEffectXids(h.Db, request)).Order());
        var intent = DirectUse(h, partner) with
        {
            AccountId = request.AccountId, UseKey = request.RequestId, Actor = "crm-integration",
            ExpectedMappingRevision = 7, BusinessEffectKey = request.RequestId.ToString("D"),
            Consumer = new(BpUseParticipant.SalesOwner, request.OpportunityId.ToString("D"), "1",
                ErpEventContracts.OrderInputSha256(request), "SALES_NEW_SOURCE")
        };
        await using (var tx = await h.Db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var participant = new BpUseParticipant(h.Db, protection, TimeProvider.System);
            var original = await participant.EnlistAsync(tx.GetDbTransaction(), intent, default);
            Assert.True(original.Replayed);
            Assert.False(original.Receipt.IsReusablePermit);
            Assert.Equal("1", original.Receipt.Bp.Revision);
            var rawReceipt = await h.Db.Database.SqlQuery<string>($"""
                SELECT receipt AS "Value" FROM main_bp.use_receipts WHERE tenant_id={h.Tenant} AND use_key={request.RequestId}
                """).SingleAsync();
            Assert.Equal(rawReceipt, JsonSerializer.Serialize(original.Receipt, UseJson));
            foreach (var changed in new[]
            {
                intent with { Consumer = intent.Consumer with { Version = "2" } },
                intent with { Purpose = "SALES_CONFIRM", Consumer = intent.Consumer with { Purpose = "SALES_CONFIRM" } },
                intent with { Actor = "模拟其他原请求主体" },
                intent with { UseKey = Guid.NewGuid() }
            })
                Assert.Equal("USE_IDEMPOTENCY_CONFLICT", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() =>
                    participant.EnlistAsync(tx.GetDbTransaction(), changed, default))).Message);
            await tx.CommitAsync();
        }
        var historyBody = UseElement(new { legalEntityKey = UseLegal, bpId = partner.Id, kind = "USE", beforeSequence = (string?)null, limit = 10 });
        var history = UseElement((await ws.PostAsync("HISTORY", historyBody, null, null, null, default)).Body);
        var historic = Assert.Single(history.GetProperty("items").EnumerateArray());
        Assert.Equal(request.RequestId, historic.GetProperty("reference").GetProperty("id").GetGuid());
        Assert.Null(historic.GetProperty("reference").GetProperty("digest").GetString());
        Assert.DoesNotContain(request.AccountId.ToString("D"), historic.GetRawText());
        Assert.Equal("1", historic.GetProperty("changes").EnumerateArray()
            .Single(field => field.GetProperty("field").GetString() == "bpRevision").GetProperty("value").GetString());
        Assert.Equal(1, await UseReceiptCount(h));
        output.WriteLine($"消费先提交。伙伴={partner.Id:D}，原键={request.RequestId:D}，外层 COMMIT={outer}，行事务={string.Join(',', xids)}。");
    }

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_wp04_02_actual_freeze_commits_before_C03_use_and_rejects_new_order()
    {
        await using var h = await Seed();
        await ProvisionWorkspace(h);
        await EnablePartnerUse(h.Db, h.Tenant);
        var protection = new EphemeralDataProtectionProvider();
        var partner = await CreateUsePartner(h, protection);
        var queue = await PrepareC03Queue(h);
        var request = await PrepareUseOrder(h, partner);
        var hold = new HoldUseCommit();
        var handler = UseHandler(h, queue, protection, source: new UseMapping());
        await using var writer = server.Context(new TenantContext { CurrentTenantId = h.Tenant }, null, hold);
        var ws = UseWorkspace(h, writer, protection);
        var status = await PrepareUseStatus(ws, partner.Id, "FROZEN");
        hold.Enabled = true;
        var freeze = ApplyUseStatus(ws, status, Guid.NewGuid());
        Task<Cp6InboxProcessingResult>? consume = null;
        try
        {
            await hold.Staged.Task.WaitAsync(TimeSpan.FromSeconds(10));
            consume = ConsumeUseOrder(handler, UseOrderEnvelope(request));
            await WaitForUseLock(h.Db, h.Tenant, "ShareLock");
            Assert.False(consume.IsCompleted);
        }
        finally
        {
            hold.Release.TrySetResult();
            await freeze;
            if (consume is not null) await consume;
        }
        Assert.Equal(Cp6InboxDisposition.Applied, consume!.Result.Disposition);
        Assert.Equal("C03_BP_FROZEN", await UseFailure(queue, request.RequestId));
        Assert.Empty(await UseEffectXids(h.Db, request));
        output.WriteLine($"冻结先提交。伙伴={partner.Id:D}，被拒绝原键={request.RequestId:D}。");
    }

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_wp04_02_outer_rollback_and_business_rejection_leave_no_use_or_order()
    {
        await using var h = await Seed();
        await ProvisionWorkspace(h, alternate: true);
        await EnablePartnerUse(h.Db, h.Tenant);
        var protection = new EphemeralDataProtectionProvider();
        var partner = await CreateUsePartner(h, protection);
        var queue = await PrepareC03Queue(h);
        var request = await PrepareUseOrder(h, partner);
        var staged = false;
        var rolledBack = UseHandler(h, queue, protection, (_, _) =>
        {
            staged = true;
            throw new InvalidOperationException("模拟成功写集已暂存后的外层故障");
        }, new UseMapping());
        await ConsumeUseOrder(rolledBack, UseOrderEnvelope(request));
        Assert.True(staged);
        Assert.Empty(await UseEffectXids(h.Db, request));
        Assert.Equal("C03_ERP_UNAVAILABLE", await UseFailure(queue, request.RequestId));

        var rejected = (await PrepareUseOrder(h, partner)) with { ExpectedAmount = 27m };
        await ConsumeUseOrder(UseHandler(h, queue, protection, source: new UseMapping()), UseOrderEnvelope(rejected));
        Assert.Equal("C03_AMOUNT_MISMATCH", await UseFailure(queue, rejected.RequestId));
        Assert.Empty(await UseEffectXids(h.Db, rejected));

        var unknown = await PrepareUseOrder(h, partner);
        await ConsumeUseOrder(UseHandler(h, queue, protection), UseOrderEnvelope(unknown));
        Assert.Equal("C03_OWNER_PARTICIPANT_UNPROVEN", await UseFailure(queue, unknown.RequestId));
        Assert.Empty(await UseEffectXids(h.Db, unknown));

        var changedMapping = await PrepareUseOrder(h, partner);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE wp0402_fixture.mapping SET current_revision=8
            WHERE tenant_id={h.Tenant} AND account_id={changedMapping.AccountId}
            """);
        await ConsumeUseOrder(UseHandler(h, queue, protection, source: new UseMapping()), UseOrderEnvelope(changedMapping));
        Assert.Equal("C03_OWNER_PARTICIPANT_UNPROVEN", await UseFailure(queue, changedMapping.RequestId));
        Assert.Empty(await UseEffectXids(h.Db, changedMapping));

        var wrongLegal = await PrepareUseOrder(h, partner, "BPB");
        await ConsumeUseOrder(UseHandler(h, queue, protection, source: new UseMapping()), UseOrderEnvelope(wrongLegal));
        Assert.Equal("C03_OWNER_PARTICIPANT_UNPROVEN", await UseFailure(queue, wrongLegal.RequestId));
        Assert.Empty(await UseEffectXids(h.Db, wrongLegal));
    }
}
