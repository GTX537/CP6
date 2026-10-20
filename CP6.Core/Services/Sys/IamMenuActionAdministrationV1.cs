using CP6.Entity.DTOs.Sys;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Sys.IamAdministrationV1;

namespace CP6.Core.Services.Sys;

public sealed record IamMenuActionCommandV1:IamCommandV1
{
    public int MenuId{get;init;}
    public string? ExpectedCatalogVersion{get;init;}
    public List<MenuActionDto>? Actions{get;init;}
}
public sealed record IamMenuActionEditorV1(Guid TenantId,Guid ActorId,int MenuId,string Version,string ReferencesVersion,
    string CatalogVersion,MenuActionDto[] Actions);

public sealed partial class IamRolePolicyAdministrationV1
{
    public Task<IamMenuActionEditorV1> ReadMenuActionsAsync(int menuId,Func<CancellationToken,Task<Guid>> actor,CancellationToken ct)
        =>authority.ExecuteAsync("pub-role-perm:query",actor,async(who,token)=>
        {
            var catalog=await Catalog(token);
            if(!catalog.Menus.Any(x=>x.MenuId==menuId))throw Error(404,"IAM_MENU_UNAVAILABLE");
            return new IamMenuActionEditorV1(db.CurrentTenantId,who,menuId,await authority.Version("permission-catalog",token),
                await authority.ReferencesVersion(token),catalog.Version,catalog.Actions.Where(x=>x.MenuId==menuId)
                    .Select(x=>new MenuActionDto{ActionCode=x.ActionCode,ActionName=x.ActionName,Sort=x.Sort}).ToArray());
        },ct);
    public Task<IamOperationV1> ChangeMenuActionsAsync(IamMenuActionCommandV1 c,Func<CancellationToken,Task<Guid>> actor,CancellationToken ct)
    {
        Validate(c);
        if(string.IsNullOrWhiteSpace(c.ExpectedCatalogVersion)||c.Actions==null||c.Actions.Any(x=>x==null
            || string.IsNullOrWhiteSpace(x.ActionCode)||x.ActionCode.Length>50||x.ActionName==null||x.ActionName.Length>100)
            || c.Actions.Select(x=>x.ActionCode).Distinct(StringComparer.Ordinal).Count()!=c.Actions.Count)throw Error(400,"IAM_MENU_ACTION_INPUT_INVALID");
        return authority.ExecuteAsync("pub-role-perm:edit",actor,async(who,token)=>
        {
            const string kind="menu-actions";var original=await authority.Original(who,kind,c,token);if(original!=null)return original;
            if(!await db.Sys_Menus.AnyAsync(x=>x.MenuId==c.MenuId,token))throw Error(404,"IAM_MENU_UNAVAILABLE");
            if(c.ExpectedVersion!=await authority.Version("permission-catalog",token)||c.ExpectedReferencesVersion!=await authority.ReferencesVersion(token)
                ||c.ExpectedCatalogVersion!=(await Catalog(token)).Version)throw Error(412,"IAM_REFERENCES_CHANGED");
            var old=await db.Sys_MenuActions.Where(x=>x.MenuId==c.MenuId).ToListAsync(token);
            var keys=c.Actions.Select(x=>x.ActionCode).ToHashSet(StringComparer.Ordinal);
            var granted=await db.Sys_RoleActions.AsNoTracking().Where(x=>x.MenuId==c.MenuId).Select(x=>x.ActionCode).ToListAsync(token);
            if(granted.Any(x=>!keys.Contains(x)))throw Error(409,"IAM_MENU_ACTION_REFERENCED");
            db.Sys_MenuActions.RemoveRange(old.Where(x=>!keys.Contains(x.ActionCode)));
            foreach(var item in c.Actions)
            {
                var row=old.SingleOrDefault(x=>x.ActionCode==item.ActionCode);
                if(row==null){row=new(){MenuId=c.MenuId,ActionCode=item.ActionCode,Creator=who.ToString("D"),CreateDate=DateTime.Now};db.Sys_MenuActions.Add(row);}
                else{row.Modifier=who.ToString("D");row.ModifyDate=DateTime.Now;}
                row.ActionName=item.ActionName;row.Sort=item.Sort;
            }
            authority.Audit(who,c.OperationId,kind,c.Reason!,null);
            await db.SaveChangesAsync(token);
            var result=new IamOperationV1(c.OperationId,kind,"COMMITTED",Version:await authority.Version("permission-catalog",token),ReferencesVersion:await authority.ReferencesVersion(token));
            await authority.Store(who,c,result,token);return result;
        },ct);
    }
}
