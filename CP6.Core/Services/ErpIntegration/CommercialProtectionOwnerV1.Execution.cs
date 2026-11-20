using Npgsql;
using static CP6.Core.Services.ErpIntegration.CommercialProtectionStoreV1;
using static CP6.Core.Services.ErpIntegration.CommercialProtectionV1;

namespace CP6.Core.Services.ErpIntegration;

public sealed partial class CommercialProtectionOwnerV1
{
    private sealed record TerminalFacts(string Outcome, string? ReceiptRef, string? EffectDigest, byte[]? Receipt,
        string? AbortCode, DateTimeOffset RecordedAt);
    private sealed record AcceptedGrantSet(IcProtectionGrantV1[] Grants);
    private sealed record ExecutionRecord(string State, IcExecutionDefinitionV1 Definition, AcceptedGrantSet? Accepted,
        TerminalFacts? Terminal);

    public Task<IcExecutionOriginalV1> StartExecutionAsync(IcExecutionDefinitionV1 definition, CancellationToken ct = default)
    {
        var identity = definition.Execution;
        var frozen = Freeze(identity.TenantId, identity.ConsumerAuthority, identity.OperationKind, identity.OperationId,
            identity.ExecutionGeneration, definition.ActorSubject, definition.EffectKey, definition.IfMatch, definition.OriginalInput,
            definition.Participants, definition.AdmissionDeadline, definition.ConsumerRegistration);
        if (!Same(frozen, definition)) throw Failure("EXECUTION_BINDING_CONFLICT");
        return store.RunAsync(async (db, tx) =>
        {
            await HoldExecutionAccessAsync(db, tx, IcAuthorityActionV1.ReadExecution, identity, ct);
            var prior = await ExecutionAsync(db, tx, identity, ct);
            if (prior is null)
            {
                await HoldExecutionAccessAsync(db, tx, IcAuthorityActionV1.StartExecution, identity, ct, frozen);
                await LockExecutionAsync(db, tx, identity, ct);
                prior = await ExecutionAsync(db, tx, identity, ct);
            }
            if (prior is not null)
            {
                if (!Same(prior.Definition, frozen)) throw Failure("EXECUTION_BINDING_CONFLICT");
                return await ViewExecutionAsync(db, tx, identity, prior, ct);
            }

            var now = await store.NowAsync(db, tx, ct);
            if (now >= frozen.AdmissionDeadline) throw Failure("ADMISSION_DEADLINE_EXCEEDED");
            await using var insert = ExecutionCommand(db, tx, """
                INSERT INTO erp_integration.ic_execution_v1(tenant_id,consumer,operation_kind,operation_id,state,definition_cipher)
                VALUES(@tenant,@consumer,@operation,@id,'PREPARING',@definition)
                """, identity);
            insert.Parameters.AddWithValue("definition", store.Seal(identity.TenantId, "execution", Address(identity), frozen));
            await insert.ExecuteNonQueryAsync(ct);
            return await ViewExecutionAsync(db, tx, identity, new("PREPARING", frozen, null, null), ct);
        }, ct);
    }

    /// <summary>从已提交的 READ COMMITTED 查询签发同一终态引用，当前访问权限由消费 Owner 持守。</summary>
    public Task<IcExecutionOriginalV1> ReadExecutionAsync(IcExecutionRefV1 execution, CancellationToken ct = default)
    {
        RequireExecution(execution);
        return store.RunAsync(async (db, tx) =>
        {
            await HoldExecutionAccessAsync(db, tx, IcAuthorityActionV1.ReadExecution, execution, ct);
            var record = await ExecutionAsync(db, tx, execution, ct);
            return await ViewExecutionAsync(db, tx, execution, record, ct);
        }, ct);
    }

