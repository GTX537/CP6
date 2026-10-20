using CP6.Core.Services.CrmIdentity;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Sys.IamAdministrationV1;

namespace CP6.Core.Services.Sys;

public sealed record IamRoleCommandV1 : IamCommandV1
{
    public int RoleId { get; init; }
    public string? RoleName { get; init; }
    public string? Description { get; init; }
    public bool Enable { get; init; } = true;
    public int OrderNo { get; init; }
}
public sealed record IamRoleViewV1(int RoleId,string RoleName,string? Description,bool Enable,int OrderNo,
    DateTime CreateDate,string Version);
public sealed record IamRoleEditorV1(Guid TenantId,Guid ActorId,string ReferencesVersion,IamRoleViewV1[] Roles);
public sealed record IamRoleReferencesV1(int RoleId,int PrimaryUsers,int AdditionalUsers,int Menus,int Actions,
    int DataScopes,int Fields,int SsoDefaults,bool SsoApproved,bool DeletionAllowed,string[] UnverifiedReferenceCategories);

/// <summary>Tenant role identity is retained; disabling never erases assignments or historical authorization.</summary>
public sealed class IamRoleAdministrationV1(CP6Context db,IamAdministrationV1 authority)
{
    public Task<IamRoleEditorV1> ReadAsync(Func<CancellationToken,Task<Guid>> actor,CancellationToken ct)
        =>authority.ExecuteAsync("role:query",actor,async(who,token)=>
        {
            var rows=await db.Sys_Roles.AsNoTracking().OrderBy(x=>x.OrderNo).ThenBy(x=>x.RoleId).ToListAsync(token);
            var views=new List<IamRoleViewV1>();
            foreach(var r in rows)views.Add(new(r.RoleId,r.RoleName,r.Description,r.Enable,r.OrderNo,r.CreateDate,
                await authority.Version(IdentityEventContracts.RoleSubject(r.RoleId),token)));
            return new IamRoleEditorV1(db.CurrentTenantId,who,await authority.ReferencesVersion(token),views.ToArray());
        },ct);
    public Task<IamRoleReferencesV1> ReferencesAsync(int id,Func<CancellationToken,Task<Guid>> actor,CancellationToken ct)
        =>authority.ExecuteAsync("role:query",actor,async(_,token)=>
        {
            if(!await db.Sys_Roles.AnyAsync(x=>x.RoleId==id,token))throw Error(404,"IAM_ROLE_NOT_FOUND");
            return await References(id,token);
        },ct);
    public Task<IamOperationV1> ChangeAsync(string kind,IamRoleCommandV1 command,Func<CancellationToken,Task<Guid>> actor,CancellationToken ct)
    {
        Validate(command);
        if(kind is not("role-create" or "role-update" or "role-delete"))throw Error(400,"IAM_OPERATION_KIND_INVALID");
        if(kind!="role-delete" && (string.IsNullOrWhiteSpace(command.RoleName)||command.RoleName.Length>100
            || command.Description?.Length>500))throw Error(400,"IAM_ROLE_INPUT_INVALID");
        return authority.ExecuteAsync(Permission(kind),actor,async(who,token)=>
        {
            var original=await authority.Original(who,kind,command,token);if(original!=null)return original;
            var row=await db.Sys_Roles.SingleOrDefaultAsync(x=>x.RoleId==command.RoleId,token);
            if(kind=="role-create" && row!=null)throw Error(409,"IAM_ROLE_EXISTS");
            if(kind!="role-create" && row==null)throw Error(404,"IAM_ROLE_NOT_FOUND");
            var version=await authority.Version(IdentityEventContracts.RoleSubject(command.RoleId),token);
            if(version!=command.ExpectedVersion || command.ExpectedReferencesVersion!=await authority.ReferencesVersion(token))
                throw Error(412,"IAM_REFERENCES_CHANGED");
            if(kind=="role-delete")
            {
                var refs=await References(command.RoleId,token);
                if(refs.PrimaryUsers+refs.AdditionalUsers+refs.SsoDefaults>0 || refs.SsoApproved)
                    throw Error(409,"IAM_ROLE_IDENTITY_REFERENCED");
                // Main cannot certify historical business, approval, or privacy retention authority.
                throw Error(409,"IAM_ROLE_RETENTION_REVIEW_REQUIRED");
            }
            if(kind=="role-create")
            {
                // A deleted identity with retained references must never be silently reincarnated.
                if(version!="absent")throw Error(409,"IAM_ROLE_RETAINED_IDENTITY");
                var refs=await References(command.RoleId,token);
                if(refs.PrimaryUsers+refs.AdditionalUsers+refs.Menus+refs.Actions+refs.DataScopes+refs.Fields+refs.SsoDefaults>0 || refs.SsoApproved)
                    throw Error(409,"IAM_ROLE_IDENTITY_REFERENCED");
                row=new(){TenantId=db.CurrentTenantId,RoleId=command.RoleId,CreateDate=DateTime.Now};
                db.Sys_Roles.Add(row);
            }
            row!.RoleName=command.RoleName!;row.Description=command.Description;row.Enable=command.Enable;row.OrderNo=command.OrderNo;
            try{db.RequireIdentityCapture(db.CurrentTenantId);}catch(InvalidOperationException){throw Error(503,"IAM_IDENTITY_WRITER_UNAVAILABLE");}
            authority.Audit(who,command.OperationId,kind,command.Reason!,null,command.RoleId);
            try{await db.SaveChangesAsync(token);}
            catch(InvalidOperationException e) when(e.Message is "C02_UNKNOWN_IDENTITY_AGGREGATE" or "C02_IDENTITY_CONTRACT_INVALID")
            {throw Error(503,"IAM_IDENTITY_CONTRACT_UNAVAILABLE");}
            var result=new IamOperationV1(command.OperationId,kind,"COMMITTED",Version:await authority.Version(IdentityEventContracts.RoleSubject(command.RoleId),token),
                ReferencesVersion:await authority.ReferencesVersion(token),RoleId:command.RoleId);
            await authority.Store(who,command,result,token);return result;
        },ct);
    }
    private async Task<IamRoleReferencesV1> References(int id,CancellationToken ct)
    {
        var document=await db.Database.SqlQuery<string>($"SELECT document AS \"Value\" FROM cp6_authentication_v1.sso_admission_policies WHERE tenant_id={db.CurrentTenantId}").SingleOrDefaultAsync(ct);
        SsoIdentityAdmissionPolicyV1? policy=null;
        if(document!=null)
        {
            try{policy=JsonSerializer.Deserialize<SsoIdentityAdmissionPolicyV1>(document);}
            catch(JsonException){throw Error(503,"IAM_SSO_REFERENCES_UNAVAILABLE");}
            if(policy?.ApprovedJitRoleIds==null)throw Error(503,"IAM_SSO_REFERENCES_UNAVAILABLE");
        }
        return new(id,await db.Sys_Users.CountAsync(x=>x.RoleId==id,ct),await db.Sys_UserRoles.CountAsync(x=>x.RoleId==id,ct),
            await db.Sys_RoleMenus.CountAsync(x=>x.RoleId==id,ct),await db.Sys_RoleActions.CountAsync(x=>x.RoleId==id,ct),
            await db.Sys_RoleDataScopes.CountAsync(x=>x.RoleId==id,ct),await db.Sys_RoleFieldPerms.CountAsync(x=>x.RoleId==id,ct),
            await db.Sys_TenantSsoConfigs.CountAsync(x=>x.DefaultRoleId==id,ct),policy?.ApprovedJitRoleIds.Contains(id)==true,
            false,["business","approval-history","privacy-retention-hold"]);
    }
}
