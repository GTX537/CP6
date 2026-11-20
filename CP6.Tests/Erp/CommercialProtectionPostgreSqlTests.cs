using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Core.Services.ErpIntegration;
using Npgsql;
using static CP6.Core.Services.ErpIntegration.CommercialProtectionV1;
using static CP6.Tests.Erp.CommercialOriginalWireTests;
using static CP6.Tests.Erp.CommercialProtectionScenario;

namespace CP6.Tests.Erp;

[Collection("CurrentPermissionPostgres")]
public sealed partial class CommercialProtectionPostgreSqlTests(CurrentPermissionPostgresFixture postgres)
{
    [CurrentPermissionPostgresFact]
    public async Task Commercial_protection_recovers_both_purposes_and_exact_published_originals()
    {
        var scenario = await CreateAsync(postgres);
        foreach (var name in new[] { "handoff", "intake", "quote", "commercial" })
        {
            var raw = FixtureOriginal(name);
            var participant = await scenario.PublishAsync(raw);
            var definition = scenario.Definition("fixture-" + name, participant);
            Assert.Equal("PREPARING", (await scenario.Owner.StartExecutionAsync(definition)).State.State);
            var input = Prepare(definition, participant);
            var calls = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => scenario.Owner.PrepareAsync(input)));
            var grant = Assert.IsType<IcProtectionGrantV1>(calls[0].Grant);
            Assert.All(calls, answer => Assert.Equivalent(grant, answer.Grant, strict: true));
            Assert.False(grant.AutomaticExpiry);
            Assert.Equal(name == "handoff" ? 2 : 3, grant.ProtectedControls.Length);
            await scenario.Owner.ReadyAsync(definition.Execution, [grant]);
            Assert.Equal(Receipt, await scenario.CommitAsync(definition));