    /// <summary>查证完整源账的原保护后冻结不可变集合，外部查询在本地执行锁外。</summary>
    public async Task<IcExecutionOriginalV1> ReadyAsync(IcExecutionRefV1 execution, IReadOnlyList<IcProtectionGrantV1> proposed,
        CancellationToken ct = default)
    {
        RequireExecution(execution);
        var originalExecution = await store.RunAsync(async (db, tx) =>
        {
            await HoldExecutionAccessAsync(db, tx, IcAuthorityActionV1.ReadExecution, execution, ct);
            return await ExecutionAsync(db, tx, execution, ct) ?? throw Failure("EXECUTION_UNPROVEN");
        }, ct);
        var definition = originalExecution.Definition;
        var accepted = ValidateGrantSet(definition, proposed);
        if (originalExecution.State != "PREPARING")
        {
            if (originalExecution.Accepted is not null && !Same(originalExecution.Accepted, accepted))
                throw Failure("GRANT_SET_CONFLICT");
            return await ReadExecutionAsync(execution, ct);
        }

        for (var index = 0; index < accepted.Grants.Length; index++)
        {
            var grant = accepted.Grants[index];
            var original = await originals.ReadGrantAsync(definition.Participants[index], grant.ProtectionKey, execution, ct);
            if (original?.Knowledge != "GRANTED" || original.ProtectionKey != grant.ProtectionKey || !Same(original.Grant, grant))
                throw Failure("GRANT_UNPROVEN");
        }

        return await store.RunAsync(async (db, tx) =>
        {
            await HoldExecutionAccessAsync(db, tx, IcAuthorityActionV1.ReadExecution, execution, ct);
            await HoldExecutionAccessAsync(db, tx, IcAuthorityActionV1.Ready, execution, ct, definition);
            await LockExecutionAsync(db, tx, execution, ct);
            var record = await ExecutionAsync(db, tx, execution, ct) ?? throw Failure("EXECUTION_UNPROVEN");
            if (record.State == "ABORTED") return await ViewExecutionAsync(db, tx, execution, record, ct);
            if (record.Accepted is not null)
            {
                if (!Same(record.Accepted, accepted)) throw Failure("GRANT_SET_CONFLICT");
                return await ViewExecutionAsync(db, tx, execution, record, ct);
            }
            if (await store.NowAsync(db, tx, ct) >= definition.AdmissionDeadline) throw Failure("ADMISSION_DEADLINE_EXCEEDED");
            await using var ready = ExecutionCommand(db, tx, """
                UPDATE erp_integration.ic_execution_v1 SET state='READY',grant_set_cipher=@grant_set
                 WHERE tenant_id=@tenant AND consumer=@consumer AND operation_kind=@operation AND operation_id=@id AND state='PREPARING'
                """, execution);
            ready.Parameters.AddWithValue("grant_set", store.Seal(execution.TenantId, "accepted", Address(execution), accepted));
            if (await ready.ExecuteNonQueryAsync(ct) != 1) throw Failure("EXECUTION_UNPROVEN");
            return await ViewExecutionAsync(db, tx, execution, record with { State = "READY", Accepted = accepted }, ct);
        }, ct);
    }

