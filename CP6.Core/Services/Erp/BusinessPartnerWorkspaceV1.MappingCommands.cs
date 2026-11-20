using System.Text.Json;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Erp;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Erp;

public sealed partial class BusinessPartnerWorkspaceV1
{
    private sealed record MappingIntent(MappingContextReference Context, DecisionSubject Subject, Guid? ObservationId,
        long? Revision, JsonElement NewPartner, Guid? DecisionBindingId, string CandidateSetHash, MappingSourceReference[] DecisionEvidence);
    private sealed record MappingApproval(JsonElement Intent, MappingContextBody Context, Guid? IdentityId, long? TargetBpRevision,
        string? SourceHash, bool CanApply);
    private sealed record MappingPlan(MappingRevisionRow? Previous, MappingAccount Identity, BusinessPartner? Target,
        Head? TargetHead, CreateIntent? Create, Proposal? Proposal, CorrectionBody? Correction);

    public Task<HttpResult> PrepareMappingPreview(JsonElement input, Guid? financeContext, CancellationToken ct)
    {
        var intent = ParseMappingIntent(input);
        var legal = intent.Subject.LegalEntityKey;
        var account = intent.Subject.AccountId;
        return owner.ExecuteAsync(Resource, MappingAction, true, async (access, token) =>
        {
            var scope = await MappingAccess(access, legal, false, token);
            await access.ActionAsync(Resource, MappingRead, token);
            var context = await GetMappingContext(access, scope, intent, token);
            var slot = await ReadMappingSlot(legal, account, false, token);
            var prior = await MappingRevisionAt(legal, account, slot?.Revision, token);
            MappingPlan? plan = null;
            Issue[] issues = [];
            try
            {
                plan = await PlanMapping(access, scope, intent, context, slot, prior, financeContext, token);
                issues = plan.Proposal?.Issues ?? [];
            }
            catch (OrderPriceCorrectionException error) when (error.Status is 409 or 422)
            {
                issues = [new("action", error.Message)];
            }
            var expiry = MappingExpires();
            var id = Guid.NewGuid();
            var hash = Hash(input);
            var approval = new MappingApproval(input, context, plan?.Identity.Id, plan?.TargetHead?.Revision, plan?.Proposal?.Sources, issues.Length == 0);
            await SaveMappingArtifact("PREVIEW", id, access, scope, account, expiry, hash, approval, token);
            return new HttpResult(new
            {
                reference = new PreviewReference(id, hash, Instant(expiry)), canApply = approval.CanApply, issues,
                candidates = context.Candidates, expectedBpRevision = OptionalRevision(approval.TargetBpRevision),
                expectedMappingRevision = intent.Subject.ExpectedMappingRevision, canonicalIntentHash = hash
            }, PreviewTag(id, hash));
        }, ct);
    }