            scenario.RestartOwner();
            Assert.Equal(raw.OriginalBytes(), (await scenario.Owner.ReadSourceOriginalAsync(FixtureTenant, participant))!.OriginalBytes());
            var reopened = await scenario.Owner.ReadExecutionAsync(definition.Execution);
            Assert.Equal(Receipt, reopened.OriginalReceipt);
            var terminal = Assert.IsType<IcExecutionTerminalV1>(reopened.State.Terminal);
            Assert.Equal("COMMITTED", terminal.Outcome);
            Assert.Equal(name == "handoff" ? "MAIN_INTAKE" : "CRM_PROJECTION", terminal.EffectScope);
            Assert.Equal(CommercialOriginalV1.Hash(Receipt), terminal.EffectDigest);
            var settled = await scenario.Owner.SettleAsync(grant.ParticipantAuthority, grant.ProtectionKey, definition.Execution, terminal);
            Assert.Equal("SETTLED", settled.Knowledge);
            Assert.Equivalent(grant, settled.Grant, strict: true);
            Assert.Equal(terminal, settled.Terminal);
            Assert.Equal(Receipt, await scenario.CommitAsync(definition));
        }
        Assert.Equal(4, await scenario.CountAsync("SELECT count(*) FROM erp_integration.ic_grant_v1 WHERE state='SETTLED'"));
        Assert.Equal(4, await scenario.CountAsync("SELECT count(*) FROM erp_integration.ic_execution_v1 WHERE state='COMMITTED'"));
        Assert.Equal(4, await scenario.CountAsync("SELECT count(*) FROM ic_fixture.effect"));
    }

    [CurrentPermissionPostgresFact]
    public async Task Commercial_protection_blocks_unsupported_purpose_replacement_original_and_cross_scope()
    {
        var scenario = await CreateAsync(postgres);
        var raw = FixtureOriginal("handoff");
        var participant = await scenario.PublishAsync(raw);
        var definition = scenario.Definition("fixture-negative", participant);
        await scenario.Owner.StartExecutionAsync(definition);
        var input = Prepare(definition, participant);
        var unknown = input with { Purpose = "MANUAL_OVERRIDE" };
        await Denial("PROTECTION_PURPOSE_UNSUPPORTED", async () => await scenario.Owner.PrepareAsync(unknown));
        var established = await scenario.Owner.PrepareAsync(input);
        Assert.Equal("GRANTED", established.Knowledge);
        Assert.Equal("ABSENT", (await scenario.Owner.ReadProtectionAsync("wrong-owner", input.ProtectionKey, definition.Execution)).Knowledge);
        await Denial("PROTECTION_KEY_CONFLICT", async () => await scenario.Owner.PrepareAsync(input with { ExpectedControlWatermark = "v2" }));
        await Denial("EXECUTION_BINDING_CONFLICT", async () => await scenario.Owner.ReadExecutionAsync(definition.Execution with { ExecutionGeneration = "2" }));
        await Denial("CURRENT_AUTHORITY_DENIED", async () => await scenario.Owner.ReadProtectionAsync(input.ParticipantAuthority, input.ProtectionKey,
            definition.Execution with { TenantId = Guid.NewGuid() }));
        var changed = JsonNode.Parse(raw.OriginalBytes())!;
        changed["eventId"] = "21111111-1111-4111-8111-111111111112";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(changed);
        var replacement = CommercialOriginalV1.Capture(raw.Route, CommercialOriginalV1.Hash(bytes), bytes);
        await Denial("SUBJECT_BINDING_CONFLICT", async () => await scenario.PublishAsync(replacement));
        Assert.Equal(1, await scenario.CountAsync("SELECT count(*) FROM erp_integration.ic_source_publication_v1"));
        Assert.Equal(1, await scenario.CountAsync("SELECT count(*) FROM erp_integration.ic_grant_v1"));
        Assert.Equal(0, await scenario.CountAsync("SELECT count(*) FROM ic_fixture.effect"));
    }

    [CurrentPermissionPostgresFact]
    public async Task Commercial_protection_requirements_are_frozen_before_preparation()
    {
        var scenario = await CreateAsync(postgres);
        var raw = FixtureOriginal("handoff");
        var principal = await scenario.PublishAsync(raw);
        var evidence = await scenario.PublishAsync(raw, "fixture-evidence-owner");
        var definition = scenario.Definition("fixture-participants", principal, evidence);
        await scenario.Owner.StartExecutionAsync(definition);
        var grant = (await scenario.Owner.PrepareAsync(Prepare(definition, principal))).Grant!;
        await Denial("PARTICIPANTS_INCOMPLETE", async () => await scenario.Owner.ReadyAsync(definition.Execution, [grant]));
        await Denial("PARTICIPANTS_INCOMPLETE", async () => await scenario.Owner.ReadyAsync(definition.Execution, [grant, grant]));
        var changed = definition with { Participants = [principal] };
        await Denial("EXECUTION_BINDING_CONFLICT", async () => await scenario.Owner.StartExecutionAsync(changed));
        var second = (await scenario.Owner.PrepareAsync(Prepare(definition, evidence))).Grant!;
        scenario.Originals.GrantUnavailable = true;
        await Denial("GRANT_UNPROVEN", async () => await scenario.Owner.ReadyAsync(definition.Execution, [grant, second]));
        Assert.Equal("PREPARING", (await scenario.Owner.ReadExecutionAsync(definition.Execution)).State.State);
        Assert.Equal(0, await scenario.CountAsync("SELECT count(*) FROM ic_fixture.effect"));
        scenario.Originals.GrantUnavailable = false;
        var ready = await scenario.Owner.ReadyAsync(definition.Execution, [second, grant]);
        Assert.Equal("READY", ready.State.State);
        Assert.Equal(2, ready.State.AcceptedGrantDigests.Length);
        Assert.Equal(Receipt, await scenario.CommitAsync(definition));
    }

    [CurrentPermissionPostgresFact]
    public async Task Commercial_protection_holds_past_progress_deadline_until_exact_terminal()
    {
        var scenario = await CreateAsync(postgres);
        var participant = await scenario.PublishAsync(FixtureOriginal("handoff"));
        var definition = scenario.Definition("fixture-unknown", participant);
        var grant = await scenario.ReadyAsync(definition);
        var subject = grant.ProtectedControls.Single(item => item.Kind == "SUBJECT");
        var revocation = new IcProtectionRevocationV1(Guid.NewGuid(), "subject-2", "fixture-revoke", Decision);
        var progress = await scenario.Owner.RevokeAsync(FixtureTenant, subject, revocation);
        Assert.Equal("BLOCKED", progress.NewGrants);
        Assert.Equal("PENDING_TERMINAL", progress.ExistingGrants);
        Assert.Equal([grant.ProtectionKey], progress.PendingProtectionKeys);

        var later = scenario.Definition("fixture-blocked", participant);
        await scenario.Owner.StartExecutionAsync(later);
        var denied = await scenario.Owner.PrepareAsync(Prepare(later, participant));
        Assert.Equal("DENIED", denied.Knowledge);
        Assert.Equal("SOURCE_NEW_GRANTS_BLOCKED", denied.DenialCode);
        Assert.True(denied.NewGrantsBlocked);

        scenario.Clock.Utc = grant.ProgressDeadline.AddDays(3);
        scenario.RestartOwner();
        var execution = definition.Execution;
        var forged = new IcExecutionTerminalV1(execution, "ABORTED", "MAIN_INTAKE", null, null, "SOURCE_REVOKE_REQUESTED",
            "1", scenario.Clock.Utc, new(execution.ConsumerAuthority, execution.OperationId.ToString("D"), "1", "fixture-pending-proof"));
        Assert.Null((await scenario.Owner.ReadExecutionAsync(execution)).State.Terminal);
        Assert.Equal("GRANTED", (await scenario.Owner.SettleAsync(grant.ParticipantAuthority, grant.ProtectionKey, execution, forged)).Knowledge);
        await scenario.AllowAsync(IcAuthorityActionV1.ReadSourceOriginal, false);
        await Denial("CURRENT_AUTHORITY_DENIED", async () => await scenario.Owner.ReadSourceOriginalAsync(FixtureTenant, participant));
        await scenario.AllowAsync(IcAuthorityActionV1.ReadSourceOriginal, true);
        var absent = execution with { OperationId = Guid.NewGuid() };
        var unproven = await scenario.Owner.AbortAsync(absent, "PROGRESS_DEADLINE_EXCEEDED", Decision);
        Assert.Equal("UNPROVEN", unproven.State.State);
        Assert.False(unproven.State.NoEffectProven);
        Assert.Equal("PENDING_TERMINAL", (await scenario.Owner.RevokeAsync(FixtureTenant, subject, revocation)).ExistingGrants);

        var aborted = await scenario.Owner.AbortAsync(execution, "SOURCE_REVOKE_REQUESTED", Decision);
        Assert.Equal("ABORTED", aborted.State.State);
        Assert.True(aborted.State.NoEffectProven);
        Assert.Null(aborted.OriginalReceipt);
        await Denial("TERMINAL_BINDING_CONFLICT", async () =>
            await scenario.Owner.SettleAsync(grant.ParticipantAuthority, grant.ProtectionKey, execution, aborted.State.Terminal! with { Execution = absent }));

        scenario.Originals.TerminalUnavailable = true;
        Assert.Equal("GRANTED", (await scenario.Owner.SettleAsync(grant.ParticipantAuthority, grant.ProtectionKey, execution, aborted.State.Terminal!)).Knowledge);
        scenario.Originals.TerminalUnavailable = false;
        Assert.Equal("GRANTED", (await scenario.Owner.SettleAsync(grant.ParticipantAuthority, grant.ProtectionKey, execution, forged)).Knowledge);
        Assert.Equal("SETTLED", (await scenario.Owner.SettleAsync(grant.ParticipantAuthority, grant.ProtectionKey, execution, aborted.State.Terminal!)).Knowledge);
        var completed = await scenario.Owner.RevokeAsync(FixtureTenant, subject, revocation);
        Assert.Equal("SETTLED", completed.ExistingGrants);
        Assert.Equal("subject-2", completed.EffectiveControlWatermark);
        Assert.Empty(completed.PendingProtectionKeys);
        Assert.Equal(0, await scenario.CountAsync("SELECT count(*) FROM ic_fixture.effect"));
    }

    [CurrentPermissionPostgresFact]
    public async Task Commercial_protection_outer_commit_wins_waiting_abort_and_abort_first_rejects_late_worker()
    {
        var scenario = await CreateAsync(postgres);
        var participant = await scenario.PublishAsync(FixtureOriginal("handoff"));
        var winner = scenario.Definition("fixture-commit-wins", participant);
        var grant = await scenario.ReadyAsync(winner);
        await using var consumer = await scenario.OpenAsync();
        await using var outer = await consumer.BeginTransactionAsync();
        Assert.Equal(Receipt, await scenario.Owner.CommitWithinAsync(consumer, outer, winner.Execution, Apply(winner.EffectKey)));
        Assert.Equal(0, await scenario.CountAsync("SELECT count(*) FROM ic_fixture.effect"));
        Assert.Null((await scenario.Owner.ReadExecutionAsync(winner.Execution)).State.Terminal);
        var revoked = await scenario.Owner.RevokeAsync(FixtureTenant, grant.ProtectedControls[0],
            new(Guid.NewGuid(), "policy-2", "fixture-revoke", Decision));
        Assert.Equal("PENDING_TERMINAL", revoked.ExistingGrants);
        var abort = scenario.Owner.AbortAsync(winner.Execution, "SOURCE_REVOKE_REQUESTED", Decision);
        await using var observer = await scenario.OpenAsync();
        await WaitForLockAsync(observer, consumer.ProcessID, () => scenario.Gate.AbortPid);
        Assert.False(abort.IsCompleted);
        await outer.CommitAsync();
        var committed = await abort;
        Assert.Equal("COMMITTED", committed.State.State);
        Assert.False(committed.State.NoEffectProven);
        Assert.Equal(Receipt, committed.OriginalReceipt);
        Assert.Equal("SETTLED", (await scenario.Owner.SettleAsync(grant.ParticipantAuthority, grant.ProtectionKey, winner.Execution, committed.State.Terminal!)).Knowledge);

        var resultSource = await scenario.PublishAsync(FixtureOriginal("quote"));
        var loser = scenario.Definition("fixture-abort-wins", resultSource);
        var otherGrant = await scenario.ReadyAsync(loser);
        var aborted = await scenario.Owner.AbortAsync(loser.Execution, "SOURCE_REVOKE_REQUESTED", Decision);
        Assert.Equal("ABORTED", aborted.State.State);
        Assert.True(aborted.State.NoEffectProven);
        await Denial("EXECUTION_ABORTED", async () => await scenario.CommitAsync(loser));
        Assert.Equal("SETTLED", (await scenario.Owner.SettleAsync(otherGrant.ParticipantAuthority, otherGrant.ProtectionKey, loser.Execution, aborted.State.Terminal!)).Knowledge);
        Assert.Equal(1, await scenario.CountAsync("SELECT count(*) FROM ic_fixture.effect"));
    }

    [CurrentPermissionPostgresFact]
    public async Task Commercial_protection_rolls_back_and_recovers_receipt_under_current_read_only()
    {
        var scenario = await CreateAsync(postgres);
        var raw = FixtureOriginal("quote");
        var participant = await scenario.PublishAsync(raw);
        var definition = scenario.Definition("fixture-rollback", participant);
        var grant = await scenario.ReadyAsync(definition);
        await using var db = await scenario.OpenAsync();
        await using (var tx = await db.BeginTransactionAsync())
        {
            await scenario.Owner.CommitWithinAsync(db, tx, definition.Execution, Apply(definition.EffectKey));
            await tx.RollbackAsync();
        }
        Assert.Equal("READY", (await scenario.Owner.ReadExecutionAsync(definition.Execution)).State.State);
        Assert.Equal(0, await scenario.CountAsync("SELECT count(*) FROM ic_fixture.effect"));
        await using (var tx = await db.BeginTransactionAsync())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => scenario.Owner.CommitWithinAsync(db, tx, definition.Execution,
                async (conn, transaction, ct) =>
                {
                    await Apply(definition.EffectKey)(conn, transaction, ct);
                    throw new InvalidOperationException("模拟业务写故障");
                }));
            await tx.CommitAsync();
        }
        Assert.Equal(0, await scenario.CountAsync("SELECT count(*) FROM ic_fixture.effect"));
        await scenario.CommitAsync(definition);
        await scenario.AllowAsync(IcAuthorityActionV1.Commit, false);
        await scenario.AllowAsync(IcAuthorityActionV1.Prepare, false);
        await scenario.AllowAsync(IcAuthorityActionV1.StartExecution, false);
        Assert.Equal(Receipt, await scenario.CommitAsync(definition));
        Assert.Equal(Receipt, (await scenario.Owner.StartExecutionAsync(definition)).OriginalReceipt);
        Assert.Equivalent(grant, (await scenario.Owner.PrepareAsync(Prepare(definition, participant))).Grant, strict: true);
        Assert.Equal("GRANTED", (await scenario.Owner.ReadProtectionAsync(grant.ParticipantAuthority, grant.ProtectionKey, definition.Execution)).Knowledge);

        await scenario.AllowAsync(IcAuthorityActionV1.ReadExecution, false);
        await scenario.AllowAsync(IcAuthorityActionV1.ReadProtection, false);
        await scenario.AllowAsync(IcAuthorityActionV1.ReadSourceOriginal, false);
        await Denial("CURRENT_AUTHORITY_DENIED", async () => await scenario.CommitAsync(definition));
        await Denial("CURRENT_AUTHORITY_DENIED", async () => await scenario.Owner.ReadExecutionAsync(definition.Execution));
        await Denial("CURRENT_AUTHORITY_DENIED", async () => await scenario.Owner.ReadProtectionAsync(grant.ParticipantAuthority, grant.ProtectionKey, definition.Execution));
        await Denial("CURRENT_AUTHORITY_DENIED", async () => await scenario.Owner.ReadSourceOriginalAsync(FixtureTenant, participant));
        Assert.Equal(1, await scenario.CountAsync("SELECT count(*) FROM ic_fixture.effect"));
    }

    [CurrentPermissionPostgresFact]
    public async Task Commercial_protection_stream_revocation_covers_all_active_subjects_and_forward_migration_preserves_C03()
    {
        var scenario = await CreateAsync(postgres, forward: true);
        Assert.Equal(1, await scenario.CountAsync("""
            SELECT count(*) FROM erp_integration."Aggregate"
              WHERE "LastRequestVersion"=7 AND "LastAggregateVersion"=9 AND "LastResultVersion"=11
            """));
        var raw = FixtureOriginal("quote");
        var first = await scenario.PublishAsync(raw);
        var variant = JsonNode.Parse(raw.OriginalBytes())!;
        variant["quoteKey"] = "fixture-another-quote";
        variant["eventId"] = "fixture-another-quote-event";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(variant);
        var second = await scenario.PublishAsync(CommercialOriginalV1.Capture(raw.Route, CommercialOriginalV1.Hash(bytes), bytes));
        var a = scenario.Definition("fixture-stream-a", first);
        var b = scenario.Definition("fixture-stream-b", second);
        var ga = await scenario.ReadyAsync(a);
        var gb = await scenario.ReadyAsync(b);
        var stream = ga.ProtectedControls.Single(x => x.Kind == "PRODUCER_STREAM");
        Assert.Equal(stream, gb.ProtectedControls.Single(x => x.Kind == "PRODUCER_STREAM"));
        var request = new IcProtectionRevocationV1(Guid.NewGuid(), "stream-2", "fixture-epoch-retired", Decision);
        var revoke = await scenario.Owner.RevokeAsync(FixtureTenant, stream, request);
        Assert.Equal(2, revoke.PendingProtectionKeys.Length);

        await AbortAndSettle(a, ga);
        Assert.Equal([gb.ProtectionKey], (await scenario.Owner.RevokeAsync(FixtureTenant, stream, request)).PendingProtectionKeys);
        await AbortAndSettle(b, gb);
        Assert.Equal("SETTLED", (await scenario.Owner.RevokeAsync(FixtureTenant, stream, request)).ExistingGrants);
        Assert.Equal(0, await scenario.CountAsync("SELECT count(*) FROM ic_fixture.effect"));

        async Task AbortAndSettle(IcExecutionDefinitionV1 original, IcProtectionGrantV1 grant)
        {
            var decided = await scenario.Owner.AbortAsync(original.Execution, "SOURCE_REVOKE_REQUESTED", Decision);
            await scenario.Owner.SettleAsync(grant.ParticipantAuthority, grant.ProtectionKey, original.Execution, decided.State.Terminal!);
        }
    }

    private static async Task WaitForLockAsync(NpgsqlConnection observer, int blocker, Func<int> waiting)
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (true)
        {
            await using var lockCheck = new NpgsqlCommand("SELECT @blocker = ANY(pg_blocking_pids(@waiting))", observer);
            lockCheck.Parameters.AddWithValue("blocker", blocker);
            lockCheck.Parameters.AddWithValue("waiting", waiting());
            if (await lockCheck.ExecuteScalarAsync(limit.Token) is true) return;
            await Task.Delay(20, limit.Token);
        }
    }

    private static async Task Denial(string code, Func<Task> activity) =>
        Assert.Equal(code, (await Assert.ThrowsAsync<CommercialProtocolException>(activity)).Code);
}
