using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Sys.IamAdministrationV1;

namespace CP6.Core.Services.Sys;

public sealed record IamUserCommandV1 : IamCommandV1
{
    public Guid? Id { get; init; }
    public string? UserName { get; init; }
    public string? NickName { get; init; }
    public string? Password { get; init; }
    public int? RoleId { get; init; }
    public int[]? RoleIds { get; init; }
    public bool Enable { get; init; } = true;
    public Guid? DeptId { get; init; }
    public Guid? ManagerId { get; init; }
    public string? Email { get; init; }
}

public sealed record IamUserViewV1(Guid Id, string UserName, string? NickName, bool Enable, int? RoleId,
    int[] RoleIds, Guid? DeptId, Guid? ManagerId, string? Email, string Version, bool MustChangePassword, bool Locked);
public sealed record IamRoleRefV1(int RoleId,string RoleName,bool Enable);
public sealed record IamUserRefV1(Guid Id,string UserName,string? NickName,bool Enable);
public sealed record IamDeptRefV1(Guid Id,string DeptName,Guid? ParentId,bool Enable,bool Available);
public sealed record IamUserEditorV1(Guid TenantId,Guid ActorId,string ReferencesVersion,IamUserViewV1? User,
    IamRoleRefV1[] Roles,IamDeptRefV1[] Departments,IamUserRefV1[] Managers);

