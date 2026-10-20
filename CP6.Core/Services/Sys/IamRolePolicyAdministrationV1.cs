using CP6.Core.Services.CrmIdentity;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DTOs.Sys;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Sys.IamAdministrationV1;

namespace CP6.Core.Services.Sys;

public sealed record IamRolePolicyCommandV1 : IamCommandV1
{
    public string? ExpectedCatalogVersion { get; init; }
    public List<int>? MenuIds { get; init; }
    public List<RoleActionItem>? Actions { get; init; }
    public List<RoleDataScopeDto>? Scopes { get; init; }
    public string? ResourceKey { get; init; }
    public List<RoleFieldPermDto>? Fields { get; init; }
}
public sealed record IamPolicyMenuV1(int MenuId,string MenuName,string? MenuKey,int? ParentId,bool Enable);
public sealed record IamPolicyRoleV1(int RoleId,string RoleName,bool Enable);
public sealed record IamPolicyDepartmentV1(Guid Id,string DeptName,bool Available);
public sealed record IamPolicyCatalogV1(string Version,IamPolicyMenuV1[] Menus,MenuActionFullDto[] Actions,
    DataScopeResourceDto[] Scopes,FieldResourceDto[] Fields,IamPolicyDepartmentV1[] Departments);
public sealed record IamRolePolicyEditorV1(Guid TenantId,Guid ActorId,string Mode,int? RoleId,string Version,
    string ReferencesVersion,IamPolicyRoleV1[] Roles,IamPolicyCatalogV1 Catalog,RolePermDto Function,
    RoleDataScopeDto[] DataScopes,RoleFieldPermDto[] FieldPerms,string[] Warnings);