    /// <summary>消费 Owner 把完整业务写集与原收据交给同一事务。外层提交后通过 ReadExecutionAsync 取得终态证据。</summary>
    public async Task<byte[]> CommitWithinAsync(NpgsqlConnection db, NpgsqlTransaction tx, IcExecutionRefV1 execution,
        Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<IcBusinessEffectV1>> apply, CancellationToken ct = default)
    {
        RequireExecution(execution);
        await store.RequireNativeAsync(db, tx, ct);
        await HoldExecutionAccessAsync(db, tx, IcAuthorityActionV1.ReadExecution, execution, ct);
        var current = await ExecutionAsync(db, tx, execution, ct) ?? throw Failure("EXECUTION_UNPROVEN");
        if (current.State == "COMMITTED") return Receipt(current);
        if (current.State == "ABORTED") throw Failure("EXECUTION_ABORTED");

        // 最终权限先于执行根，业务原有锁序由同事务回调保持。
        await HoldExecutionAccessAsync(db, tx, IcAuthorityActionV1.Commit, execution, ct, current.Definition);
        await LockExecutionAsync(db, tx, execution, ct);
        current = await ExecutionAsync(db, tx, execution, ct) ?? throw Failure("EXECUTION_UNPROVEN");
        if (current.State == "COMMITTED") return Receipt(current);
        if (current.State == "ABORTED") throw Failure("EXECUTION_ABORTED");
        if (current.State != "READY" || current.Accepted is null) throw Failure("PARTICIPANTS_INCOMPLETE");
        _ = ValidateGrantSet(current.Definition, current.Accepted.Grants);
        if (await store.NowAsync(db, tx, ct) >= current.Definition.AdmissionDeadline) throw Failure("ADMISSION_DEADLINE_EXCEEDED");

        return await WithinSavepointAsync(tx, async () =>
        {
            var effect = await apply(db, tx, ct);
            _ = CommercialWireV1.RequireKey(effect.BusinessReceiptRef);
            if (effect.OriginalReceipt.Length is 0 or > CommercialOriginalV1.MaximumBodyBytes) throw Failure("BUSINESS_RECEIPT_INVALID");
            var receipt = effect.OriginalReceipt.ToArray();
            var facts = new TerminalFacts("COMMITTED", effect.BusinessReceiptRef, CommercialOriginalV1.Hash(receipt), receipt, null,
                await store.NowAsync(db, tx, ct));
            await SaveTerminalAsync(db, tx, execution, current.State, facts, ct);
            return receipt;
        }, ct);
    }

    public async Task<IcExecutionOriginalV1> AbortAsync(IcExecutionRefV1 execution, string reasonCode, IcEvidenceRefV1 sourceDecision,
        CancellationToken ct = default)
    {
        RequireExecution(execution);
        CheckEvidence(sourceDecision);
        if (!IsAbortCode(reasonCode)) throw Failure("EXECUTION_ABORT_INVALID");
        await store.RunAsync(async (db, tx) =>
        {
            await HoldExecutionAccessAsync(db, tx, IcAuthorityActionV1.ReadExecution, execution, ct);
            await HoldExecutionAccessAsync(db, tx, IcAuthorityActionV1.Abort, execution, ct, evidence: sourceDecision, abortCode: reasonCode);
            await LockExecutionAsync(db, tx, execution, ct);
            var record = await ExecutionAsync(db, tx, execution, ct);
            if (record is null || record.Terminal is not null) return;
            var facts = new TerminalFacts("ABORTED", null, null, null, reasonCode, await store.NowAsync(db, tx, ct));
            await SaveTerminalAsync(db, tx, execution, record.State, facts, ct);
        }, ct);
        return await ReadExecutionAsync(execution, ct);
    }

    private async Task<IcExecutionOriginalV1> ViewExecutionAsync(NpgsqlConnection db, NpgsqlTransaction tx, IcExecutionRefV1 execution,
        ExecutionRecord? record, CancellationToken ct)
    {
        var at = await store.NowAsync(db, tx, ct);
        var facts = record?.Terminal;
        var terminal = facts is null ? null : ToTerminal(execution, facts);
        var state = new IcExecutionReadV1(execution, record?.State ?? "UNPROVEN", GrantDigests(record), terminal,
            terminal?.Outcome == "ABORTED", at);
        return new(state, facts?.Receipt);
    }

    private static string[] GrantDigests(ExecutionRecord? record) =>
        record?.Accepted?.Grants.Select(grant => grant.GrantDigest).ToArray() ?? [];

    private static byte[] Receipt(ExecutionRecord record) => record.Terminal?.Receipt ?? throw Failure("ORIGINAL_UNPROVEN");

    private static IcExecutionTerminalV1 ToTerminal(IcExecutionRefV1 execution, TerminalFacts facts)
    {
        const string terminalGeneration = "1";
        var digest = CommercialCanonicalV1.Digest("IC-TERMINAL-1",
            new { execution, facts.Outcome, facts.ReceiptRef, facts.EffectDigest, facts.AbortCode, terminalGeneration, facts.RecordedAt });
        return new(execution, facts.Outcome, EffectScope(execution), facts.ReceiptRef, facts.EffectDigest, facts.AbortCode,
            terminalGeneration, facts.RecordedAt, new(execution.ConsumerAuthority, execution.OperationId.ToString("D"), terminalGeneration, digest));
    }