    public Task<HttpResult> ApplyMapping(JsonElement input, string? ifMatch, string? commandKey, Guid? financeContext, CancellationToken ct)
    {
        var reference = MappingApplyReference(input);
        return MappingCommand("MAPPING_APPLY", ParseId(commandKey), input, ifMatch,
            async (actor, token) =>
            {
                var preview = await FindMappingArtifact("PREVIEW", reference.PreviewId, token);
                return preview?.Actor == actor ? preview.Legal : null;
            }, async (access, command, fingerprint, token) =>
            {
                var saved = await FindMappingArtifact("PREVIEW", reference.PreviewId, token);
                if (saved is null || saved.Actor != access.ActorId) throw Missing();
                var scope = await MappingAccess(access, saved.Legal, true, token);
                await access.ActionAsync(Resource, MappingAction, token);
                await access.ActionAsync(Resource, MappingRead, token);
                RequireStrongTag(ifMatch, PreviewTag(saved.Id, saved.Hash), stale: true);
                if (saved.Hash != reference.IntentHash || Instant(saved.Expires) != reference.ExpiresAt || saved.Expires <= Now) throw Stale();
                if (await db.Database.SqlQuery<bool>($"""
                     SELECT EXISTS(SELECT 1 FROM main_bp.mapping_consumed_previews WHERE tenant_id={Tenant} AND preview_id={saved.Id}) AS "Value"
                    """).SingleAsync(token)) throw Rejected("PREVIEW_ALREADY_USED", 409);
                var approval = OpenArtifact<MappingApproval>("PREVIEW", saved);
                if (!approval.CanApply || Hash(approval.Intent) != saved.Hash) throw Stale();
                var intent = ParseMappingIntent(approval.Intent);
                if (intent.Subject.AccountId != saved.Account || intent.Subject.LegalEntityKey != saved.Legal) throw Stale();
                if (intent.Subject.Action == "CREATE_AND_LINK")
                    await AuthorizeCreate(access, MappingCreateIntent(saved.Legal, intent.NewPartner), await BasesAsync(access, saved.Legal, token), token);
                if (intent.Subject.Action == "CORRECT_CURRENT") await access.ActionAsync(Resource, MappingCorrect, token);
                if (saved.Permission != access.ReadScopeVersion) throw Stale();
                var slot = await LockMappingSlot(saved.Legal, saved.Account, token);
                var previous = await MappingRevisionAt(saved.Legal, saved.Account, slot.Revision, token);
                await LockMappingClaims(saved.Legal, saved.Account, previous, intent.Subject.TargetBpId, token);
                if (scope.Revision != saved.ScopeRevision) throw Stale();
                var context = await GetMappingContext(access, scope, intent, token);
                if (Hash(context) != Hash(approval.Context)) throw Stale();
                var plan = await PlanMapping(access, scope, intent, context, slot, previous, financeContext, token);
                if (plan.Identity.Id != approval.IdentityId || plan.TargetHead?.Revision != approval.TargetBpRevision ||
                    plan.Proposal?.Sources != approval.SourceHash) throw Stale();
                if (plan.Proposal is { } proposed) RejectIssues(proposed.Issues);
                return new HttpResult(await CommitMapping(access, scope, slot, saved, intent, plan, command, fingerprint, token));
            }, ct);
    }

    private Task<HttpResult> MappingCommand(string operation, Guid command, JsonElement body, string? ifMatch,
        Func<Guid, CancellationToken, Task<string?>> planLegal,
        Func<OperationsAccessV1, Guid, byte[], CancellationToken, Task<HttpResult>> execute, CancellationToken ct)
        => owner.ExecuteAsync(Resource, MappingRead, true, async (access, token) =>
        {
            var fingerprint = Sha(Canonical(new { actor = access.ActorId, operation, ifMatch, body,
                path = "/api/main/v1/bp/" + operation.ToLowerInvariant().Replace('_', '-') }));
            var original = await FindCommand(command, token);
            if (original is not null) return new HttpResult(await Replay(access, original, fingerprint, token));
            return await execute(access, command, fingerprint, token);
        }, ct, scopedActionAtFence: async (actor, token) =>
        {
            var prior = await FindCommand(command, token);
            if (prior is not null && prior.Actor != actor) throw Missing();
            var legal = prior?.Legal ?? await planLegal(actor, token) ?? throw Missing();
            if (await BpUseScopeGate.IsCutoverAsync(db, token))
                await BpUseScopeGate.LockAsync(db, legal, prior is null, token);
            else if (prior is null)
                throw BpUseScopeGate.LockUnproven();
            await BpUseScopeGate.LockCommandAsync(db, command, token);
            prior = await FindCommand(command, token);
            if (prior is not null && (prior.Actor != actor || prior.Legal != legal))
            {
                if (prior.Actor != actor) throw Missing();
                throw Rejected("COMMAND_IDEMPOTENCY_CONFLICT", 409);
            }
            return prior is null ? MappingRead : CommandRead;
        });

    private static PreviewReference MappingApplyReference(JsonElement input)
    {
        var fields = Exact(Exact(input, "preview")["preview"], "previewId", "intentHash", "expiresAt");
        return new(Uuid(fields["previewId"]), Digest(fields["intentHash"]), Token(fields["expiresAt"], 40));
    }

