namespace CP6.Core.Services.Wms;

internal sealed partial class ExecutionIntentStoreV1
{
    private async Task<List<Binding>> BindingsCore(CancellationToken ct)
    {
        var values = new List<Binding>();
        await using var command = Command($"SELECT body_bytes FROM cp6_execution_v1.bindings WHERE {Where} ORDER BY owner,scope_key,protection_kind");
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) values.Add(Decode<Binding>(reader.GetFieldValue<byte[]>(0)));
        return values;
    }
    internal Task<List<Binding>> Bindings(CancellationToken ct = default) => Atomic(() => BindingsCore(ct), ct);
    private async Task AddAction(Binding binding, string kind, byte[] payload, CancellationToken ct)
    {
        await Execute("""
            INSERT INTO cp6_execution_v1.actions(environment_id,sandbox_id,tenant_id,authority_key,action_id,binding_id,kind,payload_bytes,payload_digest,state,attempt_epoch,attempt_revision,observation_state)
            VALUES(@e,@s,@t,@k,@id,@binding,@kind,@body,@digest,'PENDING',0,1,'UNKNOWN')
            """, ct, ("id", Guid.NewGuid()), ("binding", binding.Id), ("kind", kind), ("body", payload), ("digest", Hash(payload)));
    }
    private async Task<List<ActionView>> ActionsCore(CancellationToken ct)
    {
        var list = new List<ActionView>();
        await using var command = Command($"SELECT action_id,binding_id,kind,payload_bytes,state,attempt_epoch,attempt_revision,observation_state,last_confirmed_bytes,last_observation_bytes FROM cp6_execution_v1.actions WHERE {Where} ORDER BY action_id");
        await using var r = await command.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(new(r.GetGuid(0), r.GetGuid(1), r.GetString(2), TextBytes(r.GetFieldValue<byte[]>(3)), r.GetString(4), r.GetInt64(5), r.GetInt64(6), r.GetString(7),
            r.IsDBNull(8) ? null : Decode<OwnerOutcome>(r.GetFieldValue<byte[]>(8)), r.IsDBNull(9) ? null : Decode<OwnerOutcome>(r.GetFieldValue<byte[]>(9))));
        return list;
    }
    internal Task<List<ActionView>> Actions(CancellationToken ct = default) => Atomic(() => ActionsCore(ct), ct);
    private async Task<ActionView> Action(Guid id, CancellationToken ct) => (await ActionsCore(ct)).SingleOrDefault(x => x.Id == id) ?? throw Error("EXECUTION_ACTION_NOT_FOUND");
    internal Task<ActionView> ClaimAction(Guid actionId, CancellationToken ct = default) => Atomic(async () =>
    {
        var i = await Current(ct); var action = await Action(actionId, ct);
        if (action.Kind == "PREPARE")
        {
            if (i.State is not ("PREPARING" or "READY")) throw Error("EXECUTION_PREPARE_CLOSED");
            if (i.State == "READY") await Move("PREPARING", i.ExecutionEpoch, null, ct);
        }
        else
        {
            if (action.Kind == "CONFIRM_COMMITTED" ? i.State != "APPLIED" : i.State != "SEALED_NO_EFFECT") throw Error("EXECUTION_ACTION_PHASE_INVALID");
            if (action.State == "SUCCEEDED") return action;
        }
        await Execute($"UPDATE cp6_execution_v1.actions SET state='WORKING',attempt_epoch=attempt_epoch+1,attempt_revision=attempt_revision+1,observation_state='UNKNOWN' WHERE {Where} AND action_id=@id", ct, ("id", actionId));
        return await Action(actionId, ct);
    }, ct);
    private static bool Confirmed(OwnerOutcome value) => value.State is "PROTECTED" or "CONFIRMED_TRANSITIONED" or "CONFIRMED_STOP_CONTROL" or "CLOSED_NO_EFFECT" or "RELEASED";
    private async Task ValidateOutcome(ActionView action, Binding binding, OwnerOutcome value, CancellationToken ct)
    {
        var prepare = binding.Prepare;
        if (value == null || value.ActionKey != action.Id || value.ActionDigest != Hash(System.Text.Encoding.UTF8.GetBytes(action.Payload))
            || value.Owner != prepare.Target.Owner || value.PrepareRequestKey != prepare.RequestKey || value.PrepareRequestDigest != binding.PrepareDigest
            || value.ExecutionIntentId != prepare.ExecutionIntentId || value.ScopeDigest != prepare.Target.ScopeDigest || value.CandidateDigest != prepare.CandidateDigest
            || value.Coverage is not ("COMPLETE" or "UNKNOWN") || value.PendingApplications < 0) throw Error("EXECUTION_OWNER_BINDING_CONFLICT");
        if (value.State is "UNKNOWN" or "NOT_OBSERVED" or "UNSUPPORTED_GUARANTEE" or "CONFLICT") return;
        var allowed = action.Kind switch
        {
            "PREPARE" => value.State == "PROTECTED",
            "CONFIRM_COMMITTED" => value.State == (prepare.Target.ProtectionKind == "STOP_NEW_USE" ? "CONFIRMED_STOP_CONTROL" : "CONFIRMED_TRANSITIONED"),
            "RELEASE_SEALED" => value.State is "CLOSED_NO_EFFECT" or "RELEASED",
            _ => false
        };
        if (!allowed || value.Coverage != "COMPLETE" || value.Receipt == null) throw Error("EXECUTION_OWNER_OUTCOME_INVALID");
        Ref(value.Receipt);
        if (value.State != "CLOSED_NO_EFFECT") { Text(value.PrepareId); Text(value.GuardEpoch); Text(value.ControlRevision); }
        var actions = await ActionsCore(ct);
        if (action.Kind == "PREPARE" && actions.Any(x => x.BindingId == binding.Id && x.Kind == "RELEASE_SEALED" && x.LastConfirmed != null))
            throw Error("EXECUTION_OWNER_CLOSURE_CONFLICT");
        var prior = actions.Single(x => x.BindingId == binding.Id && x.Kind == "PREPARE").LastConfirmed;
        // An installed guard cannot be replaced by a differently identified guard or an absent tombstone.
        if (prior != null && (value.PrepareId != prior.PrepareId || value.GuardEpoch != prior.GuardEpoch)) throw Error("EXECUTION_OWNER_GUARD_CONFLICT");
    }
    internal Task<ActionView> Observe(Guid actionId, long attemptEpoch, OwnerOutcome input, CancellationToken ct = default) => Atomic(async () =>
    {
        if (input == null) throw Error("EXECUTION_OWNER_OUTCOME_INVALID"); var value = Freeze(input); var bytes = Canonical(value);
        var action = await Action(actionId, ct);
        await using (var command = Command($"SELECT body_bytes FROM cp6_execution_v1.observations WHERE {Where} AND action_id=@id AND attempt_epoch=@epoch", ("id", actionId), ("epoch", attemptEpoch)))
        {
            var saved = await command.ExecuteScalarAsync(ct) as byte[];
            if (saved != null)
            {
                if (!saved.SequenceEqual(bytes)) throw Error("EXECUTION_OBSERVATION_INPUT_CONFLICT");
                return action;
            }
        }
        if (action.AttemptEpoch != attemptEpoch || action.State != "WORKING") throw Error("EXECUTION_ACTION_FENCE_CONFLICT");
        var binding = (await BindingsCore(ct)).Single(x => x.Id == action.BindingId);
        await ValidateOutcome(action, binding, value, ct);
        var confirmed = Confirmed(value); var conflict = value.State is "CONFLICT" or "UNSUPPORTED_GUARANTEE";
        await Execute("""
            INSERT INTO cp6_execution_v1.observations(environment_id,sandbox_id,tenant_id,authority_key,action_id,attempt_epoch,body_bytes,body_digest)
            VALUES(@e,@s,@t,@k,@id,@epoch,@body,@digest)
            """, ct, ("id", actionId), ("epoch", attemptEpoch), ("body", bytes), ("digest", Hash(bytes)));
        await Execute($"""
            UPDATE cp6_execution_v1.actions SET state=@state,attempt_revision=attempt_revision+1,observation_state=@observation,
            last_observation_bytes=@body,last_confirmed_bytes=CASE WHEN @confirmed THEN @body ELSE last_confirmed_bytes END
            WHERE {Where} AND action_id=@id
            """, ct, ("id", actionId), ("state", confirmed ? "SUCCEEDED" : conflict ? "CONFLICT" : "PENDING"),
            ("observation", confirmed ? "CONFIRMED" : conflict ? "CONFLICT" : "UNKNOWN"), ("confirmed", confirmed), ("body", bytes));
        return await Action(actionId, ct);
    }, ct);
    private async Task RequireProtected(CancellationToken ct)
    {
        var intent = await Current(ct); var bindings = await BindingsCore(ct); var actions = await ActionsCore(ct);
        if (bindings.Count != intent.Plan.RequiredOwners.Length) throw Error("EXECUTION_PROTECTION_INCOMPLETE");
        foreach (var binding in bindings)
        {
            var action = actions.Single(x => x.Kind == "PREPARE" && x.BindingId == binding.Id);
            if (action.State != "SUCCEEDED" || action.ObservationState != "CONFIRMED" || action.LastConfirmed is not { State: "PROTECTED", Coverage: "COMPLETE", PendingApplications: 0 })
                throw Error("EXECUTION_PROTECTION_INCOMPLETE");
        }
    }
}
