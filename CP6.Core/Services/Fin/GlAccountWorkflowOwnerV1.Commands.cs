using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Fin.GlAccountContentRulesV1;
using static CP6.Core.Services.Fin.GlAccountWorkflowStoreV1;

namespace CP6.Core.Services.Fin;

public sealed partial class GlAccountWorkflowOwnerV1
{
    public Task<object> PublishPreview(Guid context, Guid root, GlPublishPreviewV1 request,
        Func<CancellationToken, Task<Guid>> current, CancellationToken ct)
        => With(context, current, async (frame, token) =>
        {
            RequireAction(frame.Actor, "view");
            RequireAction(frame.Actor, "release");
            RequireDisclosure(frame.Actor);
            RequireScopeRevision(frame, request.ExpectedScopeRevision);
            var subject = await ReadSubject(frame, root, token);
            RequireOpenHead(subject, request.ExpectedHeads);
            if (request.Activate == true && subject.Root.TimelineRevision.HasValue)
                throw Error("INITIAL_ENABLE_NOT_FIRST_RELEASE", 422);
            if (request.Activate != subject.Proposal.Activate) throw Error("ACTIVATION_SUBJECT_MISMATCH", 422);
            if (request.ReplaceFrom != subject.Version.Content.From || request.ReplaceToExclusive != subject.Version.Content.ToExclusive)
                throw Error("REPLACE_INTERVAL_MISMATCH", 422);
            var (plan, _) = await MakeRelease(frame, subject, Guid.NewGuid(), token);
            var created = await store.AddPreview(frame.Id, frame.Actor.UserId, "PUBLISH", plan, token);
            var currentTimeline = await store.Timeline(frame.Id, subject.Root, token);
            var before = currentTimeline?.Segments ?? [];
            var segments = plan.Segments.Select(part => new
            {
                part.From, part.ToExclusive, part.VersionId, releaseId = part.ReleaseId == plan.ReleaseId ? (Guid?)null : part.ReleaseId
            }).ToArray();
            return new
            {
                previewId = created.Id, created.ExpiresAt, plan.ScopeRevision, plan.Heads, plan.DecisionId, plan.VersionId,
                scope = frame.Ledger.Scope(db.CurrentTenantId), rootId = subject.Root.Id,
                beforeTimelineReference = currentTimeline?.Reference(subject.Root.Id), before, segments, plan.Superseded,
                plan.Activate, plan.CanApply, issues = plan.CanApply ? Array.Empty<string>() : ["ROLE_INTERVAL_CONFLICT"],
                isExecutionPermit = false
            };
        }, ct);

    private async Task<GlReceiptV1> Create(Frame frame, GlCommandV1 command, CancellationToken ct)
    {
        RequireEdit(frame, true);
        var id = Guid.NewGuid();
        var code = Code(command.Code);
        var content = Normalize(command.Content, frame.Ledger.BusinessDate, frame.Book);
        await RequireDraftParent(frame, id, content.ParentRootId, ct);
        var proposal = await AddDraft(frame, new(id, code, content), command.Activate!.Value, ct);
        return new(command.OperationRef, "DRAFT_CREATED", frame.Book.ScopeRevision, id, proposal.Id, proposal.VersionId,
            null, null, "SUSPENDED");
    }

    private async Task<GlProposalV1> AddDraft(Frame frame, GlPreparedDraftV1 draft, bool activate, CancellationToken ct)
    {
        await store.InsertRoot(frame.Id, draft.RootId, draft.Code, ct);
        return await OpenProposal(frame, draft.RootId, draft.Content, activate, ct);
    }

    private async Task<GlProposalV1> OpenProposal(Frame frame, Guid root, GlAccountContentV1 content, bool activate,
        CancellationToken ct)
    {
        var version = await store.AppendVersion(frame.Id, root, frame.Actor.UserId, content, ct);
        var proposal = new GlProposalV1(Guid.NewGuid(), 1, "DRAFT", version.Id, [frame.Actor.UserId], activate, null, null);
        await store.WriteProposal(frame.Id, root, null, proposal, ct);
        return proposal;
    }

    private async Task<GlReceiptV1> Change(Frame frame, GlCommandV1 command, Subject subject,
        GlStoredPreviewV1? preview, CancellationToken ct)
    {
        RequireOpenHead(subject, command.ExpectedHeads);
        return command.Kind switch
        {
            "revise" or "successor" => await Edit(frame, command, subject, ct),
            "submit" or "withdraw" or "abandon" => await MoveProposal(frame, command, subject, ct),
            "approve" or "reject" => await Review(frame, command, subject, ct),
            "publish" => await Publish(frame, command, subject, preview!, ct),
            "approve-restore" => await ReviewRestore(frame, command, subject, ct),
            _ => await ChangeControl(frame, command, subject, ct)
        };
    }

