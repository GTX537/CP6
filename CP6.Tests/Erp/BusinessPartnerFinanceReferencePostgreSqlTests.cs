using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Fin;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Fin;
using CP6.Entity.DomainModels.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Reader = CP6.Core.Services.Fin.BusinessPartnerFinanceReferenceReader;
using SourceRef = CP6.Core.Services.Fin.BusinessPartnerFinanceSourceRefV1;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private const string BpFinanceApi = "api/business-partners/finance-references-v1";
    private static readonly JsonSerializerOptions BpFinanceJson = new(JsonSerializerDefaults.Web);

    private static async Task<BusinessPartnerFinanceReferenceViewV1> FinanceReference(AuthenticationApiProcess host,
        Dictionary<string, string> cookies, Guid partner, string field, Guid? context = null, string? bpRevision = null)
    {
        using var response = await host.Send(FinanceReferenceRequest(cookies, partner, field, context, bpRevision));
        var root = await PartnerResponse(response, HttpStatusCode.OK);
        return root.GetProperty("data").Deserialize<BusinessPartnerFinanceReferenceViewV1>(BpFinanceJson)!;
    }

    private static HttpRequestMessage FinanceReferenceRequest(Dictionary<string, string> cookies, Guid partner,
        string field, Guid? context, string? bpRevision = null)
    {
        var url = $"{BpFinanceApi}/{partner:D}?field={field}";
        if (bpRevision != null) url += "&bpRevision=" + Uri.EscapeDataString(bpRevision);
        var request = OperationsRequest(cookies, HttpMethod.Get, url);
        if (context != null) request.Headers.Add("X-Finance-Context", context.Value.ToString("D"));
        return request;
    }

    private async Task<(GlActors Actors, Guid Context)> SeedBpFinance(Harness h, string tag)
    {
        await GrantBusinessPartnerIdentity(h);
        await GrantPartnerActions(h, Reader.ReadAction);
        var actors = await SeedGlBoard(h, tag);
        var scope = new ManufacturingFinanceScopeV1(h.Db, ScopeEnvironment.ToString("D"));
        var context = await scope.IssueContextAsync(actors.Ledger, h.User, default);
        return (actors, context);
    }

    private static async Task<Guid> CreateLegacyFundsBank(Harness h, string account)
    {
        var gl = new GlAccount
        {
            Id = Guid.NewGuid(), Code = "SIM-FUNDS-GL", Name = "模拟本方存款科目",
            Type = AccountType.Asset, NormalSide = AccountSide.Debit
        };
        h.Db.GlAccounts.Add(gl);
        await h.Db.SaveChangesAsync();
        var bank = await new ApMasterService(h.Db).CreateBankAccountAsync(new BankAccount
        {
            Code = "SIM-FUNDS", Name = "模拟本方资金账户", AccountNo = account, GlAccountId = gl.Id, CurrencyCd = "USD"
        }, "price-owner");
        h.Db.ChangeTracker.Clear();
        return bank;
    }

    private static async Task<SourceRef> NativeFinanceReference(Harness h, string owner, Guid id)
        => await h.Db.Database.SqlQuery<SourceRef>($"""
            SELECT v.owner AS "Owner",v.source_id AS "Id",v.version AS "Version",v.digest AS "Digest",v.purpose AS "Purpose"
            FROM cp6_bp_finance_ref_v1.versions v JOIN cp6_bp_finance_ref_v1.frontiers f
            USING (tenant_id,owner,source_id,version)
            WHERE v.tenant_id={h.Tenant} AND v.owner={owner} AND v.source_id={id.ToString("D")}
            """).SingleAsync();

    // 测试仅模拟受信来源登记和已经保存的 BP 链接。HTTP、权限、当前原件校验和事务门使用 Main 实现。
    private static Task<int> RegisterFinanceScope(Harness h, Guid ledger, SourceRef reference, Guid? subject)
        => h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_bp_finance_ref_v1.scopes(tenant_id,owner,source_id,ledger_id,partner_id)
            VALUES ({h.Tenant},{reference.Owner},{reference.Id},{ledger},NULLIF({subject ?? Guid.Empty},'00000000-0000-0000-0000-000000000000'::uuid))
            """);

    private static Task<int> StoreFinanceLink(Harness h, Guid partner, Guid ledger, string field, SourceRef reference, long revision = 1)
        => h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_bp_finance_ref_v1.links(tenant_id,partner_id,bp_revision,field,ledger_id,owner,source_id,version,digest,purpose)
            VALUES ({h.Tenant},{partner},{revision},{field},{ledger},{reference.Owner},{reference.Id},
                {reference.Version},{reference.Digest},{reference.Purpose})
            """);

    private static async Task<SourceRef> SimulatedBankPublication(Harness h, Guid ledger, Guid source, Guid version, long generation, string mask)
    {
        const string owner = "FIN-MASTER";
        const string purpose = "BP_PAYMENT_BANK_REFERENCE";
        var id = source.ToString("D");
        var exact = version.ToString("D");
        var envelope = JsonSerializer.Serialize(new { simulated = true, owner, id, version = exact, purpose, currency = "USD", maskedAccount = mask });
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(envelope))).ToLowerInvariant();
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_bp_finance_ref_v1.versions(tenant_id,owner,source_id,version,purpose,envelope,digest,masked_account,currency,publisher_ledger_id)
            VALUES ({h.Tenant},{owner},{id},{exact},{purpose},{envelope},{digest},{mask},'USD',{ledger});
            INSERT INTO cp6_bp_finance_ref_v1.frontiers(tenant_id,owner,source_id,version,generation,availability,enabled)
            VALUES ({h.Tenant},{owner},{id},{exact},1,'ONLINE',true)
            ON CONFLICT (tenant_id,owner,source_id) DO UPDATE SET version=EXCLUDED.version,generation={generation}
            """);
        return new(owner, id, exact, digest, purpose);
    }

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_finance_reference_http_does_not_promote_old_credit_to_a_proven_reference()
    {
        await using var h = await Seed();
        var (_, context) = await SeedBpFinance(h, "missing-source");
        var id = await SeedIdentityPartner(h);
        await h.Db.BusinessPartners.Where(p => p.Id == id).ExecuteUpdateAsync(update => update
            .SetProperty(p => p.BankCd, "SAME-CODE").SetProperty(p => p.RemittanceAccount, "5333444455556666")
            .SetProperty(p => p.CreditLimit, 0m));
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, GlHostSettings());
        var cookies = await Login(host, h);
        foreach (var field in new[] { Reader.ReceiptBank, Reader.PaymentBank, Reader.CreditMaster })
        {
            foreach (var token in new Guid?[] { null, context })
            {
                var r = await FinanceReference(host, cookies, id, field, token);
                Assert.Equal("UNPROVEN", r.Availability);
                Assert.Null(r.Reference);
                Assert.Null(r.Enabled);
                Assert.Null(r.CreditLimit);
                Assert.Null(r.MaskedAccount);
                Assert.False(r.IsExecutionPermit);
            }
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_finance_reference_http_recovers_exact_native_bank_and_credit_observations_with_scope_and_permissions()
    {
        await using var h = await Seed();
        var bp = await SeedIdentityPartner(h);
        var bank = await CreateLegacyFundsBank(h, "6111222233334444");
        var (actors, token) = await SeedBpFinance(h, "native-finance");
        var bankRef = await NativeFinanceReference(h, Reader.LegacyBankOwner, bank);
        var creditRef = await NativeFinanceReference(h, Reader.LegacyCreditOwner, bp);
        await RegisterFinanceScope(h, actors.Ledger, bankRef, null);
        await RegisterFinanceScope(h, actors.Ledger, creditRef, bp);
        await StoreFinanceLink(h, bp, actors.Ledger, Reader.ReceiptBank, bankRef);
        await StoreFinanceLink(h, bp, actors.Ledger, Reader.PaymentBank, bankRef);
        await StoreFinanceLink(h, bp, actors.Ledger, Reader.CreditMaster, creditRef);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, GlHostSettings());
        var cookies = await Login(host, h);
        // 实际 GL options 按当前用户签发已登记账套上下文。
        var client = new GlClient(host, cookies);
        await client.AuthenticateBook();
        Assert.Equal(token, client.Context);

        var bankRead = await FinanceReference(host, cookies, bp, Reader.ReceiptBank, token);
        Assert.Equal(bankRef, bankRead.Reference);
        Assert.Equal("****4444", bankRead.MaskedAccount);
        Assert.Equal("USD", bankRead.Currency);
        Assert.Equal(new FinanceScopeV1(ScopeEnvironment.ToString("D"), h.Tenant, "SIM-LEGAL", "SIM-BOOK", "REAL"), bankRead.Scope);
        Assert.True(bankRead.Enabled);
        Assert.False(bankRead.IsExecutionPermit);

        var wrongBank = await FinanceReference(host, cookies, bp, Reader.PaymentBank, token);
        Assert.Equal("UNPROVEN", wrongBank.Availability);
        Assert.Equal("NOT_THIS_KIND", wrongBank.Reason);
        Assert.Null(wrongBank.Reference);
        Assert.Null(wrongBank.MaskedAccount);

        var credit = await FinanceReference(host, cookies, bp, Reader.CreditMaster, token);
        Assert.Equal(creditRef, credit.Reference);
        Assert.Equal("123456.78", credit.CreditLimit);
        Assert.Null(credit.Currency);
        Assert.False(credit.Enabled);
        Assert.Equal("LEGACY_CREDIT_OBSERVATION", credit.Reason);
        Assert.False(credit.IsExecutionPermit);

        await h.Db.BankAccounts.Where(row => row.Id == bank).ExecuteUpdateAsync(set => set.SetProperty(row => row.AccountNo, "6111222233335555"));
        await h.Db.BusinessPartners.Where(row => row.Id == bp).ExecuteUpdateAsync(set => set.SetProperty(row => row.CreditLimit, 0m));
        var oldBank = await FinanceReference(host, cookies, bp, Reader.ReceiptBank, token, "1");
        var oldCredit = await FinanceReference(host, cookies, bp, Reader.CreditMaster, token, "1");
        Assert.Equal(bankRef, oldBank.Reference);
        Assert.Equal("****4444", oldBank.MaskedAccount);
        Assert.Equal(creditRef, oldCredit.Reference);
        Assert.Equal("123456.78", oldCredit.CreditLimit);
        Assert.All(new[] { oldBank, oldCredit }, read =>
        {
            Assert.False(read.Enabled);
            Assert.Equal("REFERENCE_VERSION_CHANGED", read.Reason);
        });
        var zero = await NativeFinanceReference(h, Reader.LegacyCreditOwner, bp);
        await StoreFinanceLink(h, bp, actors.Ledger, Reader.CreditMaster, zero, 2);
        var zeroRead = await FinanceReference(host, cookies, bp, Reader.CreditMaster, token);
        Assert.Equal("0", zeroRead.CreditLimit);
        Assert.False(zeroRead.Enabled);
        await h.Db.BusinessPartners.Where(row => row.Id == bp).ExecuteUpdateAsync(set => set.SetProperty(row => row.CreditLimit, (decimal?)null));
        var unset = await NativeFinanceReference(h, Reader.LegacyCreditOwner, bp);
        await StoreFinanceLink(h, bp, actors.Ledger, Reader.CreditMaster, unset, 3);
        var unsetRead = await FinanceReference(host, cookies, bp, Reader.CreditMaster, token);
        Assert.Null(unsetRead.CreditLimit);
        Assert.False(unsetRead.Enabled);

        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = PartnerResource, FieldName = Reader.CreditMaster, Access = 3 });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        var masked = await FinanceReference(host, cookies, bp, Reader.CreditMaster, token, "1");
        Assert.Equal("NOT_AUTHORIZED", masked.Availability);
        Assert.Null(masked.Reference);
        Assert.Null(masked.CreditLimit);
        Assert.Null(masked.Scope);

        await using var foreign = await Seed();
        await GrantBusinessPartnerIdentity(foreign);
        await GrantPartnerActions(foreign, Reader.ReadAction);
        var sameCode = await SeedIdentityPartner(foreign);
        var code = await h.Db.BusinessPartners.Where(row => row.Id == bp).Select(row => row.BpCd).SingleAsync();
        await foreign.Db.BusinessPartners.Where(row => row.Id == sameCode).ExecuteUpdateAsync(set => set.SetProperty(row => row.BpCd, code));
        using var wrongTenant = await host.Send(FinanceReferenceRequest(cookies, sameCode, Reader.ReceiptBank, token));
        Assert.Equal(HttpStatusCode.Forbidden, wrongTenant.StatusCode);
        await DeviceEvidence("wp11-65-native-history.json", new
        {
            simulated = "用户、租户、两份同代码伙伴、账套、资金账户、原链接、信用数据",
            bank = bankRead, oldBank, credit, oldCredit, zero = zeroRead.CreditLimit, unset = unsetRead.CreditLimit,
            wrongBank = wrongBank.Reason, redacted = masked.Availability, foreignTenant = (int)wrongTenant.StatusCode
        });
    }

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_finance_reference_http_preserves_published_reference_at_int64_boundaries_for_new_actor()
    {
        await using var h = await Seed();
        var (actors, token) = await SeedBpFinance(h, "published-finance");
        var bp = await SeedIdentityPartner(h);
        var source = Guid.NewGuid();
        var original = await SimulatedBankPublication(h, actors.Ledger, source, Guid.NewGuid(), 1, "****1111");
        await RegisterFinanceScope(h, actors.Ledger, original, bp);
        await StoreFinanceLink(h, bp, actors.Ledger, Reader.PaymentBank, original);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, GlHostSettings());
        var maker = await Login(host, h);
        var reader = await Login(host, "gl-checker-published-finance", "Price1!Password", h.Tenant.ToString("N"));
        using var borrowed = await host.Send(FinanceReferenceRequest(reader, bp, Reader.PaymentBank, token));
        Assert.Equal(HttpStatusCode.NotFound, borrowed.StatusCode);
        var client = new GlClient(host, reader);
        await client.AuthenticateBook();
        Assert.NotEqual(token, client.Context);
        var current = await FinanceReference(host, reader, bp, Reader.PaymentBank, client.Context);
        Assert.Equal(original, current.Reference);
        Assert.True(current.Enabled);

        var alternate = Guid.NewGuid();
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_fin_mfg_v1.ledgers(tenant_id,id,environment_id,mode,legal_entity_key,ledger_key,currency,business_date)
            VALUES ({h.Tenant},{alternate},{ScopeEnvironment.ToString("D")},'REAL','SIM-LEGAL','SIM-OTHER-BOOK','CNY',{GlBusinessDay});
            INSERT INTO cp6_fin_mfg_v1.members(tenant_id,ledger_id,subject_id) VALUES ({h.Tenant},{alternate},{h.User});
            """);
        var scope = new ManufacturingFinanceScopeV1(h.Db, ScopeEnvironment.ToString("D"));
        var otherToken = await scope.IssueContextAsync(alternate, h.User, default);
        var other = await FinanceReference(host, maker, bp, Reader.PaymentBank, otherToken);
        Assert.Equal("UNPROVEN", other.Availability);
        Assert.Null(other.Reference);

        await SimulatedBankPublication(h, actors.Ledger, source, Guid.NewGuid(), 2, "****2222");
        var high = new long[] { 9007199254740991, 9007199254740992, 9007199254740993, long.MaxValue };
        // 仅在本测试模拟已运行到高位的身份历史，原体、摘要及当前读取走真实 Owner。
        await using (var tx = await h.Db.Database.BeginTransactionAsync())
        {
            await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
                SET CONSTRAINTS ALL IMMEDIATE;
                INSERT INTO cp6_business_partner_identity_v1.versions(tenant_id,partner_id,revision,source_present,source_json,source_digest)
                SELECT tenant_id,partner_id,high.revision,source_present,source_json,source_digest
                FROM cp6_business_partner_identity_v1.versions CROSS JOIN unnest({high}) AS high(revision)
                WHERE tenant_id={h.Tenant} AND partner_id={bp} AND cp6_business_partner_identity_v1.versions.revision=1;
                ALTER TABLE cp6_business_partner_identity_v1.heads DISABLE TRIGGER business_partner_identity_head_monotonic;
                UPDATE cp6_business_partner_identity_v1.heads SET revision={long.MaxValue} WHERE tenant_id={h.Tenant} AND partner_id={bp};
                ALTER TABLE cp6_business_partner_identity_v1.heads ENABLE TRIGGER business_partner_identity_head_monotonic;
                """);
            await tx.CommitAsync();
        }

        var observed = new List<string>();
        foreach (var version in high)
        {
            var exact = version.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var previous = await FinanceReference(host, reader, bp, Reader.PaymentBank, client.Context, exact);
            Assert.Equal(exact, previous.BpRevision);
            Assert.Equal(original, previous.Reference);
            Assert.Equal("****1111", previous.MaskedAccount);
            Assert.False(previous.Enabled);
            Assert.Equal("REFERENCE_VERSION_CHANGED", previous.Reason);
            observed.Add(previous.BpRevision!);
        }
        var exactNumbers = observed.Select(Reader.ParseBpRevision).ToArray();
        Assert.True(exactNumbers[0] < exactNumbers[1]);
        Assert.True(exactNumbers[1] < exactNumbers[2] && exactNumbers[2] > exactNumbers[1]);
        Assert.Equal(long.MaxValue, exactNumbers[3]);
        foreach (var invalid in new[] { "9223372036854775808", "09007199254740993", "0", "-1", "9e15", "", " 1 " })
        {
            using var rejected = await host.Send(FinanceReferenceRequest(reader, bp, Reader.PaymentBank, client.Context, invalid));
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<BusinessPartnerFinanceReferenceViewV1>(
            """{"availability":"FOUND","bpRevision":9007199254740993}""", BpFinanceJson));
        var authority = PartnerAuthority(h, financeFields: true);
        var provider = new Reader(h.Db, authority, new(h.Db, ScopeEnvironment.ToString("D")));
        var link = await authority.ExecuteAsync(PartnerResource, "query", false, (access, ct) =>
            provider.ReadFinanceLinkInUowAsync(access, h.Db.Database.CurrentTransaction!.GetDbTransaction(),
                bp, token, "SIM-LEGAL", Reader.PaymentBank, ct));
        Assert.NotNull(link);
        Assert.Equal(bp, link.RequestingBpId);
        Assert.Equal("SIM-LEGAL", link.LegalEntityKey);
        Assert.Equal("PAYMENT_BANK", link.Kind);
        Assert.Equal(long.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture), link.BpRevision);
        Assert.Equal(DateTimeKind.Utc, link.ReadAt.Kind);
        Assert.Equal("REAL", link.Mode);
        Assert.Equal(original, link.Reference);
        await DeviceEvidence("wp11-65-int64-and-recovery.json", new
        {
            simulated = "FIN-MASTER 发布原件和一次后继发布、高位 BP 历史、不同主体及账套",
            borrowed = (int)borrowed.StatusCode, alternate = other.Availability, observed,
            original, current.Enabled, persistedRefUnchanged = true, storedLink = link
        });
    }

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_finance_reference_keeps_scoped_owner_credit_precision_and_declines_unproven_originals()
    {
        await using var h = await Seed();
        var (actors, context) = await SeedBpFinance(h, "exact-owner-credit");
        var bp = await SeedIdentityPartner(h);
        var wrongSubject = await SeedIdentityPartner(h);
        var wrongDigest = await SeedIdentityPartner(h);
        const string amount = "123456789012345678901234567890.12345678";
        const string currency = "JPY";
        const string owner = "FIN-CREDIT";
        const string purpose = "BP_CREDIT_MASTER_REFERENCE";
        var id = Guid.NewGuid().ToString("D");
        const string exactVersion = "CREDIT/V0004";
        var envelope = JsonSerializer.Serialize(new
        {
            simulated = true, owner, id, version = exactVersion, purpose, bp, currency, limit = amount
        });
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(envelope))).ToLowerInvariant();
        var original = new SourceRef(owner, id, exactVersion, digest, purpose);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_bp_finance_ref_v1.versions(tenant_id,owner,source_id,version,purpose,envelope,digest,amount,currency,publisher_ledger_id)
            VALUES ({h.Tenant},{owner},{id},{exactVersion},{purpose},{envelope},{digest},{amount}::numeric,{currency},{actors.Ledger});
            INSERT INTO cp6_bp_finance_ref_v1.frontiers (tenant_id,owner,source_id,version,generation,availability,enabled)
            VALUES ({h.Tenant},{owner},{id},{exactVersion},1,'ONLINE',true)
            """);
        await RegisterFinanceScope(h, actors.Ledger, original, bp);
        await StoreFinanceLink(h, bp, actors.Ledger, Reader.CreditMaster, original);
        await StoreFinanceLink(h, wrongSubject, actors.Ledger, Reader.CreditMaster, original);
        var otherSource = await SimulatedBankPublication(h, actors.Ledger, Guid.NewGuid(), Guid.NewGuid(), 1, "****2222");
        await RegisterFinanceScope(h, actors.Ledger, otherSource, wrongDigest);
        await StoreFinanceLink(h, wrongDigest, actors.Ledger, Reader.PaymentBank, otherSource with { Digest = new string('a', 64) });

        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, GlHostSettings());
        var cookies = await Login(host, h);
        var credit = await FinanceReference(host, cookies, bp, Reader.CreditMaster, context);
        Assert.Equal(original, credit.Reference);
        Assert.Equal(amount, credit.CreditLimit);
        Assert.Equal(currency, credit.Currency);
        Assert.Equal("FOUND", credit.Availability);
        Assert.False(credit.IsExecutionPermit);
        var wrongBound = await FinanceReference(host, cookies, wrongSubject, Reader.CreditMaster, context);
        Assert.Equal("UNPROVEN", wrongBound.Availability);
        Assert.Null(wrongBound.Reference);
        var invalid = await FinanceReference(host, cookies, wrongDigest, Reader.PaymentBank, context);
        Assert.Equal("UNPROVEN", invalid.Availability);
        Assert.Null(invalid.Reference);
        Assert.Null(invalid.MaskedAccount);

        var demo = Guid.NewGuid();
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_fin_mfg_v1.ledgers(tenant_id,id,environment_id,mode,legal_entity_key,ledger_key,currency,business_date)
            VALUES ({h.Tenant},{demo},{ScopeEnvironment.ToString("D")},'DEMO','SIM-LEGAL','SIM-BOOK','CNY',{GlBusinessDay});
            INSERT INTO cp6_fin_mfg_v1.members(tenant_id,ledger_id,subject_id) VALUES ({h.Tenant},{demo},{h.User});
            """);
        var scope = new ManufacturingFinanceScopeV1(h.Db, ScopeEnvironment.ToString("D"));
        var demoToken = await scope.IssueContextAsync(demo, h.User, default);
        using var wrongMode = await host.Send(FinanceReferenceRequest(cookies, bp, Reader.CreditMaster, demoToken));
        Assert.Equal(HttpStatusCode.NotFound, wrongMode.StatusCode);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_fin_mfg_v1.contexts SET expires_at=clock_timestamp()-interval '1 second' WHERE token={context}
            """);
        using var expired = await host.Send(FinanceReferenceRequest(cookies, bp, Reader.CreditMaster, context));
        Assert.Equal(HttpStatusCode.NotFound, expired.StatusCode);
        await DeviceEvidence("wp11-65-credit-exact-and-absence.json", new
        {
            simulated = "精确信用原件、账套、用户、串主体链接、错误摘要及演示域",
            credit, wrongSubject = wrongBound.Availability, badDigest = invalid.Availability,
            wrongMode = (int)wrongMode.StatusCode, expiredContext = (int)expired.StatusCode
        });
    }

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_finance_reference_enlists_exact_current_source_and_rolls_back_at_final_decision()
    {
        await using var h = await Seed();
        var bp = await SeedIdentityPartner(h);
        var bank = await CreateLegacyFundsBank(h, "4111222233334444");
        var (actors, context) = await SeedBpFinance(h, "finance-race");
        var reference = await NativeFinanceReference(h, Reader.LegacyBankOwner, bank);
        await RegisterFinanceScope(h, actors.Ledger, reference, null);
        await StoreFinanceLink(h, bp, actors.Ledger, Reader.ReceiptBank, reference);
        var authority = PartnerAuthority(h, financeFields: true);
        var reader = new Reader(h.Db, authority, new(h.Db, ScopeEnvironment.ToString("D")));
        var request = new BusinessPartnerFinanceUseV1(Reader.ReceiptBank, reference);
        Task<int>? writer = null;
        BusinessPartnerFinanceCurrentV1 held;
        try
        {
            held = await authority.ExecuteAsync(PartnerResource, "query", true, async (access, ct) =>
            {
                var tx = h.Db.Database.CurrentTransaction!.GetDbTransaction();
                var result = await reader.EnlistAsync(access, tx, bp, context, "1", [request], ct);
                Assert.Equal("VALID", result.Decision);
                Assert.True(result.HeldUntilTransactionEnd);
                Assert.NotNull(result.GuardRevision);
                Assert.True(Reader.ParseBpRevision(result.GuardRevision) > 0);
                Assert.Equal(reference, Assert.Single(result.Observations!).Reference);
                Assert.Equal("UNPROVEN", (await reader.EnlistAsync(access, tx, bp, context, "1", [request, request], ct)).Decision);
                var missing = request with { Field = Reader.PaymentBank };
                Assert.Equal("UNPROVEN", (await reader.EnlistAsync(access, tx, bp, context, "1", [request, missing], ct)).Decision);
                writer = Task.Run(async () =>
                {
                    await using var peer = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
                    return await peer.Database.ExecuteSqlInterpolatedAsync($"""
                        UPDATE public."Fin_BankAccount" SET "AccountNo"={"4111222233335555"} WHERE "Id"={bank}
                        """);
                });
                await using var monitor = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                while (!await monitor.Database.SqlQueryRaw<bool>("""
                    SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock'
                        AND query LIKE 'UPDATE public."Fin_BankAccount"%') AS "Value"
                    """).SingleAsync(timeout.Token))
                    await Task.Delay(20, timeout.Token);
                Assert.False(writer.IsCompleted);
                return result;
            });
        }
        finally
        {
            if (writer != null) await writer;
        }

        await using (var unrelated = server.Context(new TenantContext { CurrentTenantId = h.Tenant }))
        await using (var unrelatedTx = await unrelated.Database.BeginTransactionAsync())
        {
            var detached = await Assert.ThrowsAsync<OrderPriceCorrectionException>(() =>
                authority.ExecuteAsync(PartnerResource, "query", false, (access, ct) =>
                    reader.EnlistAsync(access, unrelatedTx.GetDbTransaction(), bp, context, "1", [request], ct)));
            Assert.Equal("AUTHORITY_FENCE_UNAVAILABLE", detached.Message);
        }
        var stale = await authority.ExecuteAsync(PartnerResource, "query", true, (access, ct) =>
            reader.EnlistAsync(access, h.Db.Database.CurrentTransaction!.GetDbTransaction(), bp, context, "1", [request], ct));
        Assert.Equal("DENIED", stale.Decision);
        Assert.Equal("REFERENCE_VERSION_CHANGED", stale.Reason);
        var latest = await NativeFinanceReference(h, Reader.LegacyBankOwner, bank);
        await h.Db.BusinessPartners.Where(row => row.Id == bp).ExecuteUpdateAsync(set => set.SetProperty(row => row.BpName, "复核后继伙伴"));
        await StoreFinanceLink(h, bp, actors.Ledger, Reader.ReceiptBank, latest, 2);
        var rejected = await Assert.ThrowsAsync<OrderPriceCorrectionException>(() =>
            authority.ExecuteAsync(PartnerResource, "query", true, async (access, ct) =>
            {
                var actual = await reader.EnlistAsync(access, h.Db.Database.CurrentTransaction!.GetDbTransaction(),
                    bp, context, "2", [request with { Reference = latest }], ct);
                Assert.True(actual.HeldUntilTransactionEnd);
                await h.Db.BankAccounts.Where(row => row.Id == bank)
                    .ExecuteUpdateAsync(set => set.SetProperty(row => row.IsActive, false), ct);
                return true;
            }));
        Assert.Equal(412, rejected.Status);
        Assert.Equal("BP_FINANCE_SOURCE_CHANGED", rejected.Message);
        Assert.Equal(latest, await NativeFinanceReference(h, Reader.LegacyBankOwner, bank));
        Assert.True(await h.Db.BankAccounts.Where(row => row.Id == bank).Select(row => row.IsActive).SingleAsync());
        await DeviceEvidence("wp11-65-transaction.json", new
        {
            simulated = "资金账户、伙伴、来源登记与同库消费者",
            held.Decision, held.HeldUntilTransactionEnd, held.GuardRevision, independentWriterWaited = true, detachedTransactionRejected = true, stale = stale.Reason,
            finalDecision = rejected.Message, bankAndVersionRolledBack = true
        });
    }

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_finance_reference_offline_does_not_gate_native_freeze_or_retire()
    {
        await using var h = await Seed();
        var (actors, context) = await SeedBpFinance(h, "finance-offline");
        await GrantPartnerActions(h, "edit", "del");
        h.Db.Sys_Roles.Add(new() { RoleId = 1, RoleName = "模拟管理员" });
        await h.Db.Sys_Users.Where(user => user.Id == h.User).ExecuteUpdateAsync(set => set.SetProperty(user => user.RoleId, 1));
        h.Db.Sys_UserRoles.Add(new() { UserId = h.User, RoleId = 72 });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        var bp = await SeedIdentityPartner(h);
        var source = await SimulatedBankPublication(h, actors.Ledger, Guid.NewGuid(), Guid.NewGuid(), 1, "****5555");
        await RegisterFinanceScope(h, actors.Ledger, source, bp);
        await StoreFinanceLink(h, bp, actors.Ledger, Reader.PaymentBank, source);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_bp_finance_ref_v1.frontiers SET availability='OFFLINE', generation=generation+1
            WHERE tenant_id={h.Tenant} AND owner={source.Owner} AND source_id={source.Id}
            """);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, GlHostSettings());
        var cookies = await Login(host, h);
        var unavailable = await FinanceReference(host, cookies, bp, Reader.PaymentBank, context);
        Assert.Equal("UNPROVEN", unavailable.Availability);
        Assert.Null(unavailable.Reference);
        Assert.Null(unavailable.Enabled);
        await using var outage = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        await using var locked = await outage.Database.BeginTransactionAsync();
        await outage.Database.ExecuteSqlRawAsync("LOCK TABLE cp6_bp_finance_ref_v1.frontiers IN ACCESS EXCLUSIVE MODE");
        var lockedRead = await FinanceReference(host, cookies, bp, Reader.PaymentBank, context).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("UNPROVEN", lockedRead.Availability);
        Assert.Null(lockedRead.Reference);
        var authority = PartnerAuthority(h, financeFields: true);
        var provider = new Reader(h.Db, authority, new(h.Db, ScopeEnvironment.ToString("D")));
        var stored = await authority.ExecuteAsync(PartnerResource, "query", false, (access, ct) =>
            provider.ReadFinanceLinkInUowAsync(access, h.Db.Database.CurrentTransaction!.GetDbTransaction(),
                bp, context, "SIM-LEGAL", Reader.PaymentBank, ct));
        Assert.NotNull(stored);
        Assert.Equal(source, stored.Reference);
        // WP04-01 的独立状态命令由模拟消费者发出；使用真实 Main 授权和 BP 状态原子写入。
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await PartnerAuthority(h, financeFields: true).ExecuteAsync(PartnerResource, "edit", true, async (access, ct) =>
        {
            var updated = await access.BusinessPartners().Where(p => p.Id == bp)
                .ExecuteUpdateAsync(set => set.SetProperty(p => p.IsFrozen, true), ct);
            Assert.Equal(1, updated);
            return updated;
        }, limit.Token);
        var row = await h.Db.BusinessPartners.AsNoTracking().SingleAsync(p => p.Id == bp);
        // 直接走 Main 退休 Owner 当前授权；旧 HTTP 的独立入口问题由 #1277 跟踪。
        await new BusinessPartnerService(h.Db, authority).DeleteAsync(row.BpCd, row.RowVersion, null)
            .WaitAsync(TimeSpan.FromSeconds(8));
        await locked.RollbackAsync();
        var identity = await Partners(h).ReadAsync(bp);
        Assert.True(identity.Source.GetProperty("isFrozen").GetBoolean());
        Assert.True(identity.Source.GetProperty("isDeleted").GetBoolean());
        Assert.Equal(9, identity.Source.GetProperty("status").GetInt32());
        await DeviceEvidence("wp11-65-offline.json", new
        {
            simulated = "FIN-MASTER 离线 PostgreSQL 来源锁、用户设备及 WP04-01 冻结入口",
            unavailable.Availability, lockedRead = lockedRead.Availability, originalLinkRead = stored.Reference, retiredBy = "Main BusinessPartnerService.DeleteAsync",
            frozen = identity.Source.GetProperty("isFrozen").GetBoolean(), retiredState = 9
        });
    }

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_finance_reference_migration_witnesses_existing_native_data_without_binding_scope()
    {
        var tenant = new TenantContext { CurrentTenantId = Guid.NewGuid() };
        await using var owner = server.Context(tenant);
        var database = "wp1165_legacy_" + Guid.NewGuid().ToString("N");
        await owner.Database.ExecuteSqlRawAsync("CREATE DATABASE " + database);
        await using var old = server.Context(tenant);
        old.Database.SetConnectionString(new NpgsqlConnectionStringBuilder(old.Database.GetConnectionString()) { Database = database }.ConnectionString);
        await old.GetService<IMigrator>().MigrateAsync("20261119144500_GlAccountVersionWorkflowV1");
        old.Sys_Tenants.Add(new Sys_Tenant { Id = tenant.CurrentTenantId, TenantCode = "wp1165-legacy", TenantName = "模拟旧租户" });
        var bp = new CP6.Entity.DomainModels.Erp.BusinessPartner
        {
            BpCd = "SIM-OLD", BpName = "模拟旧伙伴", BaseCd = "S1", CreditLimit = 78.34m
        };
        old.BusinessPartners.Add(bp);
        await old.SaveChangesAsync();
        var gl = new GlAccount { Id = Guid.NewGuid(), Code = "SIM-OLD-GL", Name = "模拟旧科目", Type = AccountType.Asset, NormalSide = AccountSide.Debit };
        old.GlAccounts.Add(gl);
        await old.SaveChangesAsync();
        var bank = await new ApMasterService(old).CreateBankAccountAsync(new BankAccount
        {
            Code = "SIM-OLD-BANK", Name = "模拟旧本方银行", AccountNo = "6111111122223333", CurrencyCd = "USD", GlAccountId = gl.Id
        }, "模拟旧录入员");
        await old.Database.MigrateAsync();
        Assert.Empty(await old.Database.GetPendingMigrationsAsync());
        var captured = await old.Database.SqlQuery<string>($"""
            SELECT envelope AS "Value" FROM cp6_bp_finance_ref_v1.versions
            WHERE tenant_id={tenant.CurrentTenantId} AND source_id IN ({bp.Id.ToString("D")},{bank.ToString("D")})
            """).ToArrayAsync();
        Assert.Equal(2, captured.Length);
        Assert.Single(captured, body => body.Contains("****3333", StringComparison.Ordinal));
        Assert.Single(captured, body => body.Contains("78.34000000", StringComparison.Ordinal));
        Assert.All(captured, body => Assert.DoesNotContain("6111111122223333", body));
        Assert.Equal(0, await old.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM cp6_bp_finance_ref_v1.scopes").SingleAsync());
        Assert.Equal(0, await old.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM cp6_bp_finance_ref_v1.links").SingleAsync());
    }
}
