using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Sys;
using CP6.Tests.Platform;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using static CP6.Core.Services.Plm.EngineeringIntakeWireV1;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private const string SimulatedPlmAuthority = "SIMULATED_PLM", IntakeLegalEntity = "SIMULATED_ENTITY";

    private sealed class SimulatedEngineeringSource(Guid tenant, Guid actor, Guid opportunity, Guid account,
        EngineeringIntakeCallerV1 client, Guid generation, ECDsa signingKey) : IEngineeringIntakeAuthorityV1
    {
        internal readonly EngineeringIntakeOwnerSignerV1 OwnerSigner = new("simulated-main-signing-v1", signingKey);
        internal readonly EngineeringIntakeProofKeyV1 SourceKey = new("simulated-crm-source-v1", RandomNumberGenerator.GetBytes(32));
        internal DateTimeOffset? UseUntil { get; set; }
        internal bool AllowOtherCurrentTokens { get; set; }
        internal Guid? CannotReadOriginalToken { get; set; }
        internal Guid? CannotReadOriginalRequest { get; set; }

        public async Task<EngineeringIntakeRouteV1?> HoldRouteAsync(CP6Context db, EngineeringIntakeCallerV1 caller, EngineeringIntakeScopeV1 scope,
            EngineeringIntakeUseV1 use, CancellationToken ct)
        {
            Assert.NotNull(db.Database.CurrentTransaction);
            if (scope.TenantId != tenant || scope.Authority != SimulatedPlmAuthority || scope.LegalEntityKey != IntakeLegalEntity
                || scope.OpportunityId is { } id && id != opportunity || caller.TenantId != tenant || caller.ClientId != client.ClientId
                || caller.TokenId != client.TokenId && !AllowOtherCurrentTokens)
                throw Failure("SIMULATED_SCOPE_FORBIDDEN", 403);
            _ = await HoldSourceControlAsync(db, ct);
            var current = await db.Database.SqlQuery<Guid>($"""
                SELECT generation AS "Value" FROM cp6_engineering_intake_v1.coverage WHERE tenant_id={tenant}
                    AND authority={SimulatedPlmAuthority} AND legal_entity_key={IntakeLegalEntity}
                """).SingleOrDefaultAsync(ct);
            return new(scope, actor, null, "simulated-route-v1", current == Guid.Empty ? generation : current, "simulated-writer-inventory",
                "simulated-crm-e1", new(SimulatedPlmAuthority, IntakeContract, "simulated-intake-e1"),
                new(SimulatedPlmAuthority, ProgressContract, "simulated-progress-e1"), "simulated-main-result", OwnerSigner, client.ExpiresAt);
        }

        public async Task<EngineeringIntakeSourceUseV1?> RequireSourceUseAsync(CP6Context db, EngineeringIntakeRouteV1 route,
            EngineeringIntakeRequestV1 request, string originalBodyHash, CancellationToken ct)
        {
            if (request.Source.AccountId != account || request.Source.SourceId != opportunity
                || request.Source.EvidenceRefs.Length != 0 || request.Source.ActivitySources.Length != 0)
                return null;
            var control = await HoldSourceControlAsync(db, ct);
            if (!control.AllowUse) throw Failure("SIMULATED_SOURCE_USE_REVOKED", 403);
            var accepted = await db.Database.SqlQuery<bool>($"""
                SELECT EXISTS(SELECT FROM wp0560_simulated.source_publications
                    WHERE tenant_id={tenant} AND request_key={request.RequestKey} AND body_hash={originalBodyHash}) AS "Value"
                """).SingleAsync(ct);
            if (!accepted) return null;
            return new(SourceKey, UseUntil ?? client.ExpiresAt,
                control.NeedsInfo ? ["SIMULATED_REQUIRED_INPUT_PENDING"] : []);
        }

        public async Task<DateTimeOffset?> HoldOriginalReadAsync(CP6Context db, Guid requester, EngineeringIntakeCallerV1? service, EngineeringIntakeSourceLocationV1 original,
            CancellationToken ct)
        {
            if (requester != actor || original.NaturalKey.TenantId != tenant || original.NaturalKey.OpportunityId != opportunity)
                throw Failure("SIMULATED_SCOPE_FORBIDDEN", 403);
            if (service is not null && service.TokenId == CannotReadOriginalToken || original.RequestKey == CannotReadOriginalRequest)
                throw Failure("SIMULATED_ORIGINAL_READ_REVOKED", 403);
            var control = await HoldSourceControlAsync(db, ct);
            if (!control.AllowRead) throw Failure("SIMULATED_ORIGINAL_READ_REVOKED", 403);
            return client.ExpiresAt;
        }

        private sealed record SourceControl(bool AllowUse, bool AllowRead, bool NeedsInfo);
        private async Task<SourceControl> HoldSourceControlAsync(CP6Context db, CancellationToken ct)
        {
            Assert.NotNull(db.Database.CurrentTransaction);
            return (await db.Database.SqlQuery<SourceControl>($"""
                SELECT allow_use AS "AllowUse", allow_read AS "AllowRead", needs_info AS "NeedsInfo"
                FROM wp0560_simulated.source_controls WHERE tenant_id={tenant} FOR SHARE
                """).ToArrayAsync(ct)).Single();
        }
    }

    private sealed class EngineeringIntakeHarness : IDisposable
    {
        internal readonly ECDsa SigningKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        internal readonly IDataProtectionProvider Encryption = new EphemeralDataProtectionProvider();
        internal readonly EngineeringIntakeCallerV1 Caller;
        internal readonly EngineeringCaseNaturalKeyV1 Natural;
        internal readonly SimulatedEngineeringSource Source;
        internal readonly Guid InventoryGeneration = Guid.NewGuid();
        internal readonly Guid Account = Guid.NewGuid();
        internal readonly Harness Tenant;
        internal readonly AuthorizationCatalogStoreV1 Catalog = new();

        internal EngineeringIntakeHarness(Harness h)
        {
            Tenant = h;
            var until = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds());
            Caller = new(h.Tenant, "https://simulated-wp0560.example", "simulated-ho-service", Guid.NewGuid(), until);
            Natural = new(h.Tenant, SourceSystem, Guid.NewGuid(), SimulatedPlmAuthority, IntakeLegalEntity, Purpose);
            Source = new(h.Tenant, h.User, Natural.OpportunityId, Account, Caller, InventoryGeneration, SigningKey);
            Catalog.RegisterScope("plm-engineering-requirement", "工程需求", [1, 2, 3, 4, 5], 5);
            Catalog.RegisterFields("plm-engineering-requirement", [new("Definition", "定义"), new("Source", "来源")]);
            Catalog.Seal();
        }

        internal OperationsOwnerV1 Operations(CP6Context db) =>
            new(db, new HttpContextAccessor { HttpContext = Tenant.Http }, _ => Task.FromResult(Tenant.User), Catalog.Capture);
        internal EngineeringRequirementService Native(CP6Context? db = null)
        {
            db ??= Tenant.Db;
            return new(db, Operations(db), intakeOriginals: new(db, Encryption, Source));
        }

        internal EngineeringIntakeContextService Provider(CP6Context? db = null, bool connected = true, IDataProtectionProvider? encryption = null)
        {
            db ??= Tenant.Db;
            var use = connected ? Source : null;
            var originals = new EngineeringIntakeOriginalsV1(db, encryption ?? Encryption, use);
            var operations = Operations(db);
            return new(db, operations, new(db, operations, intakeOriginals: originals), originals, encryption ?? Encryption, use);
        }

        internal EngineeringNextQueryV1 EmptyQuery() => new(Natural, null, "0", null, null, null);
        internal Task<EngineeringNextContextV1> PrepareAsync(EngineeringNextQueryV1? query = null) =>
            Provider().QueryNextAsync(Caller, Encode(query ?? EmptyQuery()), default);
        internal Task<EngineeringIntakeOriginalPageV1> OriginalAsync(EngineeringIntakeRequestV1 request, EngineeringIntakeContextService? owner = null) =>
            (owner ?? Provider()).QueryOriginalAsync(Caller, Encode(OriginalQuery(request)), default);
        internal static EngineeringIntakeOriginalQueryV1 OriginalQuery(EngineeringIntakeRequestV1 request) =>
            new("HO-ORIGINAL-QUERY/1", request.TenantId, request.CaseNaturalKey.TargetAuthority, request.CaseNaturalKey.LegalEntityKey,
                new("ENGINEERING", request.CaseId, request.RequestKey, request.SubmissionVersion, request.SourceBinding.SourceDigest), null, 100);

        internal EngineeringIntakeRequestV1 Build(EngineeringNextContextV1 next, string sourceVersion = "1")
        {
            var query = next.Query;
            var requirement = JsonSerializer.SerializeToElement(new
            {
                summary = "模拟原需求 " + sourceVersion,
                lines = new[] { new { lineId = Guid.NewGuid(), clientRef = (string?)null, description = "原始定量条款",
                    customerItemCode = (string?)null, productReference = (object?)null,
                    quantity = "12.340061", unitCode = "EA", needByDate = (string?)null } },
                decisionPath = (string?)null, noDecisionDateReason = (string?)null
            }, WireJson);
            var source = new EngineeringIntakeSourceV1("HO-ENG-SOURCE/1", Tenant.Tenant, SourceKind, Natural.OpportunityId,
                sourceVersion, Account, requirement, null, ["已知载荷"], ["工艺待澄清"], [], []);
            var binding = new EngineeringIntakeBindingV1(SourceKind, Natural.OpportunityId, sourceVersion, SourceDigest(Source.SourceKey, source), Source.SourceKey.Version);
            var previous = query.CaseId is null ? null : new EngineeringIntakePreviousV1(query.CurrentSubmissionVersion,
                query.CurrentRequestKey!.Value, query.CurrentReceiptId!, query.CurrentSource!, next.MainCaseRevision);
            return new(RequestContract, Tenant.Tenant, Guid.NewGuid(), SourceSystem, "simulated-crm-e1", Natural,
                query.CaseId ?? Guid.NewGuid(), Version(previous is null ? 1 : long.Parse(previous.SubmissionVersion) + 1),
                Guid.NewGuid(), previous, next.RequiredMode!, next.Target, source, binding, [],
                new("SIMULATED_CRM", "published-source-use", "1", "simulated-proof-ref"), next, "本轮 SIMULATED 工程交接",
                EngineeringIntakeStoreV1.Milliseconds(DateTimeOffset.UtcNow));
        }

        internal async Task<byte[]> PublishAsync(EngineeringIntakeRequestV1 request, byte[]? bytes = null)
        {
            bytes ??= Encode(request);
            await Tenant.Db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO wp0560_simulated.source_publications(tenant_id,request_key,body_hash)
                VALUES({Tenant.Tenant},{request.RequestKey},{Hash(bytes)})
                """);
            return bytes;
        }

        internal Task ControlAsync(bool allowUse = true, bool allowRead = true, bool needsInfo = false, bool localConflict = false) =>
            Tenant.Db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE wp0560_simulated.source_controls SET allow_use={allowUse},allow_read={allowRead},needs_info={needsInfo},
                    local_conflict={localConflict} WHERE tenant_id={Tenant.Tenant}
                """);

        internal async Task<(long Cases, long Requests, long Results, long Outbox, long Drafts, long Sources)> CountsAsync()
        {
            var counts = await Tenant.Db.Database.SqlQuery<long>($"""
                SELECT n AS "Value" FROM (VALUES
                    (1,(SELECT count(*) FROM cp6_engineering_intake_v1.cases WHERE tenant_id={Tenant.Tenant})),
                    (2,(SELECT count(*) FROM cp6_engineering_intake_v1.requests WHERE tenant_id={Tenant.Tenant})),
                    (3,(SELECT count(*) FROM cp6_engineering_intake_v1.intake_results WHERE tenant_id={Tenant.Tenant})),
                    (4,(SELECT count(*) FROM cp6_engineering_intake_v1.outbox WHERE tenant_id={Tenant.Tenant})),
                    (5,(SELECT count(*) FROM cp6_engineering_requirement_v1.heads WHERE tenant_id={Tenant.Tenant} AND kind='REQUIREMENT')),
                    (6,(SELECT count(*) FROM cp6_engineering_requirement_v1.heads WHERE tenant_id={Tenant.Tenant} AND kind='SOURCE'))
                ) AS c(pos,n) ORDER BY pos
                """).ToArrayAsync();
            return (counts[0], counts[1], counts[2], counts[3], counts[4], counts[5]);
        }

        public void Dispose() => SigningKey.Dispose();
    }

    private static async Task<EngineeringIntakeHarness> PrepareIntakeHarnessAsync(Harness h, bool coverage = true, bool grant = true)
    {
        if (grant) await GrantRequirement(h);
        var f = new EngineeringIntakeHarness(h);
        await h.Db.Database.ExecuteSqlRawAsync("""
            CREATE SCHEMA IF NOT EXISTS wp0560_simulated;
            CREATE TABLE IF NOT EXISTS wp0560_simulated.source_controls
                (tenant_id uuid PRIMARY KEY, allow_use boolean NOT NULL, allow_read boolean NOT NULL,
                    needs_info boolean NOT NULL, local_conflict boolean NOT NULL);
            CREATE TABLE IF NOT EXISTS wp0560_simulated.source_publications
                (tenant_id uuid NOT NULL, request_key uuid NOT NULL, body_hash text NOT NULL, PRIMARY KEY(tenant_id,request_key));
            CREATE TABLE IF NOT EXISTS wp0560_simulated.consumer_submissions
                (tenant_id uuid NOT NULL, case_id uuid NOT NULL, submission_version text NOT NULL,
                    request_key uuid NOT NULL, PRIMARY KEY(tenant_id,case_id,submission_version));
            """);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO wp0560_simulated.source_controls VALUES ({h.Tenant},true,true,false,false)
            """);
        if (coverage)
            await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_engineering_intake_v1.coverage VALUES
                    ({h.Tenant},{SimulatedPlmAuthority},{IntakeLegalEntity},{f.InventoryGeneration},'simulated-writer-inventory',true)
                """);
        h.Db.CrmServiceTokenRecords.Add(new()
        {
            TenantId = h.Tenant, Issuer = f.Caller.Issuer, ClientId = f.Caller.ClientId,
            Jti = f.Caller.TokenId.ToString("D"), ExpiresAtUtc = f.Caller.ExpiresAt
        });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        return f;
    }

    private static byte[] Encode<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, WireJson);
    private static EngineeringIntakeResultV1 IntakeResult(EngineeringIntakeOriginalPageV1 page)
    {
        Assert.Equal("FOUND", page.Observation);
        Assert.False(page.NoEffectProven);
        var item = Assert.Single(page.Results);
        var original = Convert.FromBase64String(item.OriginalBodyBase64);
        Assert.Equal(Hash(original), item.BodyHash);
        Assert.Equal(IntakeContract, item.ReceivedTrust.ContractVersion);
        return Read<EngineeringIntakeResultV1>(original);
    }
    private static EngineeringNextQueryV1 AfterIntake(EngineeringIntakeRequestV1 request, EngineeringIntakeResultV1 result) =>
        new(request.CaseNaturalKey, request.CaseId, request.SubmissionVersion, request.RequestKey, result.ReceiptId, request.SourceBinding);
    private static async Task ExpectIntakeFailure(string code, Func<Task> run) =>
        Assert.Equal(code, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(run)).Message);

    [CurrentPermissionPostgresFact]
    public async Task Wp0560_absence_is_signed_only_with_registered_writer_coverage_and_M04_proves_no_absence()
    {
        await using var unregistered = await Seed();
        using var noInventory = await PrepareIntakeHarnessAsync(unregistered, coverage: false);
        await ExpectIntakeFailure("AUTHORITY_FENCE_UNAVAILABLE", () => noInventory.PrepareAsync());
        await ExpectIntakeFailure("AUTHORITY_FENCE_UNAVAILABLE", () => noInventory.Provider(connected: false)
            .QueryNextAsync(noInventory.Caller, Encode(noInventory.EmptyQuery()), default));
        var unknown = await noInventory.Provider().QueryOriginalAsync(noInventory.Caller, Encode(new EngineeringIntakeOriginalQueryV1(
            "HO-ORIGINAL-QUERY/1", unregistered.Tenant, SimulatedPlmAuthority, IntakeLegalEntity,
            new("ENGINEERING", Guid.NewGuid(), Guid.NewGuid(), "1", new string('0', 64)), null, 100)), default);
        Assert.Equal("UNPROVEN", unknown.Observation);
        Assert.False(unknown.NoEffectProven);
        Assert.Equal((0L, 0L, 0L, 0L, 0L, 0L), await noInventory.CountsAsync());

        await using var h = await Seed();
        using var f = await PrepareIntakeHarnessAsync(h);
        var next = await f.PrepareAsync();
        Assert.True(next.NextAllowed);
        Assert.Equal("0", next.MainCaseRevision);
        Assert.Equal("0", next.Query.CurrentSubmissionVersion);
        Assert.Null(next.Query.CaseId);
        Assert.Equal("CREATE", next.RequiredMode);
        Assert.Null(next.Target);
        Assert.Equal("ABSENT", next.PriorExecutionFence.Key);
        using var verifier = ECDsa.Create(f.SigningKey.ExportParameters(false));
        var unsigned = next with
        {
            PriorExecutionFence = next.PriorExecutionFence with { ProofRef = "" },
            Decision = next.Decision with { ProofRef = "" }
        };
        Assert.True(verifier.VerifyData(EngineeringIntakeOwnerSignerV1.Data("HO-ENG-MAIN-FENCE-1", unsigned),
            WebEncoders.Base64UrlDecode(next.PriorExecutionFence.ProofRef), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        var notObserved = await f.OriginalAsync(f.Build(next));
        Assert.Equal("NOT_OBSERVED", notObserved.Observation);
        Assert.Empty(notObserved.Results);
        Assert.False(notObserved.NoEffectProven);
        var priorToReceive = f.Build(next);
        var pending = await f.PrepareAsync(new(f.Natural, priorToReceive.CaseId, "1", priorToReceive.RequestKey, null, priorToReceive.SourceBinding));
        Assert.False(pending.NextAllowed);
        Assert.Equal("UNPROVEN", pending.PriorExecutionFence.Key);
        Assert.Null(pending.RequiredMode);
        Assert.Equal((0L, 0L, 0L, 0L, 0L, 0L), await f.CountsAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Wp0560_lost_commit_recovers_same_key_and_intake_bytes_one_engineering_identity_only()
    {
        var lost = new CommitFault();
        await using var h = await Seed(lost);
        using var f = await PrepareIntakeHarnessAsync(h);
        var request = f.Build(await f.PrepareAsync());
        var raw = await f.PublishAsync(request, [.. Encoding.UTF8.GetBytes(" \n"), .. Encode(request), .. Encoding.UTF8.GetBytes("\n ")]);
        lost.Armed = true;
        await ExpectIntakeFailure("ENGINEERING_INTAKE_OUTCOME_UNKNOWN", () => f.Provider().ReceiveAsync(f.Caller, raw, default));

        await using var reader = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        var owner = f.Provider(reader);
        var original = await f.OriginalAsync(request, owner);
        var result = IntakeResult(original);
        Assert.Equal("ACCEPTED", result.Outcome);
        Assert.NotNull(result.Engineering);
        Assert.Equal(request.SourceBinding, result.Source.Source);
        Assert.True(f.Source.OwnerSigner.Verify("HO-ENG-INTAKE-EVIDENCE-1",
            result with { OwnerEvidence = result.OwnerEvidence with { ProofRef = "" } }, result.OwnerEvidence.ProofRef));
        var item = Assert.Single(original.Results);
        Assert.True(f.Source.OwnerSigner.Verify("HO-ENG-ORIGINAL-TRUST-1", new { requestKey = request.RequestKey,
            bodyHash = item.BodyHash, trust = item.ReceivedTrust with { VerificationRef = "" } }, item.ReceivedTrust.VerificationRef));
        var replay = await owner.ReceiveAsync(f.Caller, raw, default);
        Assert.Equal("ALREADY_OBSERVED", replay.State);
        Assert.Equal("RECEIPT_ONLY", replay.Scope);
        Assert.Equal("UNKNOWN", replay.BusinessOutcome);
        Assert.Equal(request.RequestKey, replay.RequestKey);
        Assert.Equal(replay, await owner.ReceiveAsync(f.Caller, raw, default));
        await ExpectIntakeFailure("REQUEST_KEY_CONFLICT", () => owner.ReceiveAsync(f.Caller, Encode(request), default));
        Assert.Equal(item, Assert.Single((await f.OriginalAsync(request, owner)).Results));
        Assert.Equal((1L, 1L, 1L, 1L, 1L, 1L), await f.CountsAsync());
        var draft = await f.Native(reader).ReadAsync(Guid.Parse(result.Engineering!.EngineeringRequestId));
        Assert.Equal("DRAFT_ONLY", draft.State);
        Assert.Contains("工艺待澄清", draft.Body.Requirement!.ClarificationQuestions);
        Assert.Equal(SourceBytes(raw), (await f.Native(reader).DownloadOriginalAsync(draft.Reference.Id, draft.Reference.Version, null)).Bytes);
        Assert.Empty(await reader.WorkOrders.AsNoTracking().ToArrayAsync());
        Assert.Equal(2, await reader.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM cp6_main_owner_v1.operations WHERE tenant_id={h.Tenant}
                AND owner='Main.Plm.EngineeringRequirement.v1'
            """).SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Wp0560_changes_to_writer_generation_or_Main_target_invalidate_Prepared_context_and_keep_history()
    {
        await using var h = await Seed();
        using var f = await PrepareIntakeHarnessAsync(h);
        var expiredInventory = f.Build(await f.PrepareAsync());
        var expiredBytes = await f.PublishAsync(expiredInventory);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_engineering_intake_v1.coverage SET evidence_key=evidence_key WHERE tenant_id={h.Tenant}
            """);
        await ExpectIntakeFailure("STALE_NEXT_CONTEXT", () => f.Provider().ReceiveAsync(f.Caller, expiredBytes, default));
        Assert.Equal((0L, 0L, 0L, 0L, 0L, 0L), await f.CountsAsync());

        var first = f.Build(await f.PrepareAsync());
        Assert.Equal("UNKNOWN", (await f.Provider().ReceiveAsync(f.Caller, await f.PublishAsync(first), default)).BusinessOutcome);
        var original = await f.OriginalAsync(first);
        var accepted = IntakeResult(original);
        var oldContext = await f.PrepareAsync(AfterIntake(first, accepted));
        Assert.Equal("REVISE", oldContext.RequiredMode);
        Assert.Equal(accepted.Engineering, oldContext.Target);
        var stale = f.Build(oldContext, "2");
        var staleBytes = await f.PublishAsync(stale);
        var native = f.Native();
        var firstSnapshot = await native.ReadAsync(Guid.Parse(accepted.Engineering!.EngineeringRequestId));
        var clarification = new RequirementClarificationV1(Guid.NewGuid(), firstSnapshot.Body.Requirement!.Source, "summary", "Main 原渠道问题");
        await native.CommandAsync(ReviseRequirement(firstSnapshot, firstSnapshot.Body with
        {
            Name = "Main 当前本地修订",
            Requirement = firstSnapshot.Body.Requirement with { Clarifications = [clarification] }
        }));
        await ExpectIntakeFailure("STALE_NEXT_CONTEXT", () => f.Provider().ReceiveAsync(f.Caller, staleBytes, default));
        Assert.Equal((1L, 1L, 1L, 1L, 1L, 1L), await f.CountsAsync());

        var refreshed = await f.PrepareAsync(AfterIntake(first, accepted));
        Assert.Equal("2", refreshed.Target!.EngineeringRevision);
        var successor = f.Build(refreshed, "2");
        await f.Provider().ReceiveAsync(f.Caller, await f.PublishAsync(successor), default);
        var second = IntakeResult(await f.OriginalAsync(successor));
        Assert.Equal("3", second.Engineering!.EngineeringRevision);
        Assert.Equal(accepted.Engineering.EngineeringRequestId, second.Engineering.EngineeringRequestId);
        Assert.Equal(first.CaseId, successor.CaseId);
        Assert.Equal(first.RequestKey, successor.Previous!.RequestKey);
        Assert.Equal(original.Results, (await f.OriginalAsync(first)).Results);
        Assert.Equal(firstSnapshot.OriginalJson, (await native.ReadAsync(firstSnapshot.Reference.Id, "1")).OriginalJson);
        Assert.Equal(SourceBytes(Encode(first)), (await native.DownloadOriginalAsync(firstSnapshot.Reference.Id, "1", null)).Bytes);
        var current = await native.ReadAsync(firstSnapshot.Reference.Id);
        Assert.Equal(clarification, Assert.Single(current.Body.Requirement!.Clarifications!));
        Assert.NotEqual(clarification.Source, current.Body.Requirement.Source);
        Assert.Equal(SourceBytes(Encode(successor)), (await native.DownloadOriginalAsync(current.Reference.Id, "3", null)).Bytes);
        Assert.Equal((1L, 2L, 2L, 2L, 1L, 2L), await f.CountsAsync());
        Assert.False((await f.PrepareAsync(AfterIntake(first, accepted))).NextAllowed);
    }

    [CurrentPermissionPostgresFact]
    public async Task Wp0560_needs_info_preserves_closed_selection_and_allows_exact_same_Case_CREATE_successor()
    {
        await using var h = await Seed();
        using var f = await PrepareIntakeHarnessAsync(h);
        await f.ControlAsync(needsInfo: true);
        var first = f.Build(await f.PrepareAsync());
        await f.Provider().ReceiveAsync(f.Caller, await f.PublishAsync(first), default);
        var before = await f.OriginalAsync(first);
        var needsInfo = IntakeResult(before);
        Assert.Equal("NEEDS_INFO", needsInfo.Outcome);
        Assert.Null(needsInfo.Engineering);
        Assert.Equal((1L, 1L, 1L, 1L, 0L, 0L), await f.CountsAsync());
        var next = await f.PrepareAsync(AfterIntake(first, needsInfo));
        Assert.Equal("CLOSED:" + first.RequestKey.ToString("D"), next.PriorExecutionFence.Key);
        Assert.True(next.NextAllowed);
        Assert.Equal("CREATE", next.RequiredMode);
        var nextRequest = f.Build(next, "2");
        await f.ControlAsync();
        await f.Provider().ReceiveAsync(f.Caller, await f.PublishAsync(nextRequest), default);
        Assert.Equal(first.CaseId, nextRequest.CaseId);
        Assert.Equal("2", nextRequest.SubmissionVersion);
        Assert.Equal("ACCEPTED", IntakeResult(await f.OriginalAsync(nextRequest)).Outcome);
        Assert.Equal(before.Results, (await f.OriginalAsync(first)).Results);
        Assert.Equal((1L, 2L, 2L, 2L, 1L, 1L), await f.CountsAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Wp0560_two_connections_share_the_final_natural_slot_and_original_key_fences()
    {
        await using var h = await Seed();
        using var f = await PrepareIntakeHarnessAsync(h);
        var next = await f.PrepareAsync();
        var first = f.Build(next);
        var second = f.Build(next);
        var firstBytes = await f.PublishAsync(first);
        var secondBytes = await f.PublishAsync(second);
        await using var a = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        await using var b = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        var aProvider = f.Provider(a);
        var bProvider = f.Provider(b);
        var outcomes = await Task.WhenAll(Attempt(aProvider, firstBytes), Attempt(bProvider, secondBytes));
        Assert.Equal(new[] { "DURABLY_RECEIVED", "STALE_NEXT_CONTEXT" }, outcomes.OrderBy(x => x, StringComparer.Ordinal));
        var won = outcomes[0] == "DURABLY_RECEIVED" ? firstBytes : secondBytes;
        Assert.All(await Task.WhenAll(Attempt(aProvider, won), Attempt(bProvider, won)), state => Assert.Equal("ALREADY_OBSERVED", state));
        Assert.Equal((1L, 1L, 1L, 1L, 1L, 1L), await f.CountsAsync());

        async Task<string> Attempt(EngineeringIntakeContextService provider, byte[] input)
        {
            try { return (await provider.ReceiveAsync(f.Caller, input, default)).State; }
            catch (OrderPriceCorrectionException error) { return error.Message; }
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task Wp0560_needs_info_with_existing_engineering_requires_read_of_the_adopted_old_source()
    {
        await using var h = await Seed();
        using var f = await PrepareIntakeHarnessAsync(h);
        var initial = f.Build(await f.PrepareAsync());
        await f.Provider().ReceiveAsync(f.Caller, await f.PublishAsync(initial), default);
        var accepted = IntakeResult(await f.OriginalAsync(initial));
        await f.ControlAsync(needsInfo: true);
        var successor = f.Build(await f.PrepareAsync(AfterIntake(initial, accepted)), "2");
        var bytes = await f.PublishAsync(successor);
        await f.Provider().ReceiveAsync(f.Caller, bytes, default);
        var needsInfo = IntakeResult(await f.OriginalAsync(successor));
        Assert.Equal("NEEDS_INFO", needsInfo.Outcome);
        Assert.Equal(accepted.Engineering, needsInfo.Engineering);

        f.Source.CannotReadOriginalRequest = initial.RequestKey;
        await ExpectIntakeFailure("SIMULATED_ORIGINAL_READ_REVOKED", () => f.PrepareAsync(AfterIntake(successor, needsInfo)));
        await ExpectIntakeFailure("SIMULATED_ORIGINAL_READ_REVOKED", () => f.OriginalAsync(successor));
        await ExpectIntakeFailure("SIMULATED_ORIGINAL_READ_REVOKED", () => f.Provider().ReceiveAsync(f.Caller, bytes, default));
        Assert.Equal((1L, 2L, 2L, 2L, 1L, 1L), await f.CountsAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Wp0560_latest_source_grant_Main_actions_and_own_tenant_apply_to_write_and_historical_disclosure()
    {
        await using var h = await Seed();
        using var f = await PrepareIntakeHarnessAsync(h);
        var original = f.Build(await f.PrepareAsync());
        var bytes = await f.PublishAsync(original);
        await f.ControlAsync(allowUse: false);
        await ExpectIntakeFailure("SIMULATED_SOURCE_USE_REVOKED", () => f.Provider().ReceiveAsync(f.Caller, bytes, default));

        await f.ControlAsync();
        var menu = await h.Db.Sys_Menus.SingleAsync(x => x.MenuKey == "plm-engineering-requirement");
        var roles = h.Db.Sys_RoleActions.Where(x => x.RoleId == 72 && x.MenuId == menu.MenuId && x.ActionCode == "add");
        await roles.ExecuteDeleteAsync();
        await ExpectIntakeFailure("ACTION_FORBIDDEN", () => f.Provider().ReceiveAsync(f.Caller, bytes, default));
        Assert.Equal((0L, 0L, 0L, 0L, 0L, 0L), await f.CountsAsync());
        h.Db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = menu.MenuId, ActionCode = "add" });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        await ExpectIntakeFailure("ENGINEERING_INTAKE_TENANT_FORBIDDEN", () => f.Provider().ReceiveAsync(f.Caller with
            { TenantId = Guid.NewGuid() }, bytes, default));
        await f.Provider().ReceiveAsync(f.Caller, bytes, default);
        await using (var foreign = await Seed())
        using (var other = await PrepareIntakeHarnessAsync(foreign))
        {
            var otherTenantQuery = EngineeringIntakeHarness.OriginalQuery(original) with { TenantId = foreign.Tenant };
            var isolated = await other.Provider().QueryOriginalAsync(other.Caller, Encode(otherTenantQuery), default);
            Assert.Equal("NOT_OBSERVED", isolated.Observation);
            Assert.Empty(isolated.Results);
            Assert.False(isolated.NoEffectProven);
        }

        await f.ControlAsync(allowUse: false);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_engineering_intake_v1.coverage SET enabled=false WHERE tenant_id={h.Tenant}
            """);
        await ExpectIntakeFailure("AUTHORITY_FENCE_UNAVAILABLE", () => f.PrepareAsync());
        Assert.Equal("ALREADY_OBSERVED", (await f.Provider().ReceiveAsync(f.Caller, bytes, default)).State);
        Assert.Equal("FOUND", (await f.OriginalAsync(original)).Observation);
        await f.ControlAsync(allowUse: false, allowRead: false);
        await ExpectIntakeFailure("SIMULATED_ORIGINAL_READ_REVOKED", () => f.OriginalAsync(original));
        await ExpectIntakeFailure("SIMULATED_ORIGINAL_READ_REVOKED", () => f.Provider().ReceiveAsync(f.Caller, bytes, default));
        await f.ControlAsync();
        var otherCaller = f.Caller with { TokenId = Guid.NewGuid() };
        f.Source.AllowOtherCurrentTokens = true;
        f.Source.CannotReadOriginalToken = otherCaller.TokenId;
        h.Db.CrmServiceTokenRecords.Add(new()
        {
            TenantId = h.Tenant, Issuer = otherCaller.Issuer, ClientId = otherCaller.ClientId,
            Jti = otherCaller.TokenId.ToString("D"), ExpiresAtUtc = otherCaller.ExpiresAt
        });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        await ExpectIntakeFailure("SIMULATED_ORIGINAL_READ_REVOKED",
            () => f.Provider().QueryOriginalAsync(otherCaller, Encode(EngineeringIntakeHarness.OriginalQuery(original)), default));
        await ExpectIntakeFailure("SIMULATED_ORIGINAL_READ_REVOKED",
            () => f.Provider().ReceiveAsync(otherCaller, Encode(original with { RequestReason = "修改回放正文" }), default));
        await h.Db.CrmServiceTokenRecords.Where(x => x.Jti == f.Caller.TokenId.ToString("D"))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAtUtc, DateTimeOffset.UtcNow));
        await ExpectIntakeFailure("ENGINEERING_INTAKE_CREDENTIAL_FORBIDDEN", () => f.OriginalAsync(original));
        Assert.Equal((1L, 1L, 1L, 1L, 1L, 1L), await f.CountsAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Wp0560_failure_staging_outbox_rolls_back_native_journal_source_and_original_request()
    {
        var hook = new StartWriteHook();
        await using var h = await Seed(hook);
        using var f = await PrepareIntakeHarnessAsync(h);
        var request = f.Build(await f.PrepareAsync());
        var bytes = await f.PublishAsync(request);
        hook.Before = command => command.CommandText.Contains("INSERT INTO cp6_engineering_intake_v1.outbox", StringComparison.Ordinal)
            ? throw new DbUpdateException("模拟同事务出站入账失败") : Task.CompletedTask;
        await ExpectIntakeFailure("AUTHORITY_FENCE_UNAVAILABLE", () => f.Provider().ReceiveAsync(f.Caller, bytes, default));
        hook.Before = null;
        Assert.Equal((0L, 0L, 0L, 0L, 0L, 0L), await f.CountsAsync());
        Assert.Empty(await h.Db.Database.SqlQuery<Guid>($"""
            SELECT operation_id AS "Value" FROM cp6_main_owner_v1.operations WHERE tenant_id={h.Tenant}
                AND owner='Main.Plm.EngineeringRequirement.v1'
            """).ToArrayAsync());
        Assert.Equal("NOT_OBSERVED", (await f.OriginalAsync(request)).Observation);
        Assert.Equal("DURABLY_RECEIVED", (await f.Provider().ReceiveAsync(f.Caller, bytes, default)).State);
        Assert.Equal((1L, 1L, 1L, 1L, 1L, 1L), await f.CountsAsync());
        await ExpectIntakeFailure("ENGINEERING_INTAKE_ORIGINAL_UNPROVEN",
            () => f.OriginalAsync(request, f.Provider(encryption: new EphemeralDataProtectionProvider())));
    }

    [CurrentPermissionPostgresFact]
    public async Task Wp0560_source_HMAC_and_signed_context_bind_exact_content_and_final_source_deadline()
    {
        var hook = new StartWriteHook();
        await using var h = await Seed(hook);
        using var f = await PrepareIntakeHarnessAsync(h);
        var next = await f.PrepareAsync();
        var unmodified = Encode(f.Build(next));
        var untrusted = JsonNode.Parse(unmodified)!.AsObject();
        untrusted["nextAuthorization"]!.AsObject()["query"] = null;
        await ExpectIntakeFailure("ENGINEERING_INTAKE_CONTRACT_INVALID",
            () => f.Provider().ReceiveAsync(f.Caller, Encoding.UTF8.GetBytes(untrusted.ToJsonString()), default));
        var tooLarge = new byte[MaxBody + 1];
        tooLarge.AsSpan().Fill((byte)' ');
        unmodified.CopyTo(tooLarge, tooLarge.Length - unmodified.Length);
        await ExpectIntakeFailure("ENGINEERING_INTAKE_CONTRACT_INVALID",
            () => f.Provider().ReceiveAsync(f.Caller, tooLarge, default));
        var alteredSource = f.Build(next);
        alteredSource = alteredSource with { Source = alteredSource.Source with { Unknowns = ["模拟替换正文"] } };
        await ExpectIntakeFailure("ENGINEERING_INTAKE_SOURCE_UNPROVEN",
            () => f.Provider().ReceiveAsync(f.Caller, Encode(alteredSource), default));
        var authorizedAlteration = await f.PublishAsync(alteredSource);
        await ExpectIntakeFailure("ENGINEERING_SOURCE_BINDING_INVALID",
            () => f.Provider().ReceiveAsync(f.Caller, authorizedAlteration, default));

        var alteredContext = next with { ExpiresAt = next.ExpiresAt.AddMilliseconds(-1) };
        var forged = f.Build(alteredContext);
        var forgedBytes = await f.PublishAsync(forged);
        await ExpectIntakeFailure("STALE_NEXT_CONTEXT", () => f.Provider().ReceiveAsync(f.Caller, forgedBytes, default));
        Assert.Equal((0L, 0L, 0L, 0L, 0L, 0L), await f.CountsAsync());

        var request = f.Build(next);
        var input = await f.PublishAsync(request);
        var until = DateTimeOffset.UtcNow.AddSeconds(4);
        f.Source.UseUntil = until;
        var staged = false;
        hook.Before = async command =>
        {
            if (!command.CommandText.Contains("INSERT INTO cp6_engineering_intake_v1.outbox", StringComparison.Ordinal)) return;
            staged = true;
            var wait = until - DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(100);
            if (wait > TimeSpan.Zero) await Task.Delay(wait);
        };
        await ExpectIntakeFailure("ENGINEERING_INTAKE_SOURCE_UNPROVEN", () => f.Provider().ReceiveAsync(f.Caller, input, default));
        Assert.True(staged);
        hook.Before = null;
        Assert.Equal((0L, 0L, 0L, 0L, 0L, 0L), await f.CountsAsync());
        f.Source.UseUntil = null;
        Assert.Equal("DURABLY_RECEIVED", (await f.Provider().ReceiveAsync(f.Caller, input, default)).State);
    }

    [CurrentPermissionPostgresFact]
    public async Task Wp0560_Main_context_does_not_clear_simulated_consumer_conflict_and_202_grants_no_manufacturing()
    {
        await using var h = await Seed();
        using var f = await PrepareIntakeHarnessAsync(h);
        var first = f.Build(await f.PrepareAsync());
        var receipt = await f.Provider().ReceiveAsync(f.Caller, await f.PublishAsync(first), default);
        Assert.Equal("RECEIPT_ONLY", receipt.Scope);
        Assert.Equal("UNKNOWN", receipt.BusinessOutcome);
        var intake = IntakeResult(await f.OriginalAsync(first));
        var next = await f.PrepareAsync(AfterIntake(first, intake));
        Assert.True(next.NextAllowed);
        var successor = f.Build(next, "2");
        await f.ControlAsync(localConflict: true);
        await using var simulatedCrm = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        Assert.Equal("CASE_CONFLICT_OPEN", await TrySubmitAsync());
        Assert.Equal(0L, await LocalSubmissionsAsync());
        Assert.Equal((1L, 1L, 1L, 1L, 1L, 1L), await f.CountsAsync());
        Assert.Empty(await h.Db.WorkOrders.AsNoTracking().ToArrayAsync());
        var requirement = await f.Native().ReadAsync(Guid.Parse(intake.Engineering!.EngineeringRequestId));
        Assert.Equal("DRAFT_ONLY", requirement.State);
        Assert.Contains("REQUIREMENT_APPROVAL_PENDING", requirement.Missing);
        await f.ControlAsync();
        Assert.Equal("LOCAL_ENGINEERING_REQUEST", await TrySubmitAsync());
        Assert.Equal(1L, await LocalSubmissionsAsync());

        Task<long> LocalSubmissionsAsync() => simulatedCrm.Database.SqlQuery<long>($"""
            SELECT count(*) AS "Value" FROM wp0560_simulated.consumer_submissions WHERE tenant_id={h.Tenant}
            """).SingleAsync();

        async Task<string> TrySubmitAsync()
        {
            // CRM 消费夹具复现准备后才登记问题的情形，最终本地门在本地事务内先执行。
            await using var local = await simulatedCrm.Database.BeginTransactionAsync();
            var conflict = (await simulatedCrm.Database.SqlQuery<bool>($"""
                SELECT local_conflict AS "Value" FROM wp0560_simulated.source_controls WHERE tenant_id={h.Tenant} FOR UPDATE
                """).ToArrayAsync()).Single();
            if (conflict) return "CASE_CONFLICT_OPEN";
            if (!successor.NextAuthorization.NextAllowed) return "MAIN_NEXT_CONTEXT_BLOCKED";
            await simulatedCrm.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO wp0560_simulated.consumer_submissions
                VALUES({h.Tenant},{successor.CaseId},{successor.SubmissionVersion},{successor.RequestKey})
                """);
            await local.CommitAsync();
            return "LOCAL_ENGINEERING_REQUEST";
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task Wp0560_forward_migration_preserves_preexisting_native_originals_and_permissions_without_seeding_coverage()
    {
        var database = await server.CreateIsolatedDatabaseAsync("20261126235900_EngineeringRequirementExportV1");
        await using var h = await SeedForDatabase(database);
        await GrantRequirement(h);
        var native = Requirements(h);
        var source = await CreateRequirement(native, SourceBody());
        var command = await NewRequirement(native, DefinitionBody(source.Reference));
        var original = await native.CommandAsync(command);
        var body = (await native.ReadAsync(original.ObjectId)).OriginalJson;
        var nativeRoleActionsBefore = await h.Db.Sys_RoleActions.Where(x => x.RoleId == 72)
            .OrderBy(x => x.MenuId).ThenBy(x => x.ActionCode).Select(x => new { x.MenuId, x.ActionCode }).ToArrayAsync();

        await h.Db.GetService<IMigrator>().MigrateAsync();
        Assert.Empty(await h.Db.Database.GetPendingMigrationsAsync());
        Assert.Empty(await h.Db.Database.SqlQueryRaw<Guid>("SELECT tenant_id AS \"Value\" FROM cp6_engineering_intake_v1.coverage").ToArrayAsync());
        Assert.Equal(original, (await native.OperationAsync(command.OperationId, false)).Result);
        Assert.Equal(body, (await native.ReadAsync(original.ObjectId)).OriginalJson);
        Assert.Equal(source.OriginalJson, (await native.ReadAsync(source.Reference.Id)).OriginalJson);
        Assert.Equal(original, await native.CommandAsync(command));
        Assert.Equal(nativeRoleActionsBefore, await h.Db.Sys_RoleActions.Where(x => x.RoleId == 72)
            .OrderBy(x => x.MenuId).ThenBy(x => x.ActionCode).Select(x => new { x.MenuId, x.ActionCode }).ToArrayAsync());
        using var f = await PrepareIntakeHarnessAsync(h, coverage: false, grant: false);
        await ExpectIntakeFailure("AUTHORITY_FENCE_UNAVAILABLE", () => f.PrepareAsync());
        Assert.Equal((0L, 0L, 0L, 0L, 1L, 1L), await f.CountsAsync());
    }
}