    private async Task<GlReceiptV1> Edit(Frame frame, GlCommandV1 command, Subject subject, CancellationToken ct)
    {
        RequireEdit(frame, false);
        var proposed = command.Activate!.Value;
        if (proposed && subject.Root.TimelineRevision.HasValue) throw Error("INITIAL_ENABLE_NOT_FIRST_RELEASE", 422);
        var successor = command.Kind == "successor";
        RequireState(subject.Proposal, successor ? ["RELEASED", "WITHDRAWN", "CLOSED"] : ["DRAFT", "REJECTED"]);
        var body = Normalize(command.Content, frame.Ledger.BusinessDate, frame.Book);
        await RequireDraftParent(frame, subject.Root.Id, body.ParentRootId, ct);
        if (successor)
        {
            var next = await OpenProposal(frame, subject.Root.Id, body, proposed, ct);
            return Result(frame, command, subject with { Proposal = next }, "SUCCESSOR_OPENED");
        }

        var digest = Digest(Encode(body));
        if (subject.Version.Digest == digest && subject.Proposal.Activate == proposed && subject.Proposal.State == "DRAFT")
            return Result(frame, command, subject, "NO_CHANGE");
        var version = await store.AppendVersion(frame.Id, subject.Root.Id, frame.Actor.UserId, body, ct);
        var updated = Bump(subject.Proposal, "DRAFT") with
        {
            VersionId = version.Id, Makers = subject.Proposal.Makers.Append(frame.Actor.UserId).Distinct().ToArray(),
            Activate = proposed, DecisionId = null, SubmissionBasis = null
        };
        await store.WriteProposal(frame.Id, subject.Root.Id, subject.Proposal, updated, ct);
        return Result(frame, command, subject with { Proposal = updated }, "DRAFT_REVISED");
    }

    private async Task<GlReceiptV1> MoveProposal(Frame frame, GlCommandV1 command, Subject subject, CancellationToken ct)
    {
        var proposal = subject.Proposal;
        string state;
        Guid? basis = null;
        switch (command.Kind)
        {
            case "submit":
                RequireState(proposal, "DRAFT", "REJECTED");
                RequireVersionPolicy(frame, subject.Version.Content);
                _ = Normalize(subject.Version.Content, frame.Ledger.BusinessDate, frame.Book);
                await RequireDraftParent(frame, subject.Root.Id, subject.Version.Content.ParentRootId, ct);
                state = "SUBMITTED";
                basis = frame.Book.PolicyRevision;
                break;
            case "withdraw":
                RequireState(proposal, "DRAFT", "SUBMITTED", "APPROVED");
                state = "WITHDRAWN";
                break;
            default:
                RequireState(proposal, "DRAFT", "REJECTED");
                state = "CLOSED";
                break;
        }
        var updated = Bump(proposal, state) with { DecisionId = null, SubmissionBasis = basis };
        await store.WriteProposal(frame.Id, subject.Root.Id, proposal, updated, ct);
        return Result(frame, command, subject with { Proposal = updated }, state);
    }

    private async Task<GlReceiptV1> Review(Frame frame, GlCommandV1 command, Subject subject, CancellationToken ct)
    {
        RequireState(subject.Proposal, "SUBMITTED");
        if (subject.Proposal.Makers.Contains(frame.Actor.UserId)) throw Error("MAKER_CANNOT_REVIEW", 403);
        RequireVersionPolicy(frame, subject.Version.Content);
        if (subject.Proposal.SubmissionBasis != frame.Book.PolicyRevision) throw Error("STALE", 412);
        _ = Normalize(subject.Version.Content, frame.Ledger.BusinessDate, frame.Book);
        var proof = await RequireQualification(frame, frame.Actor.UserId, "review", ct);
        var approve = command.Kind == "approve";
        var body = subject.Version.Content;
        var decision = new GlDecisionV1(Guid.NewGuid(), approve ? "APPROVE" : "REJECT", frame.Actor.UserId,
            proof, frame.Book.PolicyRevision, frame.Book.BasisKey, subject.Heads, subject.Version.Id,
            subject.Version.Digest, body.From, body.ToExclusive, subject.Proposal.Activate, command.Reason, DateTimeOffset.UtcNow);
        await store.AddDecision(frame.Id, subject.Root.Id, decision, ct);
        var updated = Bump(subject.Proposal, approve ? "APPROVED" : "REJECTED") with { DecisionId = decision.Id };
        await store.WriteProposal(frame.Id, subject.Root.Id, subject.Proposal, updated, ct);
        return Result(frame, command, subject with { Proposal = updated }, updated.State);
    }