/// <summary>Closed configuration domains; effective multi-role aggregation is deliberately unchanged.</summary>
public sealed partial class IamRolePolicyAdministrationV1(CP6Context db,IamAdministrationV1 authority)
{
    public static string Resource(string mode)=>mode switch
    {"actions"=>"pub-role-perm","scopes"=>"pub-data-scope","fields"=>"pub-field-perm",_=>throw Error(400,"IAM_POLICY_MODE_INVALID")};
    public Task<IamRolePolicyEditorV1> ReadAsync(string mode,int? roleId,Func<CancellationToken,Task<Guid>> actor,CancellationToken ct)
        =>authority.ExecuteAsync(Resource(mode)+":query",actor,async(who,token)=>
        {
            var roles=await db.Sys_Roles.AsNoTracking().OrderBy(x=>x.OrderNo).ThenBy(x=>x.RoleId)
                .Select(x=>new IamPolicyRoleV1(x.RoleId,x.RoleName,x.Enable)).ToArrayAsync(token);
            if(roleId!=null && !roles.Any(x=>x.RoleId==roleId))throw Error(404,"IAM_ROLE_NOT_FOUND");
            var catalog=await Catalog(token);
            var function=new RolePermDto();var scopes=Array.Empty<RoleDataScopeDto>();var fields=Array.Empty<RoleFieldPermDto>();
            if(roleId!=null)
            {
                if(mode=="actions")
                {
                    function.MenuIds=await db.Sys_RoleMenus.AsNoTracking().Where(x=>x.RoleId==roleId).Select(x=>x.MenuId).ToListAsync(token);
                    function.Actions=await db.Sys_RoleActions.AsNoTracking().Where(x=>x.RoleId==roleId)
                        .Select(x=>new RoleActionItem{MenuId=x.MenuId,ActionCode=x.ActionCode}).ToListAsync(token);
                }
                if(mode=="scopes")scopes=(await db.Sys_RoleDataScopes.AsNoTracking().Where(x=>x.RoleId==roleId).ToListAsync(token))
                    .Select(x=>new RoleDataScopeDto{ResourceKey=x.ResourceKey,ScopeType=x.ScopeType,CustomDeptIds=PermissionAggregator.ParseGuids(x.CustomDeptIds).ToList()}).ToArray();
                if(mode=="fields")fields=await db.Sys_RoleFieldPerms.AsNoTracking().Where(x=>x.RoleId==roleId)
                    .Select(x=>new RoleFieldPermDto{ResourceKey=x.ResourceKey,FieldName=x.FieldName,Access=x.Access}).ToArrayAsync(token);
            }
            var warnings=new List<string>();
            if(function.MenuIds.Any(id=>!catalog.Menus.Any(x=>x.MenuId==id))
                || function.Actions.Any(a=>!catalog.Actions.Any(x=>x.MenuId==a.MenuId && x.ActionCode==a.ActionCode)))warnings.Add("IAM_UNREGISTERED_STORED_ACTIONS");
            if(scopes.Any(s=>!catalog.Scopes.Any(x=>x.Key==s.ResourceKey && x.Supports.Contains(s.ScopeType))))warnings.Add("IAM_UNREGISTERED_STORED_SCOPES");
            if(fields.Any(f=>!catalog.Fields.Any(x=>x.Key==f.ResourceKey && x.Fields.Any(d=>d.Name==f.FieldName)) || f.Access is <1 or >3))warnings.Add("IAM_UNREGISTERED_STORED_FIELDS");
            return new IamRolePolicyEditorV1(db.CurrentTenantId,who,mode,roleId,
                roleId==null?"absent":await authority.Version(IdentityEventContracts.RoleSubject(roleId.Value),token),await authority.ReferencesVersion(token),roles,
                catalog with{Menus=mode=="actions"?catalog.Menus:[],Actions=mode=="actions"?catalog.Actions:[],
                    Scopes=mode=="scopes"?catalog.Scopes:[],Departments=mode=="scopes"?catalog.Departments:[],Fields=mode=="fields"?catalog.Fields:[]},
                function,scopes,fields,warnings.ToArray());
        },ct);
    public Task<IamOperationV1> ChangeAsync(string mode,int roleId,IamRolePolicyCommandV1 command,Func<CancellationToken,Task<Guid>> actor,CancellationToken ct)
    {
        Validate(command);var kind="role-policy-"+mode;var permission=Resource(mode)+":edit";
        if(string.IsNullOrWhiteSpace(command.ExpectedCatalogVersion))throw Error(400,"IAM_CATALOG_VERSION_REQUIRED");
        // Bind the route target into the persisted fingerprint as well as the database lookup.
        var bound=new IamBoundRolePolicyCommandV1{OperationId=command.OperationId,ExpectedVersion=command.ExpectedVersion,
            ExpectedReferencesVersion=command.ExpectedReferencesVersion,Reason=command.Reason,RoleId=roleId,Mode=mode,Input=command};
        return authority.ExecuteAsync(permission,actor,async(who,token)=>
        {
            var original=await authority.Original(who,kind,bound,token);if(original!=null)return original;
            if(!await db.Sys_Roles.AnyAsync(x=>x.RoleId==roleId,token))throw Error(404,"IAM_ROLE_NOT_FOUND");
            var catalog=await Catalog(token);
            if(command.ExpectedVersion!=await authority.Version(IdentityEventContracts.RoleSubject(roleId),token)
                || command.ExpectedReferencesVersion!=await authority.ReferencesVersion(token)
                || command.ExpectedCatalogVersion!=catalog.Version)throw Error(412,"IAM_REFERENCES_CHANGED");
            if(mode=="actions")await SetActions(roleId,command,catalog,who,token);
            if(mode=="scopes")await SetScopes(roleId,command,catalog,who,token);
            if(mode=="fields")await SetFields(roleId,command,catalog,who,token);
            try{db.RequireIdentityCapture(db.CurrentTenantId);}catch(InvalidOperationException){throw Error(503,"IAM_IDENTITY_WRITER_UNAVAILABLE");}
            authority.Audit(who,command.OperationId,kind,command.Reason!,null,roleId);
            try{await db.SaveChangesAsync(token);}
            catch(InvalidOperationException e) when(e.Message is "C02_UNKNOWN_IDENTITY_AGGREGATE" or "C02_IDENTITY_CONTRACT_INVALID")
            {throw Error(503,"IAM_IDENTITY_CONTRACT_UNAVAILABLE");}
            var result=new IamOperationV1(command.OperationId,kind,"COMMITTED",Version:await authority.Version(IdentityEventContracts.RoleSubject(roleId),token),
                ReferencesVersion:await authority.ReferencesVersion(token),RoleId:roleId);
            await authority.Store(who,bound,result,token);return result;
        },ct);
    }
    private async Task<IamPolicyCatalogV1> Catalog(CancellationToken ct)
    {
        var menus=await db.Sys_Menus.AsNoTracking().OrderBy(x=>x.MenuId).Select(x=>new IamPolicyMenuV1(x.MenuId,x.MenuName,x.MenuKey,x.ParentId,x.Enable)).ToArrayAsync(ct);
        var actions=await db.Sys_MenuActions.AsNoTracking().OrderBy(x=>x.MenuId).ThenBy(x=>x.ActionCode)
            .Select(x=>new MenuActionFullDto{MenuId=x.MenuId,ActionCode=x.ActionCode,ActionName=x.ActionName,Sort=x.Sort}).ToArrayAsync(ct);
        var definitions=AuthorizationCatalogV1.Capture();
        var scopes=definitions.Scopes.Values.OrderBy(x=>x.Key,StringComparer.Ordinal).Select(x=>new DataScopeResourceDto{Key=x.Key,Name=x.Name,Default=x.Default,Supports=x.Supports.ToArray()}).ToArray();
        var fields=definitions.Fields.OrderBy(x=>x.Key,StringComparer.Ordinal).Select(pair=>new FieldResourceDto{Key=pair.Key,
            Fields=pair.Value.OrderBy(x=>x.Name,StringComparer.Ordinal).Select(x=>new FieldDefDto{Name=x.Name,Label=x.Label}).ToList()}).ToArray();
        if(scopes.Any(x=>x.Supports.Length==0 || x.Supports.Any(v=>v is <1 or >5)||!x.Supports.Contains(x.Default))
            || fields.Any(x=>x.Fields.Select(f=>f.Name).Distinct(StringComparer.Ordinal).Count()!=x.Fields.Count))throw Error(503,"IAM_CATALOG_UNAVAILABLE");
        var all=await db.Sys_Depts.AsNoTracking().ToDictionaryAsync(x=>x.Id,ct);
        var departments=all.Values.OrderBy(x=>x.Id).Select(x=>new IamPolicyDepartmentV1(x.Id,x.DeptName,IamUserAdministrationV1.DepartmentAvailable(x.Id,all))).ToArray();
        var version=AuthenticationPolicyServiceV1.Hash(new{revision=await authority.Version("permission-catalog",ct),menus,actions,scopes,fields});
        return new(version,menus,actions,scopes,fields,departments);
    }
    private async Task SetActions(int roleId,IamRolePolicyCommandV1 c,IamPolicyCatalogV1 catalog,Guid actor,CancellationToken ct)
    {
        if(c.MenuIds==null||c.Actions==null||c.MenuIds.Count!=c.MenuIds.Distinct().Count()
            || c.Actions.Any(x=>x==null)||c.Actions.Select(x=>(x.MenuId,x.ActionCode)).Distinct().Count()!=c.Actions.Count)throw Error(400,"IAM_ACTION_MATRIX_INVALID");
        var menus=c.MenuIds.ToHashSet();
        if(c.Actions.Any(a=>!menus.Contains(a.MenuId)))throw Error(400,"E-PUB-021");
        if(menus.Any(id=>!catalog.Menus.Any(x=>x.MenuId==id)))throw Error(409,"IAM_MENU_UNAVAILABLE");
        if(c.Actions.Any(a=>!catalog.Actions.Any(x=>x.MenuId==a.MenuId && x.ActionCode==a.ActionCode)
            || !catalog.Menus.Any(x=>x.MenuId==a.MenuId && !string.IsNullOrWhiteSpace(x.MenuKey))))throw Error(409,"IAM_ACTION_UNREGISTERED");
        var oldMenus=await db.Sys_RoleMenus.Where(x=>x.RoleId==roleId).ToListAsync(ct);
        db.Sys_RoleMenus.RemoveRange(oldMenus.Where(x=>!menus.Contains(x.MenuId)));
        foreach(var id in menus.Except(oldMenus.Select(x=>x.MenuId)))db.Sys_RoleMenus.Add(new(){RoleId=roleId,MenuId=id});
        var oldActions=await db.Sys_RoleActions.Where(x=>x.RoleId==roleId).ToListAsync(ct);var keys=c.Actions.Select(x=>(x.MenuId,x.ActionCode)).ToHashSet();
        db.Sys_RoleActions.RemoveRange(oldActions.Where(x=>!keys.Contains((x.MenuId,x.ActionCode))));
        foreach(var a in c.Actions.Where(x=>!oldActions.Any(o=>o.MenuId==x.MenuId && o.ActionCode==x.ActionCode)))
            db.Sys_RoleActions.Add(new(){RoleId=roleId,MenuId=a.MenuId,ActionCode=a.ActionCode,Creator=actor.ToString("D"),CreateDate=DateTime.Now});
    }
    private async Task SetScopes(int roleId,IamRolePolicyCommandV1 c,IamPolicyCatalogV1 catalog,Guid actor,CancellationToken ct)
    {
        if(c.Scopes==null||c.Scopes.Any(x=>x==null)||c.Scopes.Select(x=>x.ResourceKey).Distinct(StringComparer.Ordinal).Count()!=c.Scopes.Count
            || !c.Scopes.Select(x=>x.ResourceKey).ToHashSet(StringComparer.Ordinal).SetEquals(catalog.Scopes.Select(x=>x.Key)))
            throw Error(400,"IAM_SCOPE_MATRIX_INCOMPLETE");
        foreach(var item in c.Scopes)
        {
            if(!catalog.Scopes.Any(x=>x.Key==item.ResourceKey && x.Supports.Contains(item.ScopeType)))throw Error(400,"E-PUB-031");
            if(item.ScopeType==4 && (item.CustomDeptIds==null || item.CustomDeptIds.Count==0))throw Error(400,"E-PUB-032");
            if(item.ScopeType!=4 && item.CustomDeptIds?.Count>0)throw Error(400,"IAM_SCOPE_DEPARTMENTS_UNEXPECTED");
            if(item.ScopeType==4 && item.CustomDeptIds!.Any(id=>!catalog.Departments.Any(x=>x.Id==id && x.Available)))throw Error(409,"IAM_DEPARTMENT_UNAVAILABLE");
        }
        db.Sys_RoleDataScopes.RemoveRange(await db.Sys_RoleDataScopes.Where(x=>x.RoleId==roleId).ToListAsync(ct));
        foreach(var x in c.Scopes)db.Sys_RoleDataScopes.Add(new(){RoleId=roleId,ResourceKey=x.ResourceKey,ScopeType=x.ScopeType,
            CustomDeptIds=x.ScopeType==4?string.Join(",",x.CustomDeptIds!.Distinct().Order()):null,Creator=actor.ToString("D"),CreateDate=DateTime.Now});
    }
    private async Task SetFields(int roleId,IamRolePolicyCommandV1 c,IamPolicyCatalogV1 catalog,Guid actor,CancellationToken ct)
    {
        var domain=catalog.Fields.SingleOrDefault(x=>x.Key==c.ResourceKey);
        if(domain==null)throw Error(400,"E-PUB-041");
        if(c.Fields==null||c.Fields.Any(x=>x==null || x.ResourceKey!=c.ResourceKey || x.Access is <1 or >3)
            || c.Fields.Select(x=>x.FieldName).Distinct(StringComparer.Ordinal).Count()!=c.Fields.Count
            || !c.Fields.Select(x=>x.FieldName).ToHashSet(StringComparer.Ordinal).SetEquals(domain.Fields.Select(x=>x.Name)))
            throw Error(400,"IAM_FIELD_MATRIX_INCOMPLETE");
        var existing=await db.Sys_RoleFieldPerms.Where(x=>x.RoleId==roleId).ToListAsync(ct);
        db.Sys_RoleFieldPerms.RemoveRange(existing.Where(x=>StringComparer.Ordinal.Equals(x.ResourceKey,c.ResourceKey)));
        foreach(var x in c.Fields.Where(x=>x.Access!=1))db.Sys_RoleFieldPerms.Add(new(){RoleId=roleId,ResourceKey=x.ResourceKey,
            FieldName=x.FieldName,Access=x.Access,Creator=actor.ToString("D"),CreateDate=DateTime.Now});
    }
    private sealed record IamBoundRolePolicyCommandV1:IamCommandV1
    {public int RoleId{get;init;}public string Mode{get;init;}="";public IamRolePolicyCommandV1 Input{get;init;}=new();}
}
