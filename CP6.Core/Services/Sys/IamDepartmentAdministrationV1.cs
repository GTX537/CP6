using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Sys.IamAdministrationV1;

namespace CP6.Core.Services.Sys;

public sealed record IamDepartmentCommandV1 : IamCommandV1
{
    public Guid? Id { get; init; }
    public Guid? ParentId { get; init; }
    public string? DeptCode { get; init; }
    public string? DeptName { get; init; }
    public Guid? LeaderId { get; init; }
    public int Sort { get; init; }
    public bool Enable { get; init; }=true;
    public string? ExpectedTreeVersion { get; init; }
    public string? PreviewVersion { get; init; }
}
public sealed record IamDepartmentViewV1(Guid Id,Guid? ParentId,string DeptCode,string DeptName,string Path,
    Guid? LeaderId,int Sort,bool Enable,string Version,bool Available,string? Diagnostic);
public sealed record IamDepartmentEditorV1(Guid TenantId,Guid ActorId,string TreeVersion,string ReferencesVersion,
    IamDepartmentViewV1[] Departments,IamUserRefV1[] Leaders);
public sealed record IamScopeReferenceV1(int RoleId,string ResourceKey,int ScopeType);
public sealed record IamDepartmentReferencesV1(Guid Id,int ChildCount,int UserCount,IamScopeReferenceV1[] ScopeRefs,
    bool DeletionAllowed,string[] UnverifiedReferenceCategories);
public sealed record IamDepartmentPreviewV1(Guid Id,Guid? OldParentId,Guid? NewParentId,string TreeVersion,string ReferencesVersion,
    string Version,int AffectedDescendantCount,int AffectedUsersCount,IamScopeReferenceV1[] ScopeRefs,
    string[] ApprovalRouteWarnings,string PreviewVersion);

