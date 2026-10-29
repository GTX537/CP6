using System.Text.Json;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.RmaFormalStoreV1;
using RangeSet = CP6.Core.Services.Wms.RmaRangeSetV1;

namespace CP6.Core.Services.Wms;

public sealed partial class RmaFormalOwnerV1
{
    private sealed record ApprovalProposal(RmaAuthorizedShareInputV1 Input, RmaFormalLineOriginalV1 Line,
        EntitlementSelection Selection, RmaReturnSourceCutV1 Source, RangeSet.Segment[] Ranges,
        RangeSet.Segment[] Occupied, FulfilmentRefV1 Head);
    private static FulfilmentRefV1 ShareReference(RmaReturnShareV1 share) => new(ShareOwner,share.ShareId.ToString(),"1",Digest(share));
    private async Task CheckAuthorizationAuthority(RmaFormalStoreV1 store, OperationsAccessV1 a, Guid application,
        FulfilmentRefV1 reference, RmaPolicyVersionV1 policy, RmaAuthorizationDecisionV1? previous, CancellationToken ct)
    {
        var value = await store.Selection<AuthorizationAuthorityBody>(reference,AuthorizationAuthority,a.ActorId,ct);
        if (value.ApplicationId != application) throw Error("AUTHORIZATION_AUTHORITY_TARGET_MISMATCH",403);
        if (value.PermissionVersion != a.Authorities[0].Source.SourcePermissionVersion || value.PolicyRef != policy.Reference
            || value.DecisionHeadRef != previous?.DecisionRef) throw Error("AUTHORIZATION_CONTEXT_STALE",412);
    }
    public Task<RmaAuthorizationDecisionV1> DecideAuthorizationAsync(Guid application, Guid command, RmaAuthorizationDecideV1 input, CancellationToken ct = default)
    {
        Reference(input.ExpectedApplicationHeadRef); Reference(input.AuthorityRef);
        if (input.Outcome is not ("APPROVED" or "REJECTED") || input.ExpectedEntitlementHeadRefs == null || input.ExpectedEntitlementHeadRefs.Length > 100
            || input.AuthorizedShares == null || input.AuthorizedShares.Length > 100 || input.ReasonCodes == null || input.ReasonCodes.Length is < 1 or > 50
            || input.ReasonCodes.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 200) || input.ReasonCodes.Distinct().Count() != input.ReasonCodes.Length
            || input.ValidFrom == default || input.ValidTo <= input.ValidFrom
            || (input.Outcome == "APPROVED" ? input.AuthorizedShares.Length == 0 : input.AuthorizedShares.Length != 0 || input.ExpectedEntitlementHeadRefs.Length != 0))
            throw Error("AUTHORIZATION_INPUT_INVALID",400);
        if (input.ExceptionApprovalRef != null) throw Error("RETURN_EXCEPTION_SOURCE_NOT_CONFIGURED",422);
        foreach (var head in input.ExpectedEntitlementHeadRefs) Reference(head);
        foreach (var share in input.AuthorizedShares)
        {
            if (share == null || share.RequestedLineId == Guid.Empty || share.Ranges == null) throw Error("AUTHORIZATION_INPUT_INVALID",400);
            Reference(share.EntitlementSnapshotRef);
        }
        if (input.AuthorizedShares.Select(x => x.RequestedLineId).Distinct().Count() != input.AuthorizedShares.Length) throw Error("AUTHORIZATION_INPUT_INVALID",400);
        var c = input with { ExpectedEntitlementHeadRefs = [..input.ExpectedEntitlementHeadRefs], ReasonCodes = [..input.ReasonCodes],
            AuthorizedShares = input.AuthorizedShares.Select(x => x with { Ranges = [..x.Ranges] }).ToArray() };
        var parsed = c.AuthorizedShares.ToDictionary(x => x.RequestedLineId,x => (Ranges: RangeSet.Input(x.Ranges),Quantity: Quantity(x.Quantity)));
        return AuthorizationCommand(application,command,"authorization-decide","DECIDE",c,async (store,a,t) =>
        {
            var app = await Current(store,a,application,t); WriteFields(a,false); var view = View(app);
            if (c.ValidTo <= DateTimeOffset.UtcNow) throw Error("AUTHORIZATION_VALIDITY_ENDED",422);
            if (app.State != "SUBMITTED") throw Error("RMA_APPLICATION_NOT_SUBMITTED",409);
            if (view.ApplicationRef != c.ExpectedApplicationHeadRef) throw Error("CAS_CONFLICT",412);
            var previous = await AuthorizationCurrent(application,t); var policy = await AuthorizationPolicy(app.Scope,t);
            await CheckAuthorizationAuthority(store,a,application,c.AuthorityRef,policy,previous,t);
            if (previous?.Outcome == "APPROVED") throw Error("AUTHORIZATION_ALREADY_APPROVED",409);
            if (policy.Policy.RequireIndependentApprover && app.ActorId == a.ActorId) throw Error("INDEPENDENT_APPROVER_REQUIRED",403);
            if (c.Outcome == "APPROVED" && (!policy.Policy.Enabled || !policy.Policy.ReasonOptions.Any(x => x.Code == app.ReasonCode)
                || !policy.Policy.AllowedOutcomes.Contains(app.Outcome))) throw Error("RETURN_POLICY_DOES_NOT_ALLOW_REQUEST",422);
            var proposals = new List<ApprovalProposal>();
            foreach (var share in c.AuthorizedShares)
            {
                var line = app.Lines.SingleOrDefault(x => x.LineId == share.RequestedLineId) ?? throw Error("REQUESTED_LINE_NOT_FOUND",404);
                var selection = await store.Selection<EntitlementSelection>(share.EntitlementSnapshotRef,EntitlementOwner,a.ActorId,t);
                if (selection.ApplicationId != application || selection.LineId != line.LineId || selection.ApplicationRef != view.ApplicationRef)
                    throw Error("ENTITLEMENT_APPLICATION_MISMATCH",409);
                var current = await EntitlementCurrent(line,policy,t);
                if (selection.HeadRef != current.Head || selection.PolicyRef != policy.Reference
                    || selection.Source.Coordinate.SourceCutRef != current.Source.Coordinate.SourceCutRef) throw Error("ENTITLEMENT_HEAD_STALE",412);
                if (current.Source.DeliveredAt > DateTimeOffset.UtcNow || policy.Policy.ReturnWindowDaysAfterDelivery is { } days
                    && app.RequestedAt > current.Source.DeliveredAt.AddDays(days)) throw Error("RETURN_POLICY_WINDOW_EXCEEDED",422);
                var ranges = parsed[share.RequestedLineId].Ranges;
                if (share.Quantity.Uom != line.Original.BaseQty.Uom || parsed[share.RequestedLineId].Quantity != RangeSet.Measure(ranges)) throw Error("RANGE_MEASURE_MISMATCH",422);
                if (!RangeSet.Subset(ranges,RangeSet.Read(line.Ranges)) || !RangeSet.Subset(ranges,RangeSet.Read(current.Source.Coordinate.EligibleRanges)))
                    throw Error("RETURN_RANGE_OUTSIDE_ENTITLEMENT",422);
                proposals.Add(new(share,line,selection,current.Source,ranges,current.Occupied,current.Head));
            }
            if (!c.ExpectedEntitlementHeadRefs.ToHashSet().SetEquals(proposals.Select(x => x.Head))) throw Error("ENTITLEMENT_HEAD_STALE",412);
            foreach (var group in proposals.GroupBy(x => CoordinateKey(x.Source.Coordinate)))
            {
                var ranges = group.SelectMany(x => x.Ranges).OrderBy(x => x.From).ToArray();
                if (ranges.Zip(ranges.Skip(1)).Any(x => x.First.To > x.Second.From) || RangeSet.Intersects(ranges,group.First().Occupied))
                    throw Error("RETURN_RANGE_ALREADY_CLAIMED",409);
            }
            var at = DateTimeOffset.UtcNow; var actor = await Actor(store,a,t); var decisionId = previous?.DecisionId ?? Guid.NewGuid(); var version = checked((previous?.DecisionVersion ?? 0) + 1);
            var decisionRef = new FulfilmentRefV1(AuthorizationOwner,decisionId.ToString(),version.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Digest(new { Application = view.ApplicationRef, Input = c, at, actor, Previous = previous?.DecisionRef }));
            var shares = new List<RmaReturnShareV1>();
            foreach (var p in proposals)
            {
                var requested = view.RequestedLines.Single(x => x.LineId == p.Line.LineId);
                var lineRef = new FulfilmentRefV1("Main.Wms.RmaRequestedLine.v1",p.Line.LineId.ToString(),p.Line.LineVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),requested.LineDigest);
                var claimId = Guid.NewGuid(); var ranges = RangeSet.Wire(p.Ranges); var quantity = new FulfilmentQtyV1(Text(RangeSet.Measure(p.Ranges)),p.Source.Coordinate.BaseUom);
                var digest = Digest(new { claimId, p.Source.Coordinate, ApplicationId = application, lineRef, decisionRef, ranges, quantity, c.ValidFrom, c.ValidTo });
                var claim = new RmaRangeClaimV1(claimId,p.Source.Coordinate,application,lineRef,decisionRef,ranges,quantity,"ACTIVE_RESERVED",digest,null,null,decisionRef,c.ValidFrom,c.ValidTo);
                var key = CoordinateKey(p.Source.Coordinate);
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO cp6_rma_formal_v1.return_claims(tenant_id,claim_id,coordinate_key,application_id,decision_id,body)
                    VALUES({db.CurrentTenantId},{claimId},{key},{application},{decisionId},{Encode(claim)})
                    """,t);
                for (var i = 0; i < p.Ranges.Length; i++)
                    await db.Database.ExecuteSqlInterpolatedAsync($"""
                        INSERT INTO cp6_rma_formal_v1.return_claim_segments(tenant_id,claim_id,ordinal,coordinate_key,range_from,range_to,state)
                        VALUES({db.CurrentTenantId},{claimId},{i},{key},{p.Ranges[i].From},{p.Ranges[i].To},{"ACTIVE_RESERVED"})
                        """,t);
                var share = new RmaReturnShareV1(Guid.NewGuid(),application,decisionRef,lineRef,p.Input.EntitlementSnapshotRef,new(ClaimOwner,claimId.ToString(),"1",digest),
                    p.Source.Original,ranges,quantity,"AUTHORIZED",null,null);
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO cp6_rma_formal_v1.return_shares(tenant_id,share_id,application_id,decision_id,claim_id,body)
                    VALUES({db.CurrentTenantId},{share.ShareId},{application},{decisionId},{claimId},{Encode(share)})
                    """,t);
                shares.Add(share);
            }
            foreach (var p in proposals.DistinctBy(x => CoordinateKey(x.Source.Coordinate)).OrderBy(x => CoordinateKey(x.Source.Coordinate)))
                await EntitlementHead(CoordinateKey(p.Source.Coordinate),Digest(new { p.Source.Coordinate.SourceCutRef,p.Source.Coordinate.SourceVersionRef,policy.Reference }),true,t);
            var result = new RmaAuthorizationDecisionV1(decisionId,version,decisionRef,view.ApplicationRef,c.Outcome,shares.ToArray(),c.ReasonCodes,
                c.AuthorityRef,actor,at,c.ValidFrom,c.ValidTo,null,previous?.DecisionRef);
            await AppendAuthorization(application,result,t); return result;
        },ct);
    }
    public Task<RmaAuthorizationDecisionV1> RevokeAuthorizationAsync(Guid application, Guid command, RmaAuthorizationRevokeV1 input, CancellationToken ct = default)
    {
        Reference(input.ExpectedDecisionHeadRef); Reference(input.AuthorityRef);
        if (string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length > 500 || input.AffectedReturnShareRefs == null || input.AffectedReturnShareRefs.Length > 100)
            throw Error("AUTHORIZATION_INPUT_INVALID",400);
        foreach (var reference in input.AffectedReturnShareRefs) Reference(reference);
        var c = input with { AffectedReturnShareRefs = [..input.AffectedReturnShareRefs] };
        return AuthorizationCommand(application,command,"authorization-revoke","REVOKE",c,async (store,a,t) =>
        {
            var app = await Current(store,a,application,t); WriteFields(a,false);
            var previous = await AuthorizationCurrent(application,t) ?? throw Error("AUTHORIZATION_NOT_FOUND",404);
            if (previous.DecisionRef != c.ExpectedDecisionHeadRef) throw Error("CAS_CONFLICT",412);
            if (previous.Outcome != "APPROVED") throw Error("AUTHORIZATION_NOT_APPROVED",409);
            var policy = await AuthorizationPolicy(app.Scope,t); await CheckAuthorizationAuthority(store,a,application,c.AuthorityRef,policy,previous,t);
            if (policy.Policy.RequireIndependentApprover && app.ActorId == a.ActorId) throw Error("INDEPENDENT_APPROVER_REQUIRED",403);
            if (!c.AffectedReturnShareRefs.ToHashSet().SetEquals(previous.AuthorizedShares.Select(ShareReference))) throw Error("AUTHORIZATION_SHARE_SET_MISMATCH",409);
            var keys = new HashSet<string>();
            foreach (var share in previous.AuthorizedShares)
            {
                var claim = Guid.Parse(share.RangeClaimRef.Id); var segments = await ClaimSegments().Where(x => x.ClaimId == claim).ToArrayAsync(t);
                // Receipt/UNKNOWN segments stay occupied. The same RMA write fence serializes effect starts and revocation.
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE cp6_rma_formal_v1.return_claim_segments SET state={"RELEASED_BY_SUCCESSOR"}
                    WHERE tenant_id={db.CurrentTenantId} AND claim_id={claim} AND state={"ACTIVE_RESERVED"}
                    """,t);
                foreach (var key in segments.Select(x => x.Key)) keys.Add(key);
            }
            foreach (var key in keys.Order())
            {
                var row = await db.Database.SqlQuery<EntitlementHeadRow>($"SELECT revision AS \"Revision\",source_signature AS \"Signature\",reference AS \"Reference\" FROM cp6_rma_formal_v1.entitlement_heads WHERE tenant_id={db.CurrentTenantId} AND coordinate_key={key}").SingleAsync(t);
                await EntitlementHead(key,row.Signature,true,t);
            }
            var at = DateTimeOffset.UtcNow; var actor = await Actor(store,a,t); var version = checked(previous.DecisionVersion + 1);
            var reference = new FulfilmentRefV1(AuthorizationOwner,previous.DecisionId.ToString(),version.ToString(System.Globalization.CultureInfo.InvariantCulture),Digest(new { previous.DecisionRef, Input = c, at, actor }));
            var outcome = previous.ValidTo <= at ? "EXPIRED" : "REVOKED";
            var result = new RmaAuthorizationDecisionV1(previous.DecisionId,version,reference,View(app).ApplicationRef,outcome,[],["AUTHORIZATION_"+outcome],c.AuthorityRef,actor,at,at,null,null,previous.DecisionRef);
            await AppendAuthorization(application,result,t); return result;
        },ct);
    }
    private async Task AppendAuthorization(Guid application, RmaAuthorizationDecisionV1 value, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_rma_formal_v1.authorization_versions(tenant_id,decision_id,version,application_id,body)
            VALUES({db.CurrentTenantId},{value.DecisionId},{value.DecisionVersion},{application},{Encode(value)})
            """,ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_rma_formal_v1.authorization_heads(tenant_id,application_id,decision_id,version)
            VALUES({db.CurrentTenantId},{application},{value.DecisionId},{value.DecisionVersion})
            ON CONFLICT(tenant_id,application_id) DO UPDATE SET version=EXCLUDED.version
            """,ct);
        var eventId = Guid.NewGuid(); var body = Encode(new { schema = "WMS-RMA.AUTHORIZATION/1", eventId, tenantId = db.CurrentTenantId, applicationId = application, decision = value });
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_rma_formal_v1.authorization_outbox(tenant_id,event_id,decision_id,version,body)
            VALUES({db.CurrentTenantId},{eventId},{value.DecisionId},{value.DecisionVersion},{body})
            """,ct);
    }
    private Task<RmaAuthorizationDecisionV1> AuthorizationCommand<T>(Guid application, Guid command, string action, string operation, T input,
        Func<RmaFormalStoreV1,OperationsAccessV1,CancellationToken,Task<RmaAuthorizationDecisionV1>> apply,CancellationToken ct)
    {
        if (application == Guid.Empty || command == Guid.Empty) throw Error("VALIDATION_FAILED",400);
        var bytes = Encode(new { application, operation, input }); var journal = new MainOwnerOperationJournalV1(db); MainOwnerOperationJournalV1.Original? original = null;
        return Run(action,true,async (store,a,t) =>
        {
            if (original != null) return await DiscloseAuthorization(store,a,application,original,bytes,t);
            var result = await apply(store,a,t); var app = await store.Read(application,int.Parse(result.ApplicationRef.Version,System.Globalization.CultureInfo.InvariantCulture),t);
            var binding = new RmaCommandBindingV1(command,app.Scope,result.DecidedBy,"POST","/api/wms/rma/v1/applications/{applicationId}/authorization/"+operation.ToLowerInvariant(),
                "wms.rma."+action.Replace('-','.'),operation,result.DecisionRef,[result.ApplicationRef,result.AuthorityRef],MainOwnerOperationJournalV1.Digest(bytes),["AUTHORIZATION:"+result.DecisionId+":"+result.DecisionVersion],
                result.PreviousDecisionRef == null ? [] : [result.PreviousDecisionRef],result.DecidedAt);
            var bindingRef = await store.Fact("Main.Wms.RmaCommandBinding.v1",a.ActorId,binding,false,t);
            var work = new RmaApplicationWorkV1(command,application,operation,1,0,0,"SUCCEEDED",bindingRef,[],[],result.DecidedAt,result.DecidedAt);
            var receipt = new RmaApplicationReceiptV1(Guid.NewGuid(),bindingRef,"SUCCEEDED",[result.DecisionRef,..result.AuthorizedShares.Select(ShareReference)],binding.EffectKeys,null,result.DecidedAt,false);
            await journal.Store(AuthorizationJournal,"WMS-RMA.AUTHORIZATION/1",command,a.ActorId,bytes,result,[application],new { binding,work,receipt },t);
            return result;
        },ct,async t => { original = await journal.Read(AuthorizationJournal,command,t); return original == null ? action : "query"; });
    }
    private async Task<RmaAuthorizationDecisionV1> DiscloseAuthorization(RmaFormalStoreV1 store, OperationsAccessV1 a, Guid application,
        MainOwnerOperationJournalV1.Original original, byte[]? input, CancellationToken ct)
    {
        if (original.Actor != a.ActorId || !original.Objects.Contains(application)) throw Error("OBJECT_FORBIDDEN",403);
        if (input != null && !MainOwnerOperationJournalV1.Matches(original,input)) throw Error("COMMAND_BINDING_CONFLICT",409);
        await Current(store,a,application,ct); return JsonSerializer.Deserialize<RmaAuthorizationDecisionV1>(original.Result)!;
    }
    public Task<RmaAuthorizationCommandResultV1> QueryAuthorizationCommandAsync(RmaAuthorizationQueryV1 query, CancellationToken ct = default)
    {
        if (query.ApplicationId == Guid.Empty || query.CommandId == Guid.Empty) throw Error("VALIDATION_FAILED",400);
        return Run("query",false,async (store,a,t) =>
        {
            var original = await new MainOwnerOperationJournalV1(db).Read(AuthorizationJournal,query.CommandId,t);
            if (original == null) { await Current(store,a,query.ApplicationId,t); return new RmaAuthorizationCommandResultV1("NOT_OBSERVED",null); }
            return new RmaAuthorizationCommandResultV1("FOUND",await DiscloseAuthorization(store,a,query.ApplicationId,original,null,t));
        },ct);
    }
}