    private static MappingIntent ParseMappingIntent(JsonElement input)
    {
        var fields = Exact(input, "context", "legalEntityKey", "accountId", "observationId", "expectedMappingRevision",
            "action", "targetBpId", "newPartner", "correctionContextId", "decisionBindingId", "candidateSetHash", "decisionEvidence", "reason");
        var reference = Exact(fields["context"], "contextId", "contextHash", "expiresAt");
        var context = new MappingContextReference(Uuid(reference["contextId"]), Digest(reference["contextHash"]), Token(reference["expiresAt"], 40));
        var version = OptionalVersion(fields["expectedMappingRevision"]);
        var subject = new DecisionSubject(LegalKey(Token(fields["legalEntityKey"], 64)), Uuid(fields["accountId"]),
            Token(fields["action"], 20), OptionalRevision(version), OptionalId(fields["targetBpId"]),
            OptionalId(fields["correctionContextId"]), Text(fields["reason"], 200));
        var partner = fields["newPartner"];
        var binding = OptionalId(fields["decisionBindingId"]);
        var evidence = SourceReferences(fields["decisionEvidence"]);
        if (!MappingActions.Contains(subject.Action, StringComparer.Ordinal) ||
            (subject.Action == "CREATE_AND_LINK") != (partner.ValueKind != JsonValueKind.Null) ||
            (subject.Action == "CORRECT_CURRENT") != subject.CorrectionContextId.HasValue ||
            subject.Action is "LINK" or "REPLACE" && subject.TargetBpId is null ||
            subject.Action is "UNLINK" or "CREATE_AND_LINK" && subject.TargetBpId is not null ||
            subject.Action is "CONFIRM" or "CREATE_AND_LINK" && (binding is not null || evidence.Length != 0))
            throw Invalid();
        return new(context, subject, OptionalId(fields["observationId"]), version, partner, binding, Digest(fields["candidateSetHash"]), evidence);
    }

    private async Task<MappingContextBody> GetMappingContext(OperationsAccessV1 access, Scope scope, MappingIntent intent, CancellationToken ct)
    {
        var saved = await FindMappingArtifact("CONTEXT", intent.Context.ContextId, ct);
        EnsureArtifact(saved, access, scope.Key, intent.Subject.AccountId, Now);
        var context = OpenArtifact<MappingContextBody>("CONTEXT", saved!);
        if (saved!.ScopeRevision != scope.Revision) throw Stale();
        if (saved.Hash != intent.Context.ContextHash || Instant(saved.Expires) != intent.Context.ExpiresAt ||
            MappingContextHash(saved.Id, access.ActorId, scope.Revision, context, saved.Expires) != saved.Hash ||
            intent.ObservationId != context.Request.ObservationId ||
            intent.CandidateSetHash != context.Candidates.SetHash || intent.Subject.ExpectedMappingRevision != context.Head.MappingRevision)
            throw Rejected("MAPPING_CONTEXT_MISMATCH", 409);
        return context;
    }

