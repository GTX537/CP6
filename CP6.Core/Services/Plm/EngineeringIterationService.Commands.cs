using CP6.Core.Services.Integration;
using CP6.Core.Services.Pub;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
namespace CP6.Core.Services.Plm;
public sealed partial class EngineeringIterationService
{
    private static void ValidateCommand(EngineeringIterationCommandV1 c)
    {
        EngineeringIterationValuesV1.Text(c.Reason);
        var create=c.Action is "CREATE_PROJECT" or "CREATE_ITERATION";
        if(create?(c.ExpectedVersion!=null || c.ExpectedEtag!=null):(c.ExpectedVersion==null || c.ExpectedEtag==null || c.ExpectedEtag==Guid.Empty))throw Error("ITERATION_COMMAND_INVALID",400);
        if(!create)Revision(c.ExpectedVersion);
        if(c.Action!="CREATE_PROJECT" && (c.ExpectedAuthorityVersion!=null || c.DepartmentId!=null))throw Error("ITERATION_COMMAND_INVALID",400);
        var project=c.Action is "CREATE_PROJECT" or "EDIT_PROJECT";
        var plan=c.Action is "CREATE_ITERATION" or "EDIT_PLAN";
        var input=c.Action is "CREATE_ITERATION" or "REPLACE_INPUTS";
        var task=c.Action=="SAVE_TASK";
        var taskId=c.Action is "START_TASK" or "BLOCK_TASK" or "SUBMIT_TASK" or "CONFIRM_TASK" or "RETURN_TASK" or "CANCEL_TASK";
        if(c.Action is not("CREATE_PROJECT" or "EDIT_PROJECT" or "ACTIVATE_PROJECT" or "CREATE_ITERATION" or "EDIT_PLAN" or "REPLACE_INPUTS" or "START" or "HOLD" or "RESUME" or "SAVE_TASK" or "START_TASK" or "BLOCK_TASK" or "SUBMIT_TASK" or "CONFIRM_TASK" or "RETURN_TASK" or "CANCEL_TASK")
            || project!=(c.Project!=null) || plan!=(c.Plan!=null) || input!=(c.RequirementSet!=null) || task!=(c.Task!=null) || taskId!=(c.TaskId!=null)
            || (c.Action=="CREATE_ITERATION")!=(c.ProjectId!=null) || (c.Action=="CREATE_ITERATION")!=(c.Origin!=null)
            || (c.Action=="SUBMIT_TASK")!=(c.Deliverables!=null))throw Error("ITERATION_COMMAND_INVALID",400);
        if(project)EngineeringIterationValuesV1.Project(c.Project!);if(plan)EngineeringIterationValuesV1.Plan(c.Plan!);
        if(input)RequirementSetValuesV1.Ref(c.RequirementSet);if(task)EngineeringIterationValuesV1.Task(c.Task!);
        if(c.Origin!=null)EngineeringRevisionValuesV1.Origin(c.Origin);
        if(c.TaskId==Guid.Empty || c.ProjectId==Guid.Empty)throw Error("ITERATION_COMMAND_INVALID",400);
        if(c.Deliverables!=null)foreach(var item in c.Deliverables)ControlledDocumentValuesV1.Ref(item);
    }
    private async Task<string> Number(string key,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if(!await db.Pub_DocSequences.AsNoTracking().AnyAsync(x=>x.BizKey==key,ct))throw Error("ITERATION_NUMBER_RULE_REQUIRED",422);
        var number=await new SeqService(db).NextAsync(key,DateTime.UtcNow);EngineeringIterationValuesV1.Text(number,true,512);return number;
    }
    private static void PlanMembers(IterationProjectInputV1 p,IterationPlanV1 plan)
    {
        if(!p.Members.Contains(plan.OwnerId))throw Error("ITERATION_OWNER_NOT_MEMBER",422);
        if(p.StartDate!=null && plan.StartDate<p.StartDate || p.EndDate!=null && plan.EndDate>p.EndDate)throw Error("ITERATION_PLAN_OUTSIDE_PROJECT_DATES",422);
    }
    private async Task<EngineeringIterationBodyV1> Change(OperationsAccessV1 a,EngineeringIterationStoreV1 s,EngineeringIterationCommandV1 c,EngineeringIterationSnapshotV1? old,CancellationToken ct)
    {
        if(c.Action=="CREATE_PROJECT")
        {
            if(!c.Project!.Members.Contains(a.ActorId))throw Error("ITERATION_CREATOR_MEMBERSHIP_REQUIRED",422);
            await Staff(c.Project.Members,ct);await ProjectSources(a,c.Project,false,ct);
            return new("PROJECT",c.Project.Name,await Number("PLM_PROJECT",ct),new(c.Project,"DRAFT"),null);
        }
        if(c.Action is "EDIT_PROJECT" or "ACTIVATE_PROJECT")
        {
            var p=old?.Body.Project??throw Error("ITERATION_PROJECT_REQUIRED",422);
            if(p.Definition.OwnerId!=a.ActorId)throw Error("ITERATION_PROJECT_OWNER_REQUIRED",403);
            var definition=c.Project??p.Definition;await Staff(definition.Members,ct);
            await ProjectSources(a,definition,c.Action=="ACTIVATE_PROJECT" || p.State=="ACTIVE",ct);
            if(c.Action=="ACTIVATE_PROJECT" && p.State!="DRAFT")throw Error("ITERATION_PROJECT_STATE_CHANGED",409);
            return old!.Body with{Name=definition.Name,Project=new(definition,c.Action=="ACTIVATE_PROJECT"?"ACTIVE":p.State)};
        }
        if(c.Action=="CREATE_ITERATION")
        {
            var p=await Project(a,s,c.ProjectId!.Value,ct);if(p.State!="ACTIVE")throw Error("ITERATION_ACTIVE_PROJECT_REQUIRED",422);
            if(p.Definition.OwnerId!=a.ActorId)throw Error("ITERATION_PROJECT_OWNER_REQUIRED",403);
            PlanMembers(p.Definition,c.Plan!);await Staff([c.Plan!.OwnerId],ct);await ProjectSources(a,p.Definition,true,ct);
            var parent=await s.Read(c.ProjectId.Value,null,ct);var input=await Capture(a,c.RequirementSet!,ct);
            return new("ITERATION",c.Plan.Name,await Number("PLM_ITERATION",ct),null,
                new(c.ProjectId.Value,parent.Reference,c.Plan,c.Origin!,"PLANNED","NORMAL","1","1",input,[]));
        }
        var work=old?.Body.Iteration??throw Error("ITERATION_REQUIRED",422);
        var project=await Project(a,s,work.ProjectId,ct);
        if(project.State!="ACTIVE" || work.Stage is not("PLANNED" or "IN_DESIGN"))throw Error("ITERATION_STAGE_CHANGED",409);
        var manager=a.ActorId==project.Definition.OwnerId || a.ActorId==work.Plan.OwnerId;
        if(!manager && c.Action is not("START_TASK" or "BLOCK_TASK" or "SUBMIT_TASK" or "CONFIRM_TASK" or "RETURN_TASK"))throw Error("ITERATION_MANAGER_REQUIRED",403);
        if(c.Action!="RESUME" && work.HoldState!="NORMAL")throw Error("ITERATION_HELD",409);
        if(c.Action=="HOLD")return old!.Body with{Iteration=work with{HoldState="HELD"}};
        if(c.Action=="RESUME" && work.HoldState!="HELD")throw Error("ITERATION_NOT_HELD",409);
        if(c.Action=="EDIT_PLAN")
        {
            PlanMembers(project.Definition,c.Plan!);await Staff([c.Plan!.OwnerId],ct);
            if(work.Tasks.Any(x=>x.State!="CANCELLED" && (x.Plan.DueDate<c.Plan.StartDate || x.Plan.DueDate>c.Plan.EndDate)))throw Error("ITERATION_TASK_OUTSIDE_PLAN_DATES",422);
            return old!.Body with{Name=c.Plan.Name,Iteration=work with{Plan=c.Plan,PlanVersion=EngineeringIterationValuesV1.Next(work.PlanVersion)}};
        }
        if(c.Action=="REPLACE_INPUTS")
        {
            if(work.Tasks.Any(x=>x.State is "SUBMITTED" or "DONE"))throw Error("ITERATION_CONFIRMED_INPUT_IMPACT_REVIEW_REQUIRED",422);
            var capture=await Capture(a,c.RequirementSet!,ct);await ProjectSources(a,project.Definition,true,ct);
            var parent=await s.Read(work.ProjectId,null,ct);
            return old!.Body with{Iteration=work with{Input=capture,ProjectReference=parent.Reference,InputVersion=EngineeringIterationValuesV1.Next(work.InputVersion)}};
        }
        // This native pre-review stage has no external submissions. Future OA/VER adapters must
        // add their authoritative pending-request checks before they may advance this Owner's stage.
        if(c.Action is "START" or "RESUME" or "SUBMIT_TASK" or "CONFIRM_TASK")
        {
            await ProjectSources(a,project.Definition,true,ct);await Inputs(a,work.Input,true,ct);
            if(await ProjectDrift(s,work,project,ct))throw Error("ITERATION_PROJECT_INPUT_CHANGED",412);
            PlanMembers(project.Definition,work.Plan);await Staff([work.Plan.OwnerId],ct);
        }
        if(c.Action=="START")
        {
            if(work.Stage!="PLANNED" || !work.Tasks.Any(x=>x.State!="CANCELLED"))throw Error("ITERATION_PLAN_REQUIRED",422);
            return old!.Body with{Iteration=work with{Stage="IN_DESIGN"}};
        }
        if(c.Action=="RESUME")return old!.Body with{Iteration=work with{HoldState="NORMAL"}};
        var tasks=work.Tasks.ToList();var target=c.Task?.Id??c.TaskId!.Value;var index=tasks.FindIndex(x=>x.Plan.Id==target);
        if(c.Action=="SAVE_TASK")
        {
            var plan=c.Task!;
            if(!project.Definition.Members.Contains(plan.OwnerId) || !project.Definition.Members.Contains(plan.ConfirmerId))throw Error("ITERATION_TASK_MEMBER_REQUIRED",422);
            await Staff([plan.OwnerId,plan.ConfirmerId],ct);
            if(plan.DueDate<work.Plan.StartDate || plan.DueDate>work.Plan.EndDate)throw Error("ITERATION_TASK_OUTSIDE_PLAN_DATES",422);
            if(index>=0 && tasks[index].State is "SUBMITTED" or "DONE" or "CANCELLED")throw Error("ITERATION_TASK_PLAN_LOCKED",409);
            if(index<0){if(tasks.Count>=2048)throw Error("ITERATION_TASK_LIMIT",422);tasks.Add(new(plan,"TODO",[],null,null,null,c.Reason));}
            else tasks[index]=tasks[index] with{Plan=plan,LastReason=c.Reason};
            work=work with{PlanVersion=EngineeringIterationValuesV1.Next(work.PlanVersion)};
        }
        else
        {
            if(index<0)throw Error("ITERATION_TASK_NOT_FOUND",404);var task=tasks[index];
            if(c.Action!="CANCEL_TASK" && work.Stage!="IN_DESIGN")throw Error("ITERATION_NOT_STARTED",409);
            if(c.Action is "START_TASK" or "BLOCK_TASK" or "SUBMIT_TASK")
            {
                if(a.ActorId!=task.Plan.OwnerId)throw Error("ITERATION_TASK_OWNER_REQUIRED",403);
                if(task.State is "DONE" or "CANCELLED" or "SUBMITTED")throw Error("ITERATION_TASK_STATE_CHANGED",409);
            }
            var state=c.Action switch{"START_TASK"=>"IN_PROGRESS","BLOCK_TASK" or "RETURN_TASK"=>"BLOCKED","SUBMIT_TASK"=>"SUBMITTED","CONFIRM_TASK"=>"DONE","CANCEL_TASK"=>"CANCELLED",_=>throw Error("ITERATION_COMMAND_INVALID",400)};
            if(c.Action=="SUBMIT_TASK")
            {await Deliverables(a,c.Deliverables!,project.Definition.Product,true,ct);task=task with{Deliverables=c.Deliverables!,SubmittedBy=a.ActorId};}
            if(c.Action is "CONFIRM_TASK" or "RETURN_TASK")
            {
                if(a.ActorId!=task.Plan.ConfirmerId || a.ActorId==task.SubmittedBy || a.ActorId==task.Plan.OwnerId)throw Error("ITERATION_INDEPENDENT_CONFIRMER_REQUIRED",403);
                if(task.State!="SUBMITTED")throw Error("ITERATION_TASK_STATE_CHANGED",409);
                if(c.Action=="CONFIRM_TASK")
                {
                    await Staff([task.Plan.OwnerId,task.Plan.ConfirmerId],ct);
                    if(!project.Definition.Members.Contains(task.Plan.OwnerId))throw Error("ITERATION_TASK_MEMBER_REQUIRED",422);
                    await Deliverables(a,task.Deliverables,project.Definition.Product,true,ct);task=task with{ConfirmedBy=a.ActorId,ConfirmedAt=DateTimeOffset.UtcNow};
                }
                else task=task with{SubmittedBy=null};
            }
            if(c.Action=="CANCEL_TASK")
            {if(task.State is "DONE" or "CANCELLED" or "SUBMITTED")throw Error("ITERATION_TASK_PLAN_LOCKED",409);work=work with{PlanVersion=EngineeringIterationValuesV1.Next(work.PlanVersion)};}
            tasks[index]=task with{State=state,LastReason=c.Reason};
        }
        return old!.Body with{Iteration=work with{Tasks=tasks.ToArray()}};
    }
    private async Task<bool> ProjectDrift(EngineeringIterationStoreV1 s,IterationWorkV1 work,IterationProjectV1 current,CancellationToken ct)
    {
        var p=await CapturedProject(s,work,ct);return p.Product!=current.Definition.Product || p.Site!=current.Definition.Site;
    }
    private async Task<IterationProjectInputV1> CapturedProject(EngineeringIterationStoreV1 s,IterationWorkV1 work,CancellationToken ct)
    {
        var original=await s.Read(work.ProjectId,Revision(work.ProjectReference.Version),ct);
        if(original.Reference!=work.ProjectReference || original.Body.Project==null)throw Error("ITERATION_PROJECT_ORIGINAL_UNAVAILABLE",503);
        return original.Body.Project.Definition;
    }
}
