using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Oa;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
namespace CP6.Core.Services.Plm;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record IterationReviewIssueTargetV1(string Kind,EngineeringSourceRefV1? Candidate,string? Path);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record IterationReviewIssueCommandV1(Guid OperationId,Guid SubmissionId,Guid IssueId,string Action,
    [property:JsonNumberHandling(JsonNumberHandling.AllowReadingFromString|JsonNumberHandling.WriteAsString)] long? ExpectedRevision,string Text,IterationReviewIssueTargetV1? Target,Guid? ResponsibleId,DateOnly? DueDate,
    EngineeringSourceRefV1[]? Evidence,string Reason);
public sealed record IterationReviewIssueResponseV1(Guid Id,Guid ActorId,DateTimeOffset At,string Text,EngineeringSourceRefV1[] Evidence,bool CorrectiveNote);
public sealed record IterationReviewIssueV1(Guid Id,Guid IterationId,Guid SubmissionId,
    [property:JsonNumberHandling(JsonNumberHandling.AllowReadingFromString|JsonNumberHandling.WriteAsString)] long Revision,IterationReviewIssueTargetV1 Target,
    string Text,Guid AuthorId,Guid ResponsibleId,DateOnly DueDate,string Severity,string State,
    IterationReviewIssueResponseV1[] Responses,Guid? VerifiedBy,DateTimeOffset? VerifiedAt,string LastReason);
public sealed record IterationReviewIssueContextV1(Guid IterationId,string Etag,IterationReviewIssueV1[] Issues);
public sealed record IterationReviewIssueResultV1(Guid OperationId,Guid IterationId,IterationReviewIssueV1 Issue);
public sealed record IterationReviewIssueOperationV1(Guid OperationId,string State,IterationReviewIssueResultV1? Result=null);