/// <summary>Ordinary tenant user administration; never binds platform or authentication-owned fields.</summary>
public sealed class IamUserAdministrationV1(CP6Context db,IamAdministrationV1 authority,
    IPasswordHasher hasher,IRefreshTokenService refresh)
{
    public bool IsPostgreSql => authority.IsPostgreSql;
    public Task<IamUserEditorV1> ReadAsync(Guid? id,Func<CancellationToken,Task<Guid>> actor,CancellationToken ct)
        => authority.ExecuteAsync<IamUserEditorV1>("user:query",actor,async (who,token) =>
        {
            IamUserViewV1? view = null;
            if (id != null)
            {
                var user = await User(id.Value,token);
                view = new(user.Id,user.UserName,user.NickName,user.Enable,user.RoleId,await Roles(user,token),
                    user.DeptId,user.ManagerId,user.Email,await authority.Version("user:"+user.Id.ToString("D"),token),
                    user.MustChangePassword,user.LockedUntil > DateTime.Now);
            }
            var departments = await db.Sys_Depts.AsNoTracking().ToDictionaryAsync(x=>x.Id,token);
            return new(db.CurrentTenantId,who,await authority.ReferencesVersion(token),view,
                await db.Sys_Roles.AsNoTracking().OrderBy(x=>x.RoleId).Select(x=>new IamRoleRefV1(x.RoleId,x.RoleName,x.Enable)).ToArrayAsync(token),
                departments.Values.Select(x=>new IamDeptRefV1(x.Id,x.DeptName,x.ParentId,x.Enable,DepartmentAvailable(x.Id,departments))).ToArray(),
                await db.Sys_Users.AsNoTracking().OrderBy(x=>x.UserName).Select(x=>new IamUserRefV1(x.Id,x.UserName,x.NickName,x.Enable)).ToArrayAsync(token));
        },ct);

    public Task<IamOperationV1> ChangeAsync(string kind,IamUserCommandV1 input,Func<CancellationToken,Task<Guid>> actor,CancellationToken ct)
    {
        Validate(input);
        if (kind is not ("user-create" or "user-update" or "user-roles" or "user-org")) throw Error(400,"IAM_OPERATION_KIND_INVALID");
        var create=kind=="user-create";
        if (create && input.Id != null || !create && (input.Id == null || input.Id == Guid.Empty)) throw Error(400,"IAM_USER_ID_INVALID");
        if (kind is "user-create" or "user-update")
        {
            if (string.IsNullOrWhiteSpace(input.UserName) || input.UserName.Length>100 || input.NickName?.Length>100
                || input.Email?.Length>100 || input.Password?.Length>200 || create && string.IsNullOrEmpty(input.Password)) throw Error(400,"IAM_USER_INPUT_INVALID");
        }
        if (kind != "user-org" && (input.RoleIds == null || input.RoleIds.Length>100)) throw Error(400,"IAM_ROLE_SET_REQUIRED");
        if (input.Email?.Length>100) throw Error(400,"IAM_USER_INPUT_INVALID");
        var command=input with { RoleIds=input.RoleIds?.Distinct().Order().ToArray() };
        return authority.ExecuteAsync(Permission(kind),actor,async (who,token) =>
        {
            var original=await authority.Original(who,kind,command,token); if(original!=null)return original;
            var user=create ? new Sys_User { Id=Guid.NewGuid(),TenantId=db.CurrentTenantId,IsPlatformAdmin=false,MustChangePassword=true } : await User(command.Id!.Value,token);
            var version=create ? "absent" : await authority.Version("user:"+user.Id.ToString("D"),token);
            if (command.ExpectedVersion!=version || command.ExpectedReferencesVersion!=await authority.ReferencesVersion(token)) throw Error(412,"IAM_REFERENCES_CHANGED");
            var previousRoles=create ? [] : await Roles(user,token);
            var roles=kind=="user-org" ? previousRoles : command.RoleIds!;
            var primary=kind=="user-org" ? user.RoleId : command.RoleId;
            if(primary!=null && !roles.Contains(primary.Value))throw Error(409,"E-PUB-011");
            var currentRoles=await db.Sys_Roles.AsNoTracking().Where(x=>roles.Contains(x.RoleId)).ToListAsync(token);
            if(currentRoles.Count!=roles.Length || currentRoles.Any(x=>!x.Enable && !previousRoles.Contains(x.RoleId)))throw Error(409,"IAM_ROLE_UNAVAILABLE");
            if(kind is not "user-roles")
            {
                await RequireOrganization(user.Id,command.DeptId,command.ManagerId,user.DeptId,user.ManagerId,token);
                if(kind is not "user-org" && await db.Sys_Users.AsNoTracking().AnyAsync(x=>x.Id!=user.Id && x.UserName==command.UserName,token))throw Error(409,"IAM_USERNAME_CONFLICT");
            }
            // A published Main identity/outbox writer is mandatory for new administration effects.
            try { db.RequireIdentityCapture(db.CurrentTenantId); }
            catch(InvalidOperationException){throw Error(503,"IAM_IDENTITY_WRITER_UNAVAILABLE");}
            if(create)db.Sys_Users.Add(user);
            if(kind is "user-create" or "user-update")
            {
                user.UserName=command.UserName!; user.NickName=command.NickName; user.Enable=command.Enable;
                if(!string.IsNullOrEmpty(command.Password))
                {
                    user.Password=hasher.Hash(command.Password);user.PasswordChangedAt=DateTime.Now;user.MustChangePassword=true;
                    if(!create)await refresh.RevokeAllForUserAsync(user.Id,saveChanges:false);
                }
            }
            if(kind is not "user-roles") { user.DeptId=command.DeptId;user.ManagerId=command.ManagerId;user.Email=command.Email; }
            user.RoleId=primary;
            if(create){user.Creator=who.ToString("D");user.CreateDate=DateTime.Now;}
            else {user.Modifier=who.ToString("D");user.ModifyDate=DateTime.Now;}
            var memberships=await db.Sys_UserRoles.Where(x=>x.UserId==user.Id).ToListAsync(token);
            db.Sys_UserRoles.RemoveRange(memberships.Where(x=>!roles.Contains(x.RoleId)));
            foreach(var role in roles.Except(memberships.Select(x=>x.RoleId)))db.Sys_UserRoles.Add(new Sys_UserRole
                { Id=Guid.NewGuid(),TenantId=db.CurrentTenantId,UserId=user.Id,RoleId=role,Creator=who.ToString("D"),CreateDate=DateTime.Now });
            authority.Audit(who,command.OperationId,kind,command.Reason!,user.Id);
            await db.SaveChangesAsync(token);
            var result=new IamOperationV1(command.OperationId,kind,"COMMITTED",user.Id,
                await authority.Version("user:"+user.Id.ToString("D"),token),await authority.ReferencesVersion(token));
            await authority.Store(who,command,result,token);return result;
        },ct);
    }

    private async Task<Sys_User> User(Guid id,CancellationToken ct)
        => await db.Sys_Users.SingleOrDefaultAsync(x=>x.Id==id,ct) ?? throw Error(404,"IAM_USER_NOT_FOUND");
    private async Task<int[]> Roles(Sys_User user,CancellationToken ct)
    {
        var values=await db.Sys_UserRoles.AsNoTracking().Where(x=>x.UserId==user.Id).Select(x=>x.RoleId).ToListAsync(ct);
        if(user.RoleId is int primary)values.Add(primary);return values.Distinct().Order().ToArray();
    }
    private async Task RequireOrganization(Guid user,Guid? department,Guid? manager,Guid? oldDept,Guid? oldManager,CancellationToken ct)
    {
        if(department!=null)
        {
            var departments=await db.Sys_Depts.AsNoTracking().ToDictionaryAsync(x=>x.Id,ct);
            if(!departments.ContainsKey(department.Value) || department!=oldDept && !DepartmentAvailable(department.Value,departments))throw Error(409,"IAM_DEPARTMENT_UNAVAILABLE");
        }
        if(manager!=null)
        {
            var current=await db.Sys_Users.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==manager,ct);
            if(current==null || manager==user || !current.Enable && manager!=oldManager)throw Error(409,"IAM_MANAGER_UNAVAILABLE");
            var seen=new HashSet<Guid>{user};
            while(current!=null)
            {
                if(!seen.Add(current.Id))throw Error(409,"IAM_MANAGER_CYCLE");
                if(current.ManagerId==null)break;
                current=await db.Sys_Users.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==current.ManagerId,ct);
                if(current==null)throw Error(409,"IAM_MANAGER_UNAVAILABLE");
            }
        }
    }
    public static bool DepartmentAvailable(Guid id,IReadOnlyDictionary<Guid,Sys_Dept> departments)
    {
        var seen=new HashSet<Guid>();var path=new List<Guid>();Guid? cursor=id;
        while(cursor!=null)
        {
            if(!seen.Add(cursor.Value)||!departments.TryGetValue(cursor.Value,out var dept)||!dept.Enable)return false;
            path.Insert(0,cursor.Value);
            // Each ancestor must carry its own exact chain; never trust a malformed materialized path.
            cursor=dept.ParentId;
        }
        for(var i=0;i<path.Count;i++)if(departments[path[i]].Path!="/"+string.Join("/",path.Take(i+1))+"/")return false;
        return true;
    }
}
