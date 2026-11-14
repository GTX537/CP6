using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Fin;
using CP6.Core.Services.Platform;
using CP6.Entity.DomainModels.Fin;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData("manual", false)]
    [InlineData("manual", true)]
    [InlineData("unmatch", true)]
    [InlineData("pending", true)]
    [InlineData("lock", true)]
    [InlineData("unlock", true)]
    [InlineData("create", false)]
    [InlineData("create", true)]
    [InlineData("line-add", true)]
    [InlineData("line-edit", true)]
    [InlineData("line-delete", true)]
    [InlineData("profile-add", true)]
    [InlineData("profile-delete", true)]
    [InlineData("import", false)]
    [InlineData("import", true)]
    public async Task PGT017_bank_statement_writes_share_tenant_authority(string operation, bool pauseFirst)
    {
        await using var h = await Seed();
        await using var admin = await TenantManagementSeed();
        var db = h.Db;
        var fixture = await SeedBankMatch(h);
        foreach (var action in new[] { "mark-pending", "lock", "unlock", "import", "profile-manage", "view" })
            await GrantLegacyAuthorityAction(h, BankReconMenu, action);

        var statement = await db.BankStatements.SingleAsync();
        var line = await db.BankStatementLines.SingleAsync(x => x.Id == fixture.LineIds[0]);
        line.Source = BankLineSource.Manual;
        var journalLine = await db.JournalLines.Where(x => x.AccountId == fixture.BankGl && x.Credit == BankVoucherAmount)
            .Select(x => new { x.Id, x.EntryId }).SingleAsync();
        var newStatement = new BankStatement
        {
            Id = Guid.NewGuid(), No = "fixture requested", BankAccountId = Guid.NewGuid(), FiscalPeriodId = statement.FiscalPeriodId,
        };
        db.BankAccounts.Add(new() { Id = newStatement.BankAccountId, Code = "NEW-BK", Name = "fixture account",
            GlAccountId = fixture.BankGl, IsActive = true });
        var profile = new BankImportProfile { Id = Guid.NewGuid(), Name = "fixture profile", FileFormat = BankFileFormat.Csv,
            SkipHeaderRows = 1, DateField = "0", DateFormat = "yyyy/MM/dd", AmountMode = BankAmountMode.DepositWithdrawalColumns,
            DepositAmountField = "1", WithdrawalAmountField = "2", RefNoField = "3", IsActive = true };
        var newProfile = new BankImportProfile { Id = Guid.NewGuid(), Name = "new fixture profile", DateField = "0", IsActive = true };
        var group = new BankReconMatch { Id = Guid.NewGuid(), StatementId = fixture.Statement, MatchType = BankReconMatchType.Manual,
            StmtSignedSum = -BankVoucherAmount, MatchedBy = "fixture", MatchedAt = DateTime.Now };
        if (operation == "unmatch")
        {
            db.BankReconMatches.Add(group);
            db.BankReconJournalLinks.Add(new() { MatchGroupId = group.Id, JournalLineId = journalLine.Id,
                JournalEntryId = journalLine.EntryId, BankSignedAmount = -BankVoucherAmount });
            line.MatchGroupId = group.Id;
            line.MatchStatus = BankLineMatchStatus.Matched;
        }
        if (operation == "unlock") statement.Status = BankStatementStatus.Locked;
        if (operation == "lock")
        {
            // An empty, balanced statement on a separate GL account.
            var lockAccount = await db.BankAccounts.SingleAsync(x => x.Id == statement.BankAccountId);
            var emptyGl = new GlAccount { Id = Guid.NewGuid(), Code = "1003", Name = "fixture empty bank",
                IsActive = true, IsLeaf = true };
            db.GlAccounts.Add(emptyGl);
            lockAccount.GlAccountId = emptyGl.Id;
            db.BankStatementLines.RemoveRange(await db.BankStatementLines.ToListAsync());
        }
        if (operation is "import" or "profile-delete") db.BankImportProfiles.Add(profile);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var connection = db.Database.GetConnectionString()!;
        await using var host = await AuthenticationApiProcess.Start(connection,
            IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant));
        var actor = await Login(host, h);
        var manager = await Login(host, admin);
        using var read = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(h.Tenant)));
        var snapshot = await NumberHttpData<TenantManagementSnapshotV1>(read);
        var suspend = new TenantManagementCommandV1("bank-statement-pause", snapshot.Revision, "suspend", null);
        var before = await ReadEffects();
        var authorityBefore = await ReadPauseAuthority(h, admin.User);

        await using var barrier = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await barrier.OpenAsync();
        await observer.OpenAsync();
        Task<HttpResponseMessage> pause;
        Task<HttpResponseMessage> write;
        await using (var hold = await barrier.BeginTransactionAsync())
        {
            var table = pauseFirst ? """LOCK TABLE public."Sys_Tenants" IN SHARE MODE"""
                : operation == "manual" ? """LOCK TABLE public."Fin_BankReconMatch" IN SHARE MODE"""
                : operation == "import" ? """LOCK TABLE public."Fin_BankStatementLine" IN SHARE MODE"""
                : """LOCK TABLE public."Fin_BankStatement" IN SHARE MODE""";
            using var lockTable = new NpgsqlCommand(table, barrier, hold);
            await lockTable.ExecuteNonQueryAsync();
            if (pauseFirst)
            {
                pause = Pause();
                var owner = await WaitForFence(observer, barrier.ProcessID, "%UPDATE%Sys_Tenants%", null, pause);
                write = Write();
                var waiting = await WaitForFence(observer, owner.Backend, "%cp6_authentication_v1.acquire()%", null, write);
                output.WriteLine($"Pause {owner.Backend}; {operation} {waiting.Backend}; {waiting.WaitEvent}");
                if (operation == "import")
                {
                    using var preview = await host.Send(ImportRequest(dryRun: true));
                    Assert.True(preview.StatusCode == HttpStatusCode.OK, await OperationsFailure(preview));
                    var body = await preview.Content.ReadFromJsonAsync<JsonElement>();
                    Assert.Equal(1, body.GetProperty("data").GetProperty("successCount").GetInt32());
                }
            }
            else
            {
                write = Write();
                var pattern = operation switch
                {
                    "manual" => "%INSERT%Fin_BankReconMatch%",
                    "import" => "%INSERT%Fin_BankStatementLine%",
                    _ => "%INSERT%Fin_BankStatement%"
                };
                var business = await WaitForFence(observer, barrier.ProcessID, pattern, null, write);
                pause = Pause();
                var waiting = await WaitForFence(observer, business.Backend, "%cp6_authentication_v1.acquire()%", null, pause);
                output.WriteLine($"{operation} {business.Backend}; pause {waiting.Backend}; {waiting.WaitEvent}");
            }
            Assert.Equal(before, await ReadEffects());
            Assert.Equal(authorityBefore, await ReadPauseAuthority(h, admin.User));
            await hold.RollbackAsync();
        }
        using var paused = await pause;
        using var result = await write;
        var receipt = await NumberHttpData<TenantManagementReceiptV1>(paused);
        CheckPause(authorityBefore, await ReadPauseAuthority(h, admin.User), receipt, suspend);
        Assert.True(result.StatusCode == (pauseFirst ? HttpStatusCode.Unauthorized : HttpStatusCode.OK), await OperationsFailure(result));
        var after = await ReadEffects();
        if (pauseFirst)
        {
            Assert.Equal(before, after);
            var reactivate = new TenantManagementCommandV1("bank-statement-reactivate", receipt.Snapshot.Revision, "reactivate", null);
            using var resumed = await host.Send(OperationsRequest(manager, HttpMethod.Post, ManagementPath(h.Tenant, "/commands"), reactivate));
            Assert.True(resumed.StatusCode == HttpStatusCode.OK, await OperationsFailure(resumed));
            actor = await Login(host, h);
            using var allowed = await Write();
            Assert.True(allowed.StatusCode == HttpStatusCode.OK, await OperationsFailure(allowed));
            after = await ReadEffects();
        }
        else
        {
            using var late = await Write();
            Assert.Equal(HttpStatusCode.Unauthorized, late.StatusCode);
            Assert.Equal(after, await ReadEffects());
        }
        Assert.NotEqual(before, after);
        if (operation is "create" or "import")
            Assert.Equal(before.Sequence + 1, after.Sequence);
        if (operation == "import")
        {
            Assert.Equal(before.Lines + 1, after.Lines);
            Assert.Equal(25.75m, (await db.BankStatementLines.AsNoTracking().SingleAsync(x => x.RefNo == "PGT-017")).SignedAmount);
        }

        Task<HttpResponseMessage> Pause() =>
            host.Send(OperationsRequest(manager, HttpMethod.Post, ManagementPath(h.Tenant, "/commands"), suspend));

        Task<HttpResponseMessage> Write()
        {
            var recon = $"/api/fin/bank-recon/{fixture.Statement:D}";
            var statements = $"/api/fin/bank-statement/{fixture.Statement:D}";
            var input = new { line.TxnDate, line.Direction, Amount = 61.23m, Description = "fixture manual line" };
            var request = operation switch
            {
                "manual" => OperationsRequest(actor, HttpMethod.Post, recon + "/manual-match",
                    new ManualMatchRequest { StatementLineIds = [line.Id], JournalLineIds = [journalLine.Id] }),
                "unmatch" => OperationsRequest(actor, HttpMethod.Post, $"/api/fin/bank-recon/unmatch/{group.Id:D}"),
                "pending" => OperationsRequest(actor, HttpMethod.Post, recon + "/mark-pending",
                    new { LineIds = new[] { line.Id }, Category = BankLineCategory.Pending }),
                "lock" or "unlock" => OperationsRequest(actor, HttpMethod.Post, recon + "/" + operation,
                    operation == "unlock" ? new { Reason = "fixture review" } : null),
                "create" => OperationsRequest(actor, HttpMethod.Post, "/api/fin/bank-statement", newStatement),
                "line-add" => OperationsRequest(actor, HttpMethod.Post, statements + "/line", input),
                "line-edit" => OperationsRequest(actor, HttpMethod.Put, $"{statements}/line/{line.Id:D}", input),
                "line-delete" => OperationsRequest(actor, HttpMethod.Delete, $"{statements}/line/{line.Id:D}"),
                "profile-add" => OperationsRequest(actor, HttpMethod.Post, "/api/fin/bank-import-profile/upsert", newProfile),
                "profile-delete" => OperationsRequest(actor, HttpMethod.Delete, $"/api/fin/bank-import-profile/{profile.Id:D}"),
                "import" => ImportRequest(dryRun: false),
                _ => throw new ArgumentOutOfRangeException(nameof(operation))
            };
            return host.Send(request);
        }

        HttpRequestMessage ImportRequest(bool dryRun)
        {
            var route = $"/api/fin/bank-statement/{fixture.Statement:D}/import?profileId={profile.Id:D}&dryRun={dryRun}";
            var request = OperationsRequest(actor, HttpMethod.Post, route);
            var form = new MultipartFormDataContent();
            form.Add(new StringContent("date,deposit,withdrawal,ref\n2026/11/05,25.75,,PGT-017\n"), "file", "bank-fixture.csv");
            request.Content = form;
            return request;
        }

        async Task<StatementWrites> ReadEffects() =>
            new(await db.BankStatements.CountAsync(), await db.BankStatements.CountAsync(x => x.Status == BankStatementStatus.Locked),
                await db.BankStatementLines.CountAsync(), await db.BankStatementLines.SumAsync(x => x.Amount),
                await db.BankStatementLines.CountAsync(x => x.MatchStatus == BankLineMatchStatus.MarkedPending),
                await db.BankReconMatches.CountAsync(), await db.BankReconJournalLinks.CountAsync(),
                await db.BankImportProfiles.CountAsync(), await db.FinSequences.SumAsync(x => x.CurrentValue));
    }

    private sealed record StatementWrites(int Statements, int Locked, int Lines, decimal Amount, int Pending, int Groups,
        int Links, int Profiles, int Sequence);

    [CurrentPermissionPostgresFact]
    public async Task PGT017_bank_statement_writes_failed_commit_keeps_number_available()
    {
        await using var h = await Seed();
        var fixture = await SeedBankVoucher(h);
        await GrantLegacyAuthorityAction(h, BankReconMenu, "view");
        var db = h.Db;
        var period = await db.FiscalPeriods.SingleAsync();
        var account = new BankAccount { Code = "BKR-COMMIT", Name = "fixture commit account",
            GlAccountId = fixture.FeeGl, IsActive = true };
        db.BankAccounts.Add(account);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var newId = Guid.NewGuid();
        var request = new { Id = newId, No = "fixture requested", BankAccountId = account.Id, FiscalPeriodId = period.Id };
        await using var host = await AuthenticationApiProcess.Start(db.Database.GetConnectionString()!,
            IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant));
        var actor = await Login(host, h);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION public.cp6_test_bkr_statement_commit_reject() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'bank statement fixture commit rejected' USING ERRCODE='23514'; END $$;
            CREATE CONSTRAINT TRIGGER cp6_test_bkr_statement_commit_reject
            AFTER INSERT ON public."Fin_BankStatement" DEFERRABLE INITIALLY DEFERRED
            FOR EACH ROW EXECUTE FUNCTION public.cp6_test_bkr_statement_commit_reject();
            """);
        try
        {
            using var rejected = await host.Send(OperationsRequest(actor, HttpMethod.Post, "/api/fin/bank-statement", request));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, rejected.StatusCode);
            Assert.False(await db.BankStatements.AnyAsync(x => x.Id == newId));
            Assert.False(await db.FinSequences.AnyAsync(x => x.SeqKey == "BKR"));
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER cp6_test_bkr_statement_commit_reject ON public."Fin_BankStatement";
                DROP FUNCTION public.cp6_test_bkr_statement_commit_reject();
                """);
        }
        using var accepted = await host.Send(OperationsRequest(actor, HttpMethod.Post, "/api/fin/bank-statement", request));
        Assert.True(accepted.StatusCode == HttpStatusCode.OK, await OperationsFailure(accepted));
        var saved = await db.BankStatements.AsNoTracking().SingleAsync(x => x.Id == newId);
        Assert.Equal((account.Id, "BKR-2026-11-00001"), (saved.BankAccountId, saved.No));
    }
}