public sealed partial class IterationReviewOwnerV1
{
    private const string IssueJournal="Main.Plm.IterationReviewIssue.operations.v1",IssueContract="Main.Plm.IterationReviewIssue.commands.v1";
    private static string IssueEtag(Context c,IterationReviewIssueV1[] issues)=>"\"ii1-"+ApprovalCanonicalV2.Hash(ApprovalCanonicalV2.Bytes(new{c.Etag,issues}))+"\"";
    private async Task<IterationReviewIssueV1[]> ReadIssues(Context c,CancellationToken ct)
    {
        var items=await new IterationReviewControlStoreV1(db,Environment).Issues(c.Iteration.Reference.Id,ct);
        foreach(var i in items)
        {
            var row=c.Submissions.SingleOrDefault(x=>x.Id==i.SubmissionId)??throw Error("ITERATION_REVIEW_ISSUE_UNAVAILABLE",503);
            await DisclosePackage(c.Access,row,false,ct);
            foreach(var response in i.Responses)await iterations.ReviewEvidence(c.Access,c.Iteration,response.Evidence,false,ct);
        }
        return items;
    }
    public Task<IterationReviewIssueContextV1> IssuesAsync(Guid id,CancellationToken ct=default)
        =>Authority.ExecuteAsync(OperationsOwnerV1.EngineeringIteration,"query",false,async(a,t)=>{
            var c=await Prepare(a,id,false,t);var issues=await ReadIssues(c,t);return new IterationReviewIssueContextV1(id,IssueEtag(c,issues),issues);
        },ct);
    private static void Target(IterationReviewPackageV1 package,IterationReviewIssueTargetV1 target)
    {
        if(target.Kind=="PROCESS" && target.Candidate==null && target.Path==null)return;
        if(target.Kind!="CANDIDATE" || target.Candidate==null || target.Path==null || target.Path.Length>1000
            || !(target.Path=="" || target.Path.StartsWith("/definition/",StringComparison.Ordinal)))throw Error("ITERATION_REVIEW_ISSUE_TARGET_INVALID",422);
        var candidate=package.Candidates.SingleOrDefault(x=>x.Reference==target.Candidate)??throw Error("ITERATION_REVIEW_ISSUE_TARGET_INVALID",422);
        using var d=JsonDocument.Parse(candidate.OriginalJson);var value=d.RootElement;
        foreach(var encoded in target.Path.Split('/').Skip(1))
        {
            var key=encoded.Replace("~1","/",StringComparison.Ordinal).Replace("~0","~",StringComparison.Ordinal);
            if(value.ValueKind==JsonValueKind.Object && value.TryGetProperty(key,out var child))value=child;
            else if(value.ValueKind==JsonValueKind.Array && int.TryParse(key,out var index) && index>=0 && index<value.GetArrayLength()
                && key==index.ToString(System.Globalization.CultureInfo.InvariantCulture))value=value[index];
            else throw Error("ITERATION_REVIEW_ISSUE_TARGET_INVALID",422);
        }
    }
    public Task<IterationReviewIssueResultV1> IssueAsync(Guid id,string? etag,IterationReviewIssueCommandV1 input,CancellationToken ct=default)
    {
        if(input==null || input.OperationId==Guid.Empty || input.IssueId==Guid.Empty || input.SubmissionId==Guid.Empty
            || input.Action is not("CREATE" or "RESPOND" or "VERIFY" or "DOWNGRADE" or "NOTE"))throw Error("ITERATION_REVIEW_ISSUE_COMMAND_INVALID",400);
        Text(input.Text);Text(input.Reason,true,1000);var command=OperationsOwnerV1.Freeze(input);
        var action=command.Action=="VERIFY"?"review-issue-verify":command.Action=="DOWNGRADE"?"review-issue-degrade":"review-issue";
        var bytes=MainOwnerOperationJournalV1.Encode(new{contract=IssueContract,environment=Environment,id,etag,command});
        var journal=new MainOwnerOperationJournalV1(db);MainOwnerOperationJournalV1.Original? original=null;
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringIteration,action,true,async(a,t)=>{
            var c=await Prepare(a,id,original==null,t);var issues=await ReadIssues(c,t);
            if(original!=null)return await DiscloseIssue(c,command.OperationId,original,bytes,t);
            if(string.IsNullOrEmpty(etag))throw Error("IF_MATCH_REQUIRED",428);
            if(etag!=IssueEtag(c,issues))throw Error("ITERATION_REVIEW_ISSUE_CONTEXT_CHANGED",412);
            a.RequireEngineeringIterationFields(true);
            var row=c.Submissions.SingleOrDefault(x=>x.Id==command.SubmissionId)??throw Error("RESOURCE_NOT_FOUND",404);
            await DisclosePackage(a,row,false,t);var package=IterationReviewStoreV1.ReadPackage(row);
            var control=new IterationReviewControlStoreV1(db,Environment);var head=await control.Read(id,t);
            var issue=issues.SingleOrDefault(x=>x.Id==command.IssueId);
            var activeRow=c.Submissions.SingleOrDefault(x=>x.Id==head?.Submission);
            var activePackage=activeRow==null?null:IterationReviewStoreV1.ReadPackage(activeRow);
            var carried=issue!=null && activePackage?.Issues.Any(x=>x.GetProperty("id").GetGuid()==issue.Id
                && x.GetProperty("submissionId").GetGuid()==issue.SubmissionId)==true;
            var active=head?.State=="IN_REVIEW" && c.Iteration.Body.Iteration!.CurrentReview?.SubmissionId==head.Submission
                && (head.Submission==row.Id || command.Action!="CREATE" && carried);
            if(!active && command.Action!="NOTE")throw Error("ITERATION_REVIEW_ISSUES_READ_ONLY",409);
            if(command.Action=="CREATE")
            {
                if(issue!=null || command.ExpectedRevision!=null || command.Target==null || command.ResponsibleId==null || command.DueDate==null || command.Evidence!=null)
                    throw Error("ITERATION_REVIEW_ISSUE_COMMAND_INVALID",400);
                Target(package,command.Target);
                if(!(await iterations.ReviewMembers(a,c.Iteration,t)).Contains(command.ResponsibleId.Value))throw Error("ITERATION_REVIEW_ISSUE_RESPONSIBLE_INVALID",422);
                await iterations.ReviewStaff([command.ResponsibleId.Value],t);
                issue=new(command.IssueId,id,row.Id,1,command.Target,command.Text,a.ActorId,command.ResponsibleId.Value,command.DueDate.Value,
                    "BLOCKING","OPEN",[],null,null,command.Reason);
            }
            else
            {
                if(issue==null || issue.SubmissionId!=row.Id)throw Error("RESOURCE_NOT_FOUND",404);
                if(command.ExpectedRevision!=issue.Revision)throw Error("ITERATION_REVIEW_ISSUE_CONTEXT_CHANGED",412);
                if(command.Target!=null || command.ResponsibleId!=null || command.DueDate!=null)throw Error("ITERATION_REVIEW_ISSUE_COMMAND_INVALID",400);
                if(command.Action is "RESPOND" or "NOTE")
                {
                    if(command.Evidence==null || command.Evidence.Length>100)throw Error("ITERATION_REVIEW_ISSUE_COMMAND_INVALID",400);
                    if(command.Action=="RESPOND" && (a.ActorId!=issue.ResponsibleId || issue.State=="VERIFIED_CLOSED"))throw Error("ITERATION_REVIEW_ISSUE_RESPONSE_FORBIDDEN",403);
                    await iterations.ReviewEvidence(a,c.Iteration,command.Evidence,true,t);
                    var response=new IterationReviewIssueResponseV1(command.OperationId,a.ActorId,Now(),command.Text,command.Evidence,command.Action=="NOTE");
                    issue=issue with{Responses=[..issue.Responses,response],State=command.Action=="NOTE"?issue.State:"RESPONDED"};
                    a.AtCommit(token=>a.EngineeringFinalObservation(async(observation,finalToken)=>{
                        await iterations.ReviewEvidence(observation,c.Iteration,command.Evidence,true,finalToken);return true;
                    },token));
                }
                else
                {
                    if(command.Evidence!=null || issue.State=="VERIFIED_CLOSED")throw Error("ITERATION_REVIEW_ISSUE_COMMAND_INVALID",400);
                    if(command.Action=="VERIFY")
                    {
                        if(issue.Responses.All(x=>x.CorrectiveNote) || a.ActorId==issue.AuthorId || a.ActorId==issue.ResponsibleId
                            || issue.Responses.Any(x=>!x.CorrectiveNote && x.ActorId==a.ActorId) || !activePackage!.Reviewers.Contains(a.ActorId))throw Error("ITERATION_REVIEW_ISSUE_INDEPENDENT_VERIFIER_REQUIRED",403);
                        var policy=await Policy(c,activePackage.Policy,t);
                        if(!policy.Fact.Body.Requirements.Any(x=>a.Authorities.Any(y=>y.Actor.RoleIds.Contains(x.RoleId))))throw Error("ITERATION_REVIEW_PROFESSIONAL_ROLE_REQUIRED",403);
                        foreach(var response in issue.Responses.Where(x=>!x.CorrectiveNote))await iterations.ReviewEvidence(a,c.Iteration,response.Evidence,true,t);
                        var verifiedIssue=issue;
                        a.AtCommit(token=>a.EngineeringFinalObservation(async(observation,finalToken)=>{
                            await Policy(c with{Access=observation,Policies=await c.Store.Policies(id,finalToken)},activePackage.Policy,finalToken);
                            foreach(var response in verifiedIssue.Responses.Where(x=>!x.CorrectiveNote))
                                await iterations.ReviewEvidence(observation,c.Iteration,response.Evidence,true,finalToken);
                            return true;
                        },token));
                        issue=issue with{State="VERIFIED_CLOSED",VerifiedBy=a.ActorId,VerifiedAt=Now()};
                    }
                    else issue=issue with{Severity="NON_BLOCKING"};
                }
                issue=issue with{Revision=checked(issue.Revision+1),LastReason=command.Reason};
            }
            await control.Append(issue,command.OperationId,a.ActorId,new{command,authorities=Witnesses(a),head?.RowVersion},t);
            var result=new IterationReviewIssueResultV1(command.OperationId,id,issue);
            await journal.Store(IssueJournal,IssueContract,command.OperationId,a.ActorId,bytes,result,[id],new{command,issue.Id,issue.Revision,authorities=Witnesses(a)},t);
            return result;
        },ct,actionAtFence:async t=>{original=await journal.Read(IssueJournal,command.OperationId,t);return original==null?action:"query";});
    }
    private async Task<IterationReviewIssueResultV1> DiscloseIssue(Context c,Guid operation,MainOwnerOperationJournalV1.Original original,byte[]? bytes,CancellationToken ct)
    {
        if(original.Actor!=c.Access.ActorId || original.Contract!=IssueContract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
        if(original.State=="CLOSED")throw Error("ORIGINAL_OPERATION_CLOSED",409);
        if(bytes!=null && !MainOwnerOperationJournalV1.Matches(original,bytes))throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT",409);
        var r=JsonSerializer.Deserialize<IterationReviewIssueResultV1>(original.Result);
        if(original.State!="COMMITTED" || r==null || r.OperationId!=operation || r.IterationId!=c.Iteration.Reference.Id
            || !original.Objects.SequenceEqual(new[]{r.IterationId}))throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
        await ReadIssues(c,ct);return r;
    }
    public Task<IterationReviewIssueOperationV1> IssueOperationAsync(Guid operation,bool close,CancellationToken ct=default)
        =>Authority.ExecuteAsync(OperationsOwnerV1.EngineeringIteration,"query",close,async(a,t)=>{
            if(operation==Guid.Empty)throw Error("ORIGINAL_OPERATION_REQUIRED",400);
            await scope.Root(t);a.RequireEngineeringIterationFields(false);var journal=new MainOwnerOperationJournalV1(db);var original=await journal.Read(IssueJournal,operation,t);
            if(original!=null)
            {
                if(original.Actor!=a.ActorId || original.Contract!=IssueContract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
                if(original.State=="CLOSED")return new IterationReviewIssueOperationV1(operation,"CLOSED");
                if(original.Objects.Length!=1)throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
                return new IterationReviewIssueOperationV1(operation,"COMMITTED",await DiscloseIssue(await Prepare(a,original.Objects[0],false,t),operation,original,null,t));
            }
            if(!close)return new IterationReviewIssueOperationV1(operation,"UNKNOWN");
            var result=new IterationReviewIssueOperationV1(operation,"CLOSED");
            await journal.Store(IssueJournal,IssueContract,operation,a.ActorId,null,result,[],new{action="close-uncommitted-iteration-issue",a.ActorId},t);return result;
        },ct,readOnlyAuthorityFence:!close);
}
