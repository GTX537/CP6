using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Seed;

// Register independent qualification points. IAM must grant them explicitly; no default role grants.
public static class FormDispositionPermissionSeed
{
    public const int MenuId=744;
    public static void EnsureSeeded(CP6Context db)
    {
        const string key="oa-form-dispositions";
        var menu=db.Sys_Menus.SingleOrDefault(x=>x.MenuId==MenuId);
        if(menu==null)
            db.Sys_Menus.Add(new(){MenuId=MenuId,MenuKey=key,MenuName="表单字段处置",RoutePath="/oa/form-dispositions",ParentId=740,OrderNo=744,Icon="DocumentChecked",Enable=true});
        else if(menu.MenuKey!=key||menu.RoutePath!="/oa/form-dispositions")
            throw new InvalidOperationException("FORM_DISPOSITION_MENU_CONFLICT");
        db.SaveChanges();
        foreach(var tenant in db.Sys_Tenants.AsNoTracking().Select(x=>x.Id).ToArray())
        foreach(var (code,name) in new[]{("query","读取处置明细"),("approve","批准或驳回处置"),("retain","执行原件留存"),("revoke","撤销处置资格"),("hold","设置或解除保留限制")})
            if(!db.Sys_MenuActions.IgnoreQueryFilters().Any(x=>x.TenantId==tenant&&x.MenuId==MenuId&&x.ActionCode==code))
                db.Sys_MenuActions.Add(new(){TenantId=tenant,MenuId=MenuId,ActionCode=code,ActionName=name});
        db.SaveChanges();
    }
}