    private static void RequireTerminal(IcExecutionTerminalV1 terminal)
    {
        RequireExecution(terminal.Execution);
        CheckEvidence(terminal.ConsumerEvidence);
        _ = CommercialWireV1.Revision(terminal.TerminalGeneration);
        if (terminal.EffectScope != EffectScope(terminal.Execution) || terminal.RecordedAt != Milliseconds(terminal.RecordedAt) ||
            terminal.ConsumerEvidence.Authority != terminal.Execution.ConsumerAuthority || terminal.Outcome is not ("COMMITTED" or "ABORTED"))
            throw Failure("TERMINAL_BINDING_CONFLICT");
        if (terminal.Outcome == "COMMITTED")
        {
            _ = CommercialWireV1.RequireKey(terminal.BusinessReceiptRef);
            if (!CommercialWireV1.IsDigest(terminal.EffectDigest) || terminal.AbortCode is not null) throw Failure("TERMINAL_BINDING_CONFLICT");
        }
        else if (!IsAbortCode(terminal.AbortCode) || terminal.BusinessReceiptRef is not null || terminal.EffectDigest is not null)
            throw Failure("TERMINAL_BINDING_CONFLICT");
    }

    private static bool IsAbortCode(string? reason) =>
        reason is "SOURCE_REVOKE_REQUESTED" or "PREPARATION_FAILED" or "CONTEXT_EXPIRED" or "PROGRESS_DEADLINE_EXCEEDED";

    private static AcceptedGrantSet ValidateGrantSet(IcExecutionDefinitionV1 definition, IReadOnlyList<IcProtectionGrantV1> grants)
    {
        if (grants.Count != definition.Participants.Length) throw Failure("PARTICIPANTS_INCOMPLETE");
        var byKey = new Dictionary<string, IcProtectionGrantV1>(StringComparer.Ordinal);
        foreach (var grant in grants)
            if (!byKey.TryAdd(grant.ProtectionKey, grant)) throw Failure("PARTICIPANTS_INCOMPLETE");

        var accepted = new List<IcProtectionGrantV1>();
        foreach (var participant in definition.Participants)
        {
            var key = ProtectionKey(definition.Execution, participant);
            if (!byKey.TryGetValue(key, out var grant)) throw Failure("PARTICIPANTS_INCOMPLETE");
            ValidateGrant(definition, participant, grant);
            accepted.Add(grant with { ProtectedControls = grant.ProtectedControls.ToArray() });
        }
        return new(accepted.ToArray());
    }

    private static void ValidateGrant(IcExecutionDefinitionV1 definition, IcParticipantV1 participant, IcProtectionGrantV1 grant)
    {
        CheckEvidence(grant.GrantEvidence);
        _ = CommercialWireV1.Revision(grant.SourceGeneration);
        _ = CommercialWireV1.RequireKey(grant.SharedWriterPolicy);
        if (grant.Execution != definition.Execution || grant.ParticipantAuthority != participant.ParticipantAuthority ||
            grant.Subject != participant.Subject || grant.Purpose != participant.Purpose || grant.AutomaticExpiry ||
            grant.State != "GRANTED" || grant.GrantedAt >= definition.AdmissionDeadline || grant.GrantedAt >= grant.ProgressDeadline ||
            grant.GrantEvidence.Authority != participant.ParticipantAuthority || grant.GrantDigest != GrantDigest(grant))
            throw Failure("GRANT_BINDING_CONFLICT");

        foreach (var control in grant.ProtectedControls) RequireControl(control);
        var expectedKinds = participant.Subject is IcHandoffSubjectV1
            ? new[] { "PURPOSE_POLICY", "SUBJECT" }
            : new[] { "PRODUCER_STREAM", "PURPOSE_POLICY", "SUBJECT" };
        if (grant.ProtectedControls.Any(x => x.Authority != participant.ParticipantAuthority) ||
            !grant.ProtectedControls.Select(x => x.Kind).Order(StringComparer.Ordinal).SequenceEqual(expectedKinds))
            throw Failure("SOURCE_CONTROLS_UNPROVEN");
        var subject = grant.ProtectedControls.Single(x => x.Kind == "SUBJECT");
        if (subject.Watermark != grant.SourceControlWatermark)
            throw Failure("SOURCE_CONTROLS_UNPROVEN");
    }

