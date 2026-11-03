using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public sealed partial class PersonalFormOwnerV2
{
    private static string CommandResource(string operation,string resource)
    {
        if(operation is Create or Direct or Favorite)return resource;
        return Guid.TryParseExact(resource,"D",out var id)&&id!=Guid.Empty?id.ToString("D"):throw Error("COMMAND_SCOPE_INVALID",400);
    }
    // Closing is an explicit terminal reservation of the original slot, under the same
    // authority lock as its writer. NOT_OBSERVED alone never permits a fresh command.
    public async Task<PersonalFormResultV2> CloseAsync(string operation,string resource,string command,CancellationToken ct=default)
    {
        if(operation is not(Create or Save or Delete or Restore or Rebase or Direct or Submit or Decision or Favorite or SendBack or Transfer or AddSign or ReturnedEdit or ReturnedResubmit or ResolveApprover)
            ||!System.Text.RegularExpressions.Regex.IsMatch(command,"\\A[A-Za-z0-9_.-]{1,100}\\z"))throw Error("COMMAND_SCOPE_INVALID",400);
        resource=CommandResource(operation,resource);
        if(operation==Decision)command=Guid.TryParseExact(command,"D",out var commandId)&&commandId!=Guid.Empty?commandId.ToString("D"):throw Error("COMMAND_SCOPE_INVALID",400);
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var committing=false;
        try
        {
            var lease=await ReadLease(ct);var actor=await Actor(ct);await Action(actor,"query",ct,operation is Decision or SendBack or Transfer or AddSign or ReturnedEdit or ReturnedResubmit or ResolveApprover?"oa-inbox":"oa-form-catalog");
            var old=await Original(actor.UserId,operation,resource,command,ct);
            if(old!=null)
            {
                if(operation==Decision && !old.Closed)
                {
                    var source=await TaskSourceAsync(Guid.Parse(resource),ct);await Mask(actor,source.FormHead,await Publication(source.FormHead,source.FormVersion,ct),ct);
                    await Final(actor,lease,ct,false);return Parse<PersonalFormResultV2>(old.Result);
                }
                var result=await Disclose(old,actor,ct);await Final(actor,lease,ct,false);return result;
            }
            // The slot belongs to this actual actor; resource permission is still current.
            if(operation==Favorite) { /* Only this actor's preference slot is reserved; no form content is disclosed. */ }
            else if(operation is Create or Direct)
            {
                var head=await Heads().SingleOrDefaultAsync(h=>h.Key==resource,ct)??throw Error("FORM_NOT_FOUND",404);
                var id=await db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM cp6_form_authoring_v2.versions WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND head_id={head.Id} ORDER BY version_number DESC LIMIT 1").SingleAsync(ct);
                await Mask(actor,head.Id,await Publication(head.Id,id,ct),ct);
            }
            else
            {
                if(!Guid.TryParseExact(resource,"D",out var id))throw Error("COMMAND_SCOPE_INVALID",400);
                if(operation is Decision or SendBack or Transfer or AddSign){var source=await TaskSourceAsync(id,ct);await Mask(actor,source.FormHead,await Publication(source.FormHead,source.FormVersion,ct),ct);}
                else if(operation==ResolveApprover){var resolution=await Resolution(id,ct);await ResolutionAccess(await PersonalInstanceSource(resolution.Instance,ct),actor,ct);}
                else if(operation is ReturnedEdit or ReturnedResubmit)await ReturnedAccess(await ReturnedSourceAsync(id,ct),actor,ct);
                else{var row=await Owned(id,actor.UserId,ct);await Mask(actor,row.Head,await Publication(row.Head,row.Version,ct),ct);}
            }
            await Ready(ct);var closed=new PersonalFormResultV2(command,"CLOSED",null,null,null,Clock(),null);
            var audit=Encode(new{actor.UserId,Scope=lease.Snapshot,Reason="ACTOR_EXPLICIT_CLOSE_WITHOUT_EFFECTS"});
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_personal_form_v2.commands(id,tenant_id,environment_id,actor_id,operation,resource_key,command_id,digest,raw_body,result_json,audit_json,completed,closed)
                VALUES({Guid.NewGuid()},{db.CurrentTenantId},{fence.EnvironmentId},{actor.UserId},{operation},{resource},{command},{"CLOSED"},{Array.Empty<byte>()},{Encode(closed)},{audit},true,true)
                """,ct);
            await Final(actor,lease,ct,false);await Ready(ct);committing=true;await tx.CommitAsync(ct);return closed;
        }
        catch(Exception ex){await Rollback(tx,committing,ex);throw;}
    }
}