/// <summary>Current Main organization commands. Never rewrites historical approval bindings or infers deletion authority.</summary>
public sealed class IamDepartmentAdministrationV1(CP6Context db,IamAdministrationV1 authority)
{
    public bool IsPostgreSql=>authority.IsPostgreSql;
    public Task<IamDepartmentEditorV1> ReadAsync(Func<CancellationToken,Task<Guid>> actor,CancellationToken ct)
        =>authority.ExecuteAsync<IamDepartmentEditorV1>("pub-dept:query",actor,async(who,token)=>
        {
            var all=await Departments(token);var views=new List<IamDepartmentViewV1>();
            foreach(var d in all.Values.OrderBy(x=>x.Sort).ThenBy(x=>x.Id))views.Add(new(d.Id,d.ParentId,d.DeptCode,d.DeptName,d.Path,
                d.LeaderId,d.Sort,d.Enable,await authority.Version("dept:"+d.Id.ToString("D"),token),
                IamUserAdministrationV1.DepartmentAvailable(d.Id,all),StructureValid(d.Id,all)?null:"IAM_TREE_UNHEALTHY"));
            return new(db.CurrentTenantId,who,await authority.Version("tree",token),await authority.ReferencesVersion(token),views.ToArray(),
                await db.Sys_Users.AsNoTracking().OrderBy(x=>x.UserName).Select(x=>new IamUserRefV1(x.Id,x.UserName,x.NickName,x.Enable)).ToArrayAsync(token));
        },ct);
    public Task<IamDepartmentPreviewV1> PreviewAsync(Guid id,Guid? parent,Func<CancellationToken,Task<Guid>> actor,CancellationToken ct)
        =>authority.ExecuteAsync("pub-dept:edit",actor,async(_,token)=>await Preview(id,parent,await Departments(token),token),ct);
    public Task<IamDepartmentReferencesV1> ReferencesAsync(Guid id,Func<CancellationToken,Task<Guid>> actor,CancellationToken ct)
        =>authority.ExecuteAsync("pub-dept:query",actor,async(_,token)=>
        {
            var all=await Departments(token);RequireDepartment(id,all);
            return new IamDepartmentReferencesV1(id,all.Values.Count(x=>x.ParentId==id),await db.Sys_Users.CountAsync(x=>x.DeptId==id,token),
                await ScopeReferences([id],token),false,["business","approval-history","privacy-retention-hold"]);
        },ct);
    public Task<IamOperationV1> ChangeAsync(string kind,IamDepartmentCommandV1 command,Func<CancellationToken,Task<Guid>> actor,CancellationToken ct)
    {
        Validate(command);
        if(kind is not("dept-create" or "dept-update" or "dept-move" or "dept-leader" or "dept-delete"))throw Error(400,"IAM_OPERATION_KIND_INVALID");
        var create=kind=="dept-create";
        if(string.IsNullOrWhiteSpace(command.ExpectedTreeVersion))throw Error(400,"IAM_TREE_VERSION_REQUIRED");
        if(create && command.Id!=null || !create && (command.Id==null || command.Id==Guid.Empty))throw Error(400,"IAM_DEPARTMENT_ID_INVALID");
        if(kind is "dept-create" or "dept-update")
        {
            if(string.IsNullOrWhiteSpace(command.DeptCode)||command.DeptCode.Length>50||string.IsNullOrWhiteSpace(command.DeptName)||command.DeptName.Length>100)
                throw Error(400,"IAM_DEPARTMENT_INPUT_INVALID");
        }
        return authority.ExecuteAsync(Permission(kind),actor,async(who,token)=>
        {
            var original=await authority.Original(who,kind,command,token);if(original!=null)return original;
            var all=await Departments(token);var row=create?new Sys_Dept{Id=Guid.NewGuid(),TenantId=db.CurrentTenantId}:RequireDepartment(command.Id!.Value,all);
            var version=create?"absent":await authority.Version("dept:"+row.Id.ToString("D"),token);
            if(version!=command.ExpectedVersion || command.ExpectedTreeVersion!=await authority.Version("tree",token)
                || command.ExpectedReferencesVersion!=await authority.ReferencesVersion(token))throw Error(412,"IAM_REFERENCES_CHANGED");
            if(kind=="dept-delete")
            {
                if(all.Values.Any(x=>x.ParentId==row.Id))throw Error(409,"E-PUB-002");
                if(await db.Sys_Users.AnyAsync(x=>x.DeptId==row.Id,token))throw Error(409,"E-PUB-003");
                if((await ScopeReferences([row.Id],token)).Length!=0)throw Error(409,"IAM_DEPARTMENT_SCOPE_REFERENCED");
                // There is no accepted complete business/approval/PRIV hold authority in this slice.
                throw Error(409,"IAM_DEPARTMENT_RETENTION_REVIEW_REQUIRED");
            }
            if(kind=="dept-move")
            {
                var preview=await Preview(row.Id,command.ParentId,all,token);
                if(command.PreviewVersion!=preview.PreviewVersion)throw Error(412,"IAM_PREVIEW_CHANGED");
                var old=row.Path;var prefix=ParentPath(command.ParentId,all)+row.Id.ToString("D")+"/";
                var subtree=Subtree(row.Id,all);
                if(subtree.Any(x=>prefix.Length+x.Path.Length-old.Length>900))throw Error(409,"IAM_DEPARTMENT_PATH_LIMIT");
                foreach(var d in subtree){db.Sys_Depts.Attach(d);d.Path=prefix+d.Path[old.Length..];d.Modifier=who.ToString("D");d.ModifyDate=DateTime.Now;}
                row.ParentId=command.ParentId;
            }
            else
            {
                await RequireLeader(command.LeaderId,create?null:row.LeaderId,token);
                if(create)
                {
                    if(all.Values.Any(x=>x.DeptCode==command.DeptCode))throw Error(409,"E-PUB-001");
                    row.DeptCode=command.DeptCode!;row.ParentId=command.ParentId;row.Path=ParentPath(command.ParentId,all)+row.Id.ToString("D")+"/";
                    if(row.Path.Length>900)throw Error(409,"IAM_DEPARTMENT_PATH_LIMIT");
                    row.Creator=who.ToString("D");row.CreateDate=DateTime.Now;db.Sys_Depts.Add(row);
                }
                else
                {
                    if(!StructureValid(row.Id,all))throw Error(409,"IAM_TREE_UNHEALTHY");
                    if(kind=="dept-update" && (row.DeptCode!=command.DeptCode || row.ParentId!=command.ParentId))throw Error(409,"IAM_DEPARTMENT_IMMUTABLE_FIELD");
                    db.Sys_Depts.Attach(row);row.Modifier=who.ToString("D");row.ModifyDate=DateTime.Now;
                }
                row.LeaderId=command.LeaderId;
                if(kind is "dept-create" or "dept-update"){row.DeptName=command.DeptName!;row.Sort=command.Sort;row.Enable=command.Enable;}
            }
            try{db.RequireIdentityCapture(db.CurrentTenantId);}catch(InvalidOperationException){throw Error(503,"IAM_IDENTITY_WRITER_UNAVAILABLE");}
            authority.Audit(who,command.OperationId,kind,command.Reason!,row.Id);await db.SaveChangesAsync(token);
            var result=new IamOperationV1(command.OperationId,kind,"COMMITTED",row.Id,await authority.Version("dept:"+row.Id.ToString("D"),token),await authority.ReferencesVersion(token),await authority.Version("tree",token));
            await authority.Store(who,command,result,token);return result;
        },ct);
    }
    private async Task<IamDepartmentPreviewV1> Preview(Guid id,Guid? parent,Dictionary<Guid,Sys_Dept> all,CancellationToken ct)
    {
        var source=RequireDepartment(id,all);var subtree=Subtree(id,all);
        if(parent!=null && subtree.Any(x=>x.Id==parent))throw Error(409,"E-PUB-004");
        ParentPath(parent,all);
        var ids=subtree.Select(x=>x.Id).ToArray();var count=await db.Sys_Users.CountAsync(x=>x.DeptId!=null && ids.Contains(x.DeptId.Value),ct);
        var result=new IamDepartmentPreviewV1(id,source.ParentId,parent,await authority.Version("tree",ct),await authority.ReferencesVersion(ct),
            await authority.Version("dept:"+id.ToString("D"),ct),subtree.Length-1,count,await ScopeReferences(ids,ct),
            ["Current routing changes only; existing approval snapshots remain unchanged.","CRM/Owner adoption is not certified by this preview."],"");
        return result with {PreviewVersion=AuthenticationPolicyServiceV1.Hash(result)};
    }
    private async Task<IamScopeReferenceV1[]> ScopeReferences(Guid[] ids,CancellationToken ct)
    {
        var assigned=await db.Sys_Users.AsNoTracking().Where(x=>x.DeptId!=null && ids.Contains(x.DeptId.Value)).Select(x=>new{x.Id,x.RoleId}).ToArrayAsync(ct);
        var users=assigned.Select(x=>x.Id).ToArray();var roles=assigned.Where(x=>x.RoleId!=null).Select(x=>x.RoleId!.Value)
            .Concat(await db.Sys_UserRoles.AsNoTracking().Where(x=>users.Contains(x.UserId)).Select(x=>x.RoleId).ToArrayAsync(ct)).ToHashSet();
        var scopes=await db.Sys_RoleDataScopes.AsNoTracking().ToListAsync(ct);
        return scopes.Where(x=>x.ScopeType==4 && PermissionAggregator.ParseGuids(x.CustomDeptIds).Intersect(ids).Any()
            || x.ScopeType is 2 or 3 && roles.Contains(x.RoleId)).Select(x=>new IamScopeReferenceV1(x.RoleId,x.ResourceKey,x.ScopeType))
            .Distinct().OrderBy(x=>x.RoleId).ThenBy(x=>x.ResourceKey,StringComparer.Ordinal).ThenBy(x=>x.ScopeType).ToArray();
    }
    private async Task RequireLeader(Guid? id,Guid? old,CancellationToken ct)
    {
        if(id==null)return;
        var leader=await db.Sys_Users.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct);
        if(leader==null || !leader.Enable && id!=old)throw Error(409,"IAM_LEADER_UNAVAILABLE");
    }
    private Task<Dictionary<Guid,Sys_Dept>> Departments(CancellationToken ct)=>db.Sys_Depts.AsNoTracking().ToDictionaryAsync(x=>x.Id,ct);
    private static Sys_Dept RequireDepartment(Guid id,IReadOnlyDictionary<Guid,Sys_Dept> all)=>all.GetValueOrDefault(id)??throw Error(404,"IAM_DEPARTMENT_NOT_FOUND");
    private static string ParentPath(Guid? parent,IReadOnlyDictionary<Guid,Sys_Dept> all)
    {if(parent==null)return "/";if(!IamUserAdministrationV1.DepartmentAvailable(parent.Value,all))throw Error(409,"IAM_PARENT_UNAVAILABLE");return all[parent.Value].Path;}
    private static Sys_Dept[] Subtree(Guid id,IReadOnlyDictionary<Guid,Sys_Dept> all)
    {
        RequireDepartment(id,all);var found=new HashSet<Guid>{id};var queue=new Queue<Guid>();queue.Enqueue(id);
        while(queue.TryDequeue(out var parent))foreach(var d in all.Values.Where(x=>x.ParentId==parent))
        {if(!found.Add(d.Id))throw Error(409,"IAM_TREE_UNHEALTHY");queue.Enqueue(d.Id);}
        if(found.Any(x=>!StructureValid(x,all)))throw Error(409,"IAM_TREE_UNHEALTHY");
        return found.Select(x=>all[x]).OrderBy(x=>x.Path,StringComparer.Ordinal).ToArray();
    }
    public static bool StructureValid(Guid id,IReadOnlyDictionary<Guid,Sys_Dept> all)
    {
        var seen=new HashSet<Guid>();var path=new List<Guid>();Guid? current=id;
        while(current!=null){if(!seen.Add(current.Value)||!all.TryGetValue(current.Value,out var row))return false;path.Insert(0,current.Value);current=row.ParentId;}
        return path.Select((x,i)=>all[x].Path=="/"+string.Join("/",path.Take(i+1))+"/").All(x=>x);
    }
}