    private async Task<(GlReleasePlanV1 Plan, GlQualificationV1 Releaser)> MakeRelease(
        Frame frame, Subject subject, Guid release, CancellationToken ct)
    {
        RequireState(subject.Proposal, "APPROVED");
        RequireVersionPolicy(frame, subject.Version.Content);
        var body = Normalize(subject.Version.Content, frame.Ledger.BusinessDate, frame.Book);
        var decisionId = subject.Proposal.DecisionId ?? throw Error("DECISION_UNPROVEN", 422);
        var decision = await store.Decision(frame.Id, subject.Root.Id, decisionId, ct);
        if (decision.Kind != "APPROVE" || decision.VersionId != subject.Version.Id || decision.Digest != subject.Version.Digest ||
            decision.From != body.From || decision.ToExclusive != body.ToExclusive || decision.Activate != subject.Proposal.Activate ||
            decision.Heads with { ProposalRevision = subject.Proposal.Revision } != subject.Heads ||
            decision.Heads.ProposalRevision != subject.Proposal.Revision - 1 ||
            subject.Proposal.SubmissionBasis != frame.Book.PolicyRevision || subject.Proposal.Makers.Contains(decision.Reviewer))
            throw Error("STALE", 412);

        await RequireDecision(frame, decision, ct);
        var releaser = await RequireQualification(frame, frame.Actor.UserId, "release", ct);
        var graph = await Graph(frame, ct);
        var (segments, superseded, roleConflict) = graph.Propose(subject.Root, subject.Version, release, frame.Ledger.BusinessDate);
        return (new(subject.Root.Id, subject.Heads, frame.Book.ScopeRevision, frame.Book.PolicyRevision, frame.Ledger.BusinessDate,
            graph.Identity(frame.Ledger.BusinessDate), decision.Id, subject.Version.Id, subject.Version.Digest,
            release, segments, superseded, subject.Proposal.Activate, !roleConflict), releaser);
    }

    private async Task<GlReceiptV1> Publish(Frame frame, GlCommandV1 command, Subject subject, GlStoredPreviewV1 preview,
        CancellationToken ct)
    {
        var expected = Decode<GlReleasePlanV1>(preview.Snapshot);
        if (expected.RootId != subject.Root.Id || expected.Heads != subject.Heads ||
            expected.ScopeRevision != frame.Book.ScopeRevision || expected.PolicyRevision != frame.Book.PolicyRevision ||
            expected.BusinessDate != frame.Ledger.BusinessDate)
            throw Error("STALE", 412);
        var (actual, qualification) = await MakeRelease(frame, subject, expected.ReleaseId, ct);
        if (!StringComparer.Ordinal.Equals(preview.Snapshot, Encode(actual))) throw Error("STALE", 412);
        if (!actual.CanApply) throw Error("ROLE_INTERVAL_CONFLICT", 422);

        var timelineRevision = Next(subject.Root.TimelineRevision ?? 0);
        var activate = subject.Root.TimelineRevision is null && subject.Proposal.Activate;
        if (activate) RequireControlWrite(frame);
        await store.AppendTimeline(frame.Id, subject.Root.Id, timelineRevision, actual,
            new { frame.Actor.UserId, qualification, actual.GraphIdentity, actual.PolicyRevision, actual.Heads,
                previewId = command.PreviewId, actual.Superseded, timelineRevision, actual.Digest }, ct);
        var root = subject.Root with
        {
            TimelineRevision = timelineRevision,
            ControlState = activate ? "ENABLED" : subject.Root.ControlState,
            ControlRevision = activate ? Next(subject.Root.ControlRevision) : subject.Root.ControlRevision
        };
        await store.WriteRoot(frame.Id, subject.Root, root, ct);
        var released = Bump(subject.Proposal, "RELEASED");
        await store.WriteProposal(frame.Id, subject.Root.Id, subject.Proposal, released, ct);
        return Result(frame, command, subject with { Root = root, Proposal = released }, "RELEASED", release: actual.ReleaseId);
    }