    private async Task<ExecutionRecord?> ExecutionAsync(NpgsqlConnection db, NpgsqlTransaction tx, IcExecutionRefV1 execution, CancellationToken ct)
    {
        await using var query = ExecutionCommand(db, tx, """
            SELECT state,definition_cipher,grant_set_cipher,terminal_cipher FROM erp_integration.ic_execution_v1
             WHERE tenant_id=@tenant AND consumer=@consumer AND operation_kind=@operation AND operation_id=@id
            """, execution);
        await using var row = await query.ExecuteReaderAsync(ct);
        if (!await row.ReadAsync(ct)) return null;
        var address = Address(execution);
        var tenant = execution.TenantId;
        var definition = store.Open<IcExecutionDefinitionV1>(tenant, "execution", address, row.GetFieldValue<byte[]>(1));
        if (definition.Execution != execution) throw Failure("EXECUTION_BINDING_CONFLICT");
        var accepted = row.IsDBNull(2) ? null : store.Open<AcceptedGrantSet>(tenant, "accepted", address, row.GetFieldValue<byte[]>(2));
        var facts = row.IsDBNull(3) ? null : store.Open<TerminalFacts>(tenant, "execution-terminal", address, row.GetFieldValue<byte[]>(3));
        return new(row.GetString(0), definition, accepted, facts);
    }

    private async Task SaveTerminalAsync(NpgsqlConnection db, NpgsqlTransaction tx, IcExecutionRefV1 execution, string currentState,
        TerminalFacts facts, CancellationToken ct)
    {
        await using var sql = ExecutionCommand(db, tx, """
            UPDATE erp_integration.ic_execution_v1 SET state=@outcome,terminal_cipher=@terminal
                WHERE tenant_id=@tenant AND consumer=@consumer AND operation_kind=@operation AND operation_id=@id AND state=@current_state
            """, execution);
        sql.Parameters.AddWithValue("outcome", facts.Outcome);
        sql.Parameters.AddWithValue("current_state", currentState);
        sql.Parameters.AddWithValue("terminal", store.Seal(execution.TenantId, "execution-terminal", Address(execution), facts));
        if (await sql.ExecuteNonQueryAsync(ct) != 1) throw Failure("EXECUTION_UNPROVEN");
    }

    private Task HoldExecutionAccessAsync(NpgsqlConnection db, NpgsqlTransaction tx, IcAuthorityActionV1 action,
        IcExecutionRefV1 execution, CancellationToken ct, IcExecutionDefinitionV1? definition = null, IcEvidenceRefV1? evidence = null,
        string? abortCode = null) =>
        authority.HoldCurrentAsync(db, tx, new(action, execution.TenantId, execution.ConsumerAuthority,
            Execution: execution, Evidence: evidence ?? definition?.ConsumerRegistration, Definition: definition, AbortCode: abortCode), ct);

    private static Task LockExecutionAsync(NpgsqlConnection db, NpgsqlTransaction tx, IcExecutionRefV1 execution, CancellationToken ct) =>
        LockKeyAsync(db, tx, "ic-execution", execution.TenantId, Address(execution), ct);

    private static string Address(IcExecutionRefV1 execution) => CommercialCanonicalV1.Digest("IC-EXECUTION-ROOT-1",
        new { execution.ConsumerAuthority, execution.OperationKind, execution.OperationId });

    private static NpgsqlCommand ExecutionCommand(NpgsqlConnection db, NpgsqlTransaction tx, string sql, IcExecutionRefV1 identity) =>
        Command(db, tx, sql, ("tenant", identity.TenantId), ("consumer", identity.ConsumerAuthority),
            ("operation", identity.OperationKind), ("id", identity.OperationId));
}