    private async Task<MappingPlan> PlanMapping(OperationsAccessV1 access, Scope scope, MappingIntent intent,
        MappingContextBody context, MappingSlot? slot, MappingRevisionRow? previous, Guid? financeContext, CancellationToken ct)
    {
        var subject = intent.Subject;
        var account = subject.AccountId;
        if (slot?.OwnerConflict == true && subject.Action is not ("CONFIRM" or "CORRECT_CURRENT"))
            throw Rejected("OWNER_STREAM_CONFLICT", 409);
        if (previous?.Revision != intent.Revision) throw Stale();
        var candidates = await MappingCandidates(access, scope, account, context.Request.CandidateCriteria, previous, ct);
        if (candidates.SetHash != intent.CandidateSetHash) throw Stale();
        var observed = await CurrentMappingAccount(scope.Key, account, intent.ObservationId, access.ActorId, ct);
        var priorTarget = previous?.BpId;
        var targetId = subject.TargetBpId;
        var newLink = subject.Action is "LINK" or "CREATE_AND_LINK" or "REPLACE";
        if ((subject.Action is "LINK" or "CREATE_AND_LINK") && priorTarget is not null ||
            subject.Action == "REPLACE" && (priorTarget is null || priorTarget == targetId) ||
            subject.Action == "UNLINK" && priorTarget is null ||
            (subject.Action is "CONFIRM" or "CORRECT_CURRENT") && (previous is null || priorTarget != targetId))
            throw Rejected("MAPPING_ACTION_CONFLICT", 409);
        if (newLink && (observed is null || candidates.Visibility != "COMPLETE" || context.Request.CandidateCriteria is null))
            throw Rejected("ACCOUNT_OR_CANDIDATES_UNPROVEN", 409);
        var identity = newLink ? observed : await ReadAccount(scope.Key, account, previous?.IdentityId, ct);
        if (identity is null) throw Rejected("ACCOUNT_IDENTITY_UNPROVEN", 409);
        if (!newLink && subject.Action != "UNLINK" && observed is not null && observed.IdentityHash != identity.IdentityHash)
            throw Rejected("ACCOUNT_IDENTITY_UNPROVEN", 409);

        Head? head = null;
        BusinessPartner? target = null;
        if (targetId is { } bpId)
        {
            if (newLink)
            {
                var candidate = candidates.Items.SingleOrDefault(x => x.Reference.BpId == bpId);
                if (candidate?.Availability == "CLAIMED") throw Rejected("MAPPING_TARGET_CLAIMED", 409);
                if (candidate?.Availability != "SELECTABLE") throw Rejected("MAPPING_TARGET_NOT_SELECTABLE", 409);
            }
            var visible = await VisibleAsync(access, bpId, false, ct);
            head = Adopted(visible.Head);
            target = visible.Row;
            if (head.Legal != scope.Key || newLink && (Lifecycle(target) != Active || !HasRole(target, "Customer")))
                throw Rejected("MAPPING_TARGET_NOT_SELECTABLE", 409);
            RequireBase(await BasesAsync(access, scope.Key, ct), Base(target), newLink);
        }

        // 解除或换指也要复核旧伙伴的当前可见范围及管理授权。
        if (priorTarget is { } old && old != targetId)
        {
            var accessible = await VisibleAsync(access, old, false, ct);
            if (accessible.Head?.Legal != scope.Key) throw Missing();
            RequireBase(await BasesAsync(access, scope.Key, ct), Base(accessible.Row), true);
        }

        CreateIntent? create = null;
        Proposal? proposal = null;
        if (subject.Action == "CREATE_AND_LINK")
        {
            create = MappingCreateIntent(scope.Key, intent.NewPartner);
            if (!HasRole(create.Row, "Customer") || create.Decision.Kind != "NEW_DISTINCT" || create.Decision.Acknowledged.Length != 0 ||
                candidates.Items.Length != 0 || context.Request.CandidateCriteria != Criteria(create.Row))
                throw Rejected("MAPPING_CREATE_REQUIRES_EMPTY_CANDIDATES", 409);
            proposal = await ProposeCreate(access, create, scope, financeContext, ct);
        }
        CorrectionBody? correction = null;
        if (subject.CorrectionContextId is { } correctionId)
        {
            await access.ActionAsync(Resource, MappingCorrect, ct);
            correction = await RequireMappingCorrection(access, scope.Key, account, correctionId, previous, ct);
        }
        await RequireDecisionBinding(access, subject, intent.DecisionEvidence, intent.DecisionBindingId, correction?.OwnerEvidence ?? [], ct);
        return new(previous, identity, target, head, create, proposal, correction);
    }

    private static CreateIntent MappingCreateIntent(string legal, JsonElement newPartner)
    {
        var nested = Exact(newPartner, "registration", "identity", "duplicateDecision");
        return ParseCreate(JsonSerializer.SerializeToElement(new
        {
            legalEntityKey = legal, registration = nested["registration"], identity = nested["identity"], duplicateDecision = nested["duplicateDecision"]
        }, Json));
    }