    private async Task<GlReceiptV1> ReviewRestore(Frame frame, GlCommandV1 command, Subject subject, CancellationToken ct)
    {
        var root = subject.Root;
        var timelineRef = await RequireTimelineSubject(frame, root, command.TimelineSubject!, ct);
        var identity = await RestoreIdentity(frame, root, frame.Actor.UserId, ct);
        var proof = await RequireQualification(frame, frame.Actor.UserId, "review", ct);
        var decision = new GlDecisionV1(Guid.NewGuid(), "RESTORE", frame.Actor.UserId, proof,
            frame.Book.PolicyRevision, frame.Book.BasisKey, subject.Heads, null, identity, null, null, null,
            command.Reason, DateTimeOffset.UtcNow, timelineRef);
        await store.AddDecision(frame.Id, root.Id, decision, ct);
        return Result(frame, command, subject, "RESTORE_APPROVED", decision.Id);
    }

    private async Task<string> RestoreIdentity(Frame frame, GlRootV1 root, Guid reviewer, CancellationToken ct)
    {
        if (root.ControlState != "SUSPENDED" || root.TimelineRevision is null) throw Error("RESTORE_REQUIRES_SUSPENDED_TIMELINE");
        var graph = await Graph(frame, ct);
        var proofChecked = new HashSet<Guid>();
        foreach (var piece in graph.RequireRestore(root, frame.Ledger.BusinessDate))
        {
            var body = piece.Content;
            _ = Normalize(body, body.From, frame.Book);
            var start = piece.Segment.From > frame.Ledger.BusinessDate ? piece.Segment.From : frame.Ledger.BusinessDate;
            RequireProfessional(frame.Book, frame.Ledger.Currency, start, piece.Segment.ToExclusive);
            if (!proofChecked.Add(piece.Segment.ReleaseId)) continue;
            var release = await store.ReleaseDecision(frame.Id, root.Id, piece.Segment.ReleaseId, ct);
            var prior = await store.Decision(frame.Id, root.Id, release, ct);
            if (prior.Heads.ProposalId is not Guid proposalId || prior.Heads.ProposalRevision is not long rev ||
                prior.VersionId != piece.Segment.VersionId || prior.Digest != piece.Digest)
                throw Error("RELEASE_UNPROVEN", 503);
            var original = await store.History(frame.Id, root.Id, proposalId, rev, ct);
            if (original.Makers.Contains(reviewer)) throw Error("MAKER_CANNOT_REVIEW", 403);
        }
        return graph.Identity(frame.Ledger.BusinessDate);
    }

    private async Task<GlReceiptV1> ChangeControl(Frame frame, GlCommandV1 command, Subject subject, CancellationToken ct)
    {
        RequireControlWrite(frame);
        var root = subject.Root;
        GlRootV1 next;
        Guid? decisionId = null;
        switch (command.Kind)
        {
            case "suspend":
                if (root.ControlState == "SUSPENDED") return Result(frame, command, subject, "NO_CHANGE");
                next = root with { ControlState = "SUSPENDED" };
                break;
            case "restore":
                var timelineRef = await RequireTimelineSubject(frame, root, command.TimelineSubject!, ct);
                var decision = await store.Decision(frame.Id, root.Id, command.DecisionId!.Value, ct);
                if (decision.Kind != "RESTORE") throw Error("RESTORE_DECISION_REQUIRED", 422);
                if (decision.Heads.ControlRevision != root.ControlRevision || decision.TimelineSubject != timelineRef)
                    throw Error("TIMELINE_SUBJECT_STALE", 412);
                await RequireDecision(frame, decision, ct);
                var identity = await RestoreIdentity(frame, root, decision.Reviewer, ct);
                if (decision.Digest != identity) throw Error("TIMELINE_SUBJECT_STALE", 412);
                decisionId = decision.Id;
                next = root with { ControlState = "ENABLED" };
                break;
            case "retire":
                (await Graph(frame, ct)).RequireNoFutureChildren(root.Id, frame.Ledger.BusinessDate);
                next = root with { ControlState = "RETIRED", RetiredOn = frame.Ledger.BusinessDate };
                break;
            default:
                throw Error("GL_COMMAND_INVALID", 400);
        }
        next = next with { ControlRevision = Next(root.ControlRevision) };
        await store.WriteRoot(frame.Id, root, next, ct);
        return Result(frame, command, subject with { Root = next }, next.ControlState, decisionId);
    }

    private async Task<GlTimelineReferenceV1> RequireTimelineSubject(Frame frame, GlRootV1 root,
        GlTimelineReferenceV1 supplied, CancellationToken ct)
    {
        var timeline = await store.Timeline(frame.Id, root, ct);
        if (timeline is null || timeline.Reference(root.Id) != supplied) throw Error("TIMELINE_SUBJECT_STALE", 412);
        return supplied;
    }

