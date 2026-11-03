using System.Text.Json;
using CP6.Core.Services.Oa;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    // A fixture captures all client-observed versions when intent is created. The send helper
    // never reads fresh server state, so existing concurrency/expiry tests remain stale-intent tests.
    private sealed record NativeVoteInput(Guid CommandId,string Decision,string Comment,string TaskRowVersion,string InstanceRowVersion,string FormDataVersion);
    private static NativeVoteInput NativeVote(PersonalFormTaskContextV2 c,string comment,string decision="approve")
        =>new(Guid.NewGuid(),decision,comment,c.Task.TaskRowVersion,c.Task.InstanceRowVersion,c.FormDataVersion);
    private static async Task<PersonalFormTaskResultV2> DecideNative(PersonalFormOwnerV2 owner,Guid task,NativeVoteInput input,Guid? behalf=null)
    {
        var command=PersonalCommand(new SfsTaskDecisionInputV2("SFS",task,input.Decision.ToUpperInvariant(),PersonalData("{}"),input.TaskRowVersion,input.InstanceRowVersion,input.FormDataVersion,input.Comment),input.CommandId.ToString("D"),input.TaskRowVersion);
        return NativeVoteResult(await owner.DecideSfsTaskAsync(task,command,behalf),task);
    }
    private static PersonalFormTaskResultV2 NativeVoteResult(PersonalFormResultV2 result,Guid task)
    {
        var value=result.Result?.Deserialize<SfsTaskDecisionResultV2>(PersonalFormOwnerV2.Json);
        return new(Guid.Parse(result.CommandId),result.Outcome=="CLOSED"?"CLOSED":"COMPLETED",value?.VoteId,value?.Task,"/api/oa/v2/personal-forms/commands/SFS_TASK_DECISION/"+task+"/"+result.CommandId);
    }
    private static async Task<PersonalFormTaskResultV2?> NativeOriginal(PersonalFormOwnerV2 owner,Guid task,Guid command)
    {
        var result=await owner.OriginalAsync("SFS_TASK_DECISION",task.ToString("D"),command.ToString("D"));
        return result.Outcome=="NOT_OBSERVED"?null:NativeVoteResult(result,task);
    }
}