    private async Task<MappingSlot> LockMappingSlot(string legal, Guid account, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO main_bp.mapping_slots(tenant_id,legal_entity_key,account_id,scope_identity)
            VALUES({Tenant},{legal},{account},{Guid.NewGuid()}) ON CONFLICT (tenant_id,legal_entity_key,account_id) DO NOTHING
            """, ct);
        return await ReadMappingSlot(legal, account, true, ct) ?? throw BpUseScopeGate.LockUnproven();
    }

    private async Task LockMappingClaims(string legal, Guid account, MappingRevisionRow? prior, Guid? target, CancellationToken ct)
    {
        var ids = new[] { prior?.BpId, target }.OfType<Guid>().Distinct().OrderBy(x => x.ToString("D"), StringComparer.Ordinal);
        foreach (var bp in ids)
        {
            var claim = await ReadReverseClaim(legal, bp, true, ct);
            if (prior?.BpId == bp && (claim is null || claim.Account != account || claim.Revision != prior.Revision))
                throw Rejected("MAPPING_CLAIM_UNPROVEN", 409);
            if (target == bp && claim is not null && claim.Account != account) throw Rejected("MAPPING_TARGET_CLAIMED", 409);
        }
        foreach (var bp in ids)
            if ((await db.Database.SqlQuery<Guid>($"""
                SELECT "Id" AS "Value" FROM public."T_WebBusinessPartner" WHERE "TenantId"={Tenant} AND "Id"={bp} FOR UPDATE
                """).ToListAsync(ct)).SingleOrDefault() != bp) throw Missing();
    }

    private async Task<CommandResponse> CommitMapping(OperationsAccessV1 access, Scope scope, MappingSlot slot,
        MappingArtifact preview, MappingIntent intent, MappingPlan plan, Guid command, byte[] fingerprint, CancellationToken ct)
    {
        var legal = scope.Key;
        var account = intent.Subject.AccountId;
        var action = intent.Subject.Action;
        var at = PgTime(Now);
        var bp = plan.Target;
        var head = plan.TargetHead;
        var changed = action != "CONFIRM";
        if (plan.Create is { } create && plan.Proposal is { } proposal)
        {
            bp = create.Row;
            bp.Id = Guid.NewGuid();
            var relations = proposal.Slots.ToDictionary(x => x.Key, x => x.Value == Guid.Empty ? bp.Id : x.Value, StringComparer.Ordinal);
            ProjectRelations(bp, proposal.RelationCodes, Slots.Select(x => x.Key));
            await InsertRoot(bp, access, ct);
            head = await SaveRevision(bp, null, scope, create.Registration, relations, proposal.Finance, true, at, ct);
        }

        MappingRevisionRow current;
        long sequence = 0;
        if (changed)
        {
            var next = NextMappingNumber(slot.Revision ?? 0);
            var generation = NextMappingNumber(slot.Generation);
            var bpKey = bp is null ? null : Code(bp);
            var semanticHash = MappingSemanticHash(account, legal, next, bpKey);
            var conflicts = plan.Correction?.Conflicts.Select(x => x.ConflictId).ToArray() ?? [];
            current = new MappingRevisionRow { Revision = next, BpId = bp?.Id, BpKey = bpKey,
                SemanticHash = semanticHash, IdentityId = plan.Identity.Id, Recorded = at };
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO main_bp.mapping_revisions(tenant_id,legal_entity_key,account_id,revision,bp_id,business_partner_key,semantic_hash,
                    identity_observation_id,previous_revision,command_id,recorded_at,action,resolved_conflicts)
                VALUES({Tenant},{legal},{account},{next},{current.BpId},{bpKey},{semanticHash},
                       {current.IdentityId},{slot.Revision},{command},{at},{action},{conflicts})
                """, ct);
            await MoveReverseClaim(legal, account, plan.Previous?.BpId, current, ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE main_bp.mapping_slots SET revision={next},observation_sequence=0,generation={generation},current_projection_id=NULL,
                    owner_conflict=CASE WHEN {action}='CORRECT_CURRENT' THEN false ELSE owner_conflict END
                WHERE tenant_id={Tenant} AND legal_entity_key={legal} AND account_id={account}
                """, ct);
            if (plan.Create is null && await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE main_bp.scopes SET revision=revision+1 WHERE tenant_id={Tenant} AND legal_entity_key={legal} AND revision={scope.Revision}
                """, ct) != 1) throw Stale();
        }
        else
        {
            current = plan.Previous ?? throw Stale();
            sequence = NextMappingNumber(slot.Sequence);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE main_bp.mapping_slots SET observation_sequence={sequence}
                WHERE tenant_id={Tenant} AND legal_entity_key={legal} AND account_id={account}
                """, ct);
        }

        await AppendMappingEvent(legal, account, current, sequence, command, at, ct);
        if (changed) await AppendOwnerDecision(legal, account, slot, current, plan.Identity, at, ct);
        var result = await RecordMappingCommand(access, command, fingerprint, "MAPPING_APPLY", legal, account,
            bp is null || head is null ? null : BpReference(bp, head, head.Revision), MappingRef(legal, account, current), null,
            changed ? "APPLIED" : "NO_CHANGE", at, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO main_bp.mapping_consumed_previews(tenant_id,preview_id,command_id) VALUES({Tenant},{preview.Id},{command})
            """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO main_bp.mapping_audit(tenant_id,legal_entity_key,account_id,command_id,action,revision,actor_id,preview_id,recorded_at)
            VALUES({Tenant},{legal},{account},{command},{action},{current.Revision},{access.ActorId},{preview.Id},{at})
            """, ct);
        if (plan.Create is not null && bp is not null && head is not null)
            await RecordBpAudit(access, command, "MAPPING_APPLY", bp, head, "APPLIED", intent.Subject.Reason, [], true, at, ct);
        return result;
    }

    private async Task MoveReverseClaim(string legal, Guid account, Guid? old, MappingRevisionRow current, CancellationToken ct)
    {
        if (old is { } existing && current.BpId == old)
        {
            if (await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE main_bp.mapping_reverse_claims SET revision={current.Revision}
                 WHERE tenant_id={Tenant} AND legal_entity_key={legal} AND account_id={account} AND bp_id={existing}
                """, ct) != 1) throw Rejected("MAPPING_CLAIM_UNPROVEN", 409);
            return;
        }
        if (old is { } removed && await db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM main_bp.mapping_reverse_claims
             WHERE tenant_id={Tenant} AND legal_entity_key={legal} AND account_id={account} AND bp_id={removed}
            """, ct) != 1) throw Rejected("MAPPING_CLAIM_UNPROVEN", 409);
        if (current.BpId is { } target && await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO main_bp.mapping_reverse_claims(tenant_id,legal_entity_key,bp_id,account_id,revision)
            VALUES({Tenant},{legal},{target},{account},{current.Revision}) ON CONFLICT DO NOTHING
            """, ct) != 1) throw Rejected("MAPPING_TARGET_CLAIMED", 409);
    }

    private static string MappingSemanticHash(Guid account, string legal, long revision, string? bpKey)
        => Hash(new { accountId = account, legalEntityKey = legal, mappingRevision = Revision(revision),
            state = bpKey is null ? "UNLINKED" : "LINKED", businessPartnerKey = bpKey });

    private async Task AppendMappingEvent(string legal, Guid account, MappingRevisionRow current, long sequence,
        Guid command, DateTimeOffset timestamp, CancellationToken ct)
    {
        var eventId = Guid.NewGuid();
        var raw = Canonical(new
        {
            eventId, accountId = account, legalEntityKey = legal, mappingRevision = Revision(current.Revision),
            state = current.BpId is null ? "UNLINKED" : "LINKED", businessPartnerKey = current.BpKey, sourceAsOf = Instant(timestamp)
        });
        var hash = RawDigest(raw);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO main_bp.mapping_events(tenant_id,legal_entity_key,account_id,event_id,revision,observation_sequence,command_id,body,body_hash,source_as_of)
            VALUES({Tenant},{legal},{account},{eventId},{current.Revision},{sequence},{command},{raw},{hash},{timestamp})
            """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO main_bp.mapping_deliveries(tenant_id,event_id) VALUES({Tenant},{eventId})
            """, ct);
    }

    private async Task<CommandResponse> RecordMappingCommand(OperationsAccessV1 access, Guid command, byte[] fingerprint,
        string operation, string legal, Guid account, Reference? bp, MappingReference? mapping, ReconcileReceipt? reconcile,
        string outcome, DateTimeOffset occurred, CancellationToken ct)
    {
        var receipt = new Receipt(command, operation, outcome, bp, mapping, reconcile, null, Instant(occurred), "");
        receipt = receipt with { ReceiptHash = ReceiptHash(receipt) };
        var raw = Utf8.GetString(Canonical(receipt));
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO main_bp.commands(tenant_id,command_id,actor_id,legal_entity_key,bp_id,account_id,operation,fingerprint,receipt)
            VALUES({Tenant},{command},{access.ActorId},{legal},{bp?.BpId},{account},{operation},{fingerprint},{raw})
            """, ct);
        return new(receipt, false);
    }
}