    private async Task RequireDecision(Frame frame, GlDecisionV1 decision, CancellationToken ct)
    {
        if (decision.PolicyRevision != frame.Book.PolicyRevision || decision.BasisKey != frame.Book.BasisKey)
            throw Error("STALE", 412);
        var enabled = await db.Sys_Users.AsNoTracking().AnyAsync(user => user.Id == decision.Reviewer && user.Enable &&
            (user.LockedUntil == null || user.LockedUntil <= DateTime.Now), ct);
        if (!enabled || !await scope.CanUseLedgerAsync(frame.Id, decision.Reviewer, ct))
            throw Error("DECISION_AUTHORITY_STALE", 412);
        var reviewer = await PostgreSqlPermissionReader.ReadAsync(db, decision.Reviewer, ct);
        if (!Can(reviewer, "review") || !CanReadContent(reviewer))
            throw Error("DECISION_AUTHORITY_STALE", 412);
        if (await RequireQualification(frame, decision.Reviewer, "review", ct) != decision.Qualification)
            throw Error("DECISION_AUTHORITY_STALE", 412);
    }

    private async Task<GlQualificationV1> RequireQualification(Frame frame, Guid actor, string action, CancellationToken ct)
    {
        var proof = await store.Qualification(frame.Id, actor, action, ct);
        if (proof is null || proof.Reference == Guid.Empty || proof.Issuer == Guid.Empty || proof.Issuer == actor)
            throw Error("PROFESSIONAL_QUALIFICATION_REQUIRED", 403);
        frame.FinalChecks.Add(async final =>
        {
            if (await store.Qualification(frame.Id, actor, action, final) != proof)
                throw Error("PROFESSIONAL_QUALIFICATION_REQUIRED", 403);
        });
        return proof;
    }

    private async Task RequireDraftParent(Frame frame, Guid root, Guid? parent, CancellationToken ct)
    {
        var seen = new HashSet<Guid> { root };
        while (parent is Guid id)
        {
            if (!seen.Add(id)) throw Error("PARENT_CYCLE", 422);
            if (seen.Count > 32) throw Error("DEPENDENCY_LIMIT", 422);
            var candidate = await ReadSubject(frame, id, ct);
            if (candidate.Root.ControlState == "RETIRED") throw Error("PARENT_UNAVAILABLE", 422);
            // 有已发布计划时，由发布预览按整个有效区间验证父。草稿链用于提前发现环和越界。
            if (candidate.Root.TimelineRevision.HasValue) return;
            if (candidate.Version.Content.PostingClass != "GROUP") throw Error("CHILD_INTERVAL_CONFLICT", 422);
            parent = candidate.Version.Content.ParentRootId;
        }
    }

    private static void RequireOpenHead(Subject subject, GlHeadsV1? expected)
    {
        if (expected != subject.Heads) throw Error("STALE", 412);
        if (subject.Root.ControlState == "RETIRED") throw Error("ROOT_RETIRED");
    }

    private static void RequireEdit(Frame frame, bool code)
    {
        if (!CanEditContent(frame.Actor, code)) throw Error("GL_FIELD_WRITE_DENIED", 403);
    }

    private static void RequireVersionPolicy(Frame frame, GlAccountContentV1 content)
        => RequireProfessional(frame.Book, frame.Ledger.Currency, content.From, content.ToExclusive);

    private static void RequireControlWrite(Frame frame)
    {
        if (FieldAccess(frame.Actor, "ControlState") != 1) throw Error("GL_FIELD_WRITE_DENIED", 403);
    }

    private static void RequireState(GlProposalV1 proposal, params string[] states)
    {
        if (!states.Contains(proposal.State, StringComparer.Ordinal)) throw Error("PROPOSAL_STATE_CONFLICT");
    }

    private static GlProposalV1 Bump(GlProposalV1 proposal, string state) =>
        proposal with { Revision = Next(proposal.Revision), State = state };

    private static GlReceiptV1 Result(Frame frame, GlCommandV1 cmd, Subject subject, string kind,
        Guid? decision = null, Guid? release = null)
        => new(cmd.OperationRef, kind, frame.Book.ScopeRevision, subject.Root.Id, subject.Proposal.Id,
            subject.Proposal.VersionId, decision ?? subject.Proposal.DecisionId, release, subject.Root.ControlState);
}
