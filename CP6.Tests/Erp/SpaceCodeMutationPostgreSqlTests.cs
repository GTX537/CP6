using System.Net;
using System.Text.Json;
using CP6.Core.Services.Platform;
using CP6.Entity.DomainModels.Space;
using CP6.Entity.DTOs.Space;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private const string SpaceCodePath="api/space";
    private const string SpaceRulePermission="space-code-rule";
    private const string SpaceCodeBarrier="LOCK TABLE public.\"Space_CodeRule\", public.\"Space_Location\" IN ACCESS EXCLUSIVE MODE";
    private static List<CodeSegmentDef> SpaceCodeSegments()=>
    [
        new(){Key="zone",Source="zone-code",Sep="-"},
        new(){Key="rack",Source="rack-seq",Width=2,Sep="-"},
        new(){Key="col",Source="col",Width=2,Sep=""}
    ];
    private static CodeRuleDto SpaceRuleDto(string name,int scopeType=0,Guid? scopeId=null,bool isDefault=false)
        => new(){RuleName=name,ScopeType=scopeType,ScopeId=scopeId,IsDefault=isDefault,Segments=SpaceCodeSegments()};

    private async Task<Harness> SpaceCodeSeed()
    {
        var h=await PalletSeed("create");var db=h.Db;var menu=await db.Sys_Menus.SingleOrDefaultAsync(m=>m.MenuKey==SpaceRulePermission||m.RoutePath=="/space/code-rule");
        if(menu==null){menu=new(){MenuId=904,MenuKey=SpaceRulePermission,MenuName="SIM Space code",RoutePath="/space/code-rule"};db.Sys_Menus.Add(menu);await db.SaveChangesAsync();}
        foreach(var action in new[]{"add","edit","delete","generate"})
        {
            if(!await db.Sys_MenuActions.AnyAsync(a=>a.MenuId==menu.MenuId&&a.ActionCode==action))db.Sys_MenuActions.Add(new(){MenuId=menu.MenuId,ActionCode=action,ActionName=action});
            db.Sys_RoleActions.Add(new(){RoleId=72,MenuId=menu.MenuId,ActionCode=action});
        }
        var site=Guid.NewGuid();var floor=Guid.NewGuid();var zone=Guid.NewGuid();var rack=Guid.NewGuid();
        db.Space_Sites.Add(new(){Id=site,SiteCode="SIM",SiteName="SIM Site"});
        db.Space_Floors.Add(new(){Id=floor,SiteId=site,Level=1,FloorCode="F1",FloorName="SIM Floor"});
        db.Space_Zones.Add(new(){Id=zone,FloorId=floor,ZoneCode="A",ZoneName="SIM Zone"});
        db.Space_Racks.Add(new(){Id=rack,ZoneId=zone,FloorId=floor,RackCode="R01",X=0,Y=0,Cols=5,Levels=3,DepthCount=1});
        var segments=JsonSerializer.Serialize(SpaceCodeSegments());
        db.Space_CodeRules.AddRange(new(){Id=Guid.NewGuid(),RuleName="SIM Tenant",ScopeType=0,IsDefault=true,Segments=segments},new(){Id=Guid.NewGuid(),RuleName="SIM Floor",ScopeType=1,ScopeId=floor,IsDefault=true,Segments=segments});
        db.Space_Locations.AddRange(Location(1,"OLD-1",0),Location(2,null,0),Location(3,"FROZEN",1));
        await db.SaveChangesAsync();db.ChangeTracker.Clear();return h;
        Space_Location Location(int col,string? code,int status)=>new(){Id=Guid.NewGuid(),RackId=rack,FloorId=floor,Col=col,Level=1,Depth=1,Status=status,CodeOrigin=1,Placed=true,LocationCode=code};
    }

    private sealed record SpaceCodeHttpInput(HttpMethod Method,string Path,object Body);
    private static async Task<SpaceCodeHttpInput> SpaceCodeInput(Harness h,string operation)
    {
        var rule=await h.Db.Space_CodeRules.AsNoTracking().SingleAsync(x=>x.ScopeType==0);
        var loc=await h.Db.Space_Locations.AsNoTracking().SingleAsync(x=>x.Col==1);
        return operation switch
        {
            "create-rule"=>new(HttpMethod.Post,SpaceCodePath+"/code-rule",SpaceRuleDto("SIM New")),
            "edit-rule"=>new(HttpMethod.Put,SpaceCodePath+"/code-rule/"+rule.Id,SpaceRuleDto("SIM Changed",isDefault:true)),
            "delete-rule"=>new(HttpMethod.Delete,SpaceCodePath+"/code-rule/"+rule.Id,new{}),
            "rebuild"=>new(HttpMethod.Post,SpaceCodePath+"/floor/"+loc.FloorId+"/generate-codes",new{mode="rebuild"}),
            "single"=>new(HttpMethod.Post,SpaceCodePath+"/location/"+loc.Id+"/gen-code",new{}),
            _=>throw new ArgumentOutOfRangeException(nameof(operation))
        };
    }
    private static async Task<string> SpaceCodeRows(Harness h)
        => JsonSerializer.Serialize(new{Rules=await h.Db.Space_CodeRules.AsNoTracking().OrderBy(x=>x.Id).ToArrayAsync(),Locations=await h.Db.Space_Locations.AsNoTracking().OrderBy(x=>x.Id).ToArrayAsync()});


    [TenantManagementPostgresTheory]
    [InlineData("create-rule")][InlineData("edit-rule")][InlineData("delete-rule")]
    [InlineData("rebuild")][InlineData("single")]
    public async Task SpaceCode_existing_five_writes_decide_current_tenant_and_session(string operation)
    {
        await using var h=await SpaceCodeSeed();await using var administrator=await TenantManagementSeed();var connection=h.Db.Database.GetConnectionString()!;await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);var manager=await Login(host,administrator);var input=await SpaceCodeInput(h,operation);
        using var current=await host.Send(OperationsRequest(manager,HttpMethod.Get,ManagementPath(h.Tenant)));var active=await NumberHttpData<TenantManagementSnapshotV1>(current);var before=await SpaceCodeRows(h);var stock=await PalletStockRows(h);
        await using var business=new NpgsqlConnection(connection);await business.OpenAsync();await using var target=new NpgsqlConnection(connection);await target.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();
        Task<HttpResponseMessage> pending;FenceWait pauseWait,commandWait;TenantManagementReceiptV1 suspended;
        await using(var locked=await business.BeginTransactionAsync())
        {
            await using var barrier=new NpgsqlCommand(SpaceCodeBarrier,business,locked);await barrier.ExecuteNonQueryAsync();
            try
            {
                await using var held=await target.BeginTransactionAsync();await using var tenantLock=new NpgsqlCommand("SELECT 1 FROM public.\"Sys_Tenants\" WHERE \"Id\"=@tenant FOR UPDATE",target,held);tenantLock.Parameters.AddWithValue("tenant",h.Tenant);Assert.Equal(1,await tenantLock.ExecuteScalarAsync());
                var pause=host.Send(OperationsRequest(manager,HttpMethod.Post,ManagementPath(h.Tenant,"/commands"),new TenantManagementCommandV1("space-code-"+operation,active.Revision,"suspend",null)));
                try{pauseWait=await WaitForFence(observer,target.ProcessID,"%UPDATE%Sys_Tenants%",null,pause);pending=Send();commandWait=await SerialNativeWait(observer,pauseWait.Backend,business.ProcessID,"space-code-"+operation,pending);}
                finally{await held.RollbackAsync();}
                using var paused=await pause;suspended=await NumberHttpData<TenantManagementReceiptV1>(paused);Assert.False(suspended.Snapshot.Enable);
            }
            finally{await locked.RollbackAsync();}
        }
        using var denied=await pending;await DeviceEvidence("space-code-"+operation+"-pause.json",new{Operation=operation,Status=(int)denied.StatusCode,AuthorityWait=commandWait.Blocker==pauseWait.Backend,pauseWait,commandWait,Before=before,After=await SpaceCodeRows(h),StockBefore=stock,StockAfter=await PalletStockRows(h)});
        Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);Assert.Equal(pauseWait.Backend,commandWait.Blocker);Assert.Equal(before,await SpaceCodeRows(h));Assert.Equal(stock,await PalletStockRows(h));
        using var restored=await host.Send(OperationsRequest(manager,HttpMethod.Post,ManagementPath(h.Tenant,"/commands"),new TenantManagementCommandV1("space-code-restore-"+operation,suspended.Snapshot.Revision,"reactivate",null)));Assert.True((await NumberHttpData<TenantManagementReceiptV1>(restored)).Snapshot.Enable);actor=await Login(host,h);using var recovered=await Send();Assert.True(recovered.StatusCode==HttpStatusCode.OK,await OperationsFailure(recovered));
        Task<HttpResponseMessage> Send()=>host.Send(OperationsRequest(actor,input.Method,input.Path,input.Body));
    }

    [TenantManagementPostgresTheory]
    [InlineData("create-rule","add")][InlineData("edit-rule","edit")][InlineData("delete-rule","delete")][InlineData("rebuild","generate")]
    public async Task SpaceCode_current_action_revocation_wins_before_the_write(string operation,string action)
    {
        await using var h=await SpaceCodeSeed();var connection=h.Db.Database.GetConnectionString()!;await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);var input=await SpaceCodeInput(h,operation);var before=await SpaceCodeRows(h);
        var menu=await h.Db.Sys_Menus.Where(m=>m.MenuKey==SpaceRulePermission||m.RoutePath=="/space/code-rule").Select(m=>m.MenuId).SingleAsync();await using var control=new NpgsqlConnection(connection);await control.OpenAsync();await using var business=new NpgsqlConnection(connection);await business.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();Task<HttpResponseMessage> pending;FenceWait wait;
        await using(var locked=await business.BeginTransactionAsync())
        {
            await using var barrier=new NpgsqlCommand(SpaceCodeBarrier,business,locked);await barrier.ExecuteNonQueryAsync();
            try
            {
                await using var changed=await control.BeginTransactionAsync();await using var change=new NpgsqlCommand("SELECT cp6_authentication_v1.acquire(); DELETE FROM public.\"Sys_RoleAction\" WHERE \"TenantId\"=@tenant AND \"RoleId\"=72 AND \"MenuId\"=@menu AND \"ActionCode\"=@action",control,changed);change.Parameters.AddWithValue("tenant",h.Tenant);change.Parameters.AddWithValue("menu",menu);change.Parameters.AddWithValue("action",action);await change.ExecuteNonQueryAsync();
                pending=host.Send(OperationsRequest(actor,input.Method,input.Path,input.Body));wait=await SerialNativeWait(observer,control.ProcessID,business.ProcessID,"space-code-revoke-"+action,pending);await changed.CommitAsync();
            }
            finally{await locked.RollbackAsync();}
        }
        using var denied=await pending;await DeviceEvidence("space-code-revoke-"+action+".json",new{Status=(int)denied.StatusCode,AuthorityWait=wait.Blocker==control.ProcessID,wait,Before=before,After=await SpaceCodeRows(h)});Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);Assert.Equal(control.ProcessID,wait.Blocker);Assert.Equal(before,await SpaceCodeRows(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task SpaceCode_business_first_commits_before_the_later_tenant_pause()
    {
        await using var h=await SpaceCodeSeed();await using var administrator=await TenantManagementSeed();var connection=h.Db.Database.GetConnectionString()!;await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);var manager=await Login(host,administrator);var input=await SpaceCodeInput(h,"rebuild");
        using var current=await host.Send(OperationsRequest(manager,HttpMethod.Get,ManagementPath(h.Tenant)));var active=await NumberHttpData<TenantManagementSnapshotV1>(current);await using var business=new NpgsqlConnection(connection);await business.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();Task<HttpResponseMessage> writing,pause;FenceWait businessWait;FenceWait? pauseWait=null;
        await using(var locked=await business.BeginTransactionAsync())
        {
            await using var barrier=new NpgsqlCommand(SpaceCodeBarrier,business,locked);await barrier.ExecuteNonQueryAsync();
            try
            {
                writing=host.Send(OperationsRequest(actor,input.Method,input.Path,input.Body));businessWait=await SerialNativeWait(observer,business.ProcessID,business.ProcessID,"space-code-business-first",writing);
                pause=host.Send(OperationsRequest(manager,HttpMethod.Post,ManagementPath(h.Tenant,"/commands"),new TenantManagementCommandV1("space-code-business-first",active.Revision,"suspend",null)));
                try{pauseWait=await SerialNativeWait(observer,businessWait.Backend,business.ProcessID,"space-code-pause-after",pause);}catch(Xunit.Sdk.XunitException) when(pause.IsCompletedSuccessfully){ }
            }
            finally{await locked.RollbackAsync();}
        }
        using var committed=await writing;using var paused=await pause;var receipt=await NumberHttpData<TenantManagementReceiptV1>(paused);await DeviceEvidence("space-code-business-first.json",new{Status=(int)committed.StatusCode,AuthorityWait=pauseWait?.Blocker==businessWait.Backend,businessWait,pauseWait});
        Assert.Equal(HttpStatusCode.OK,committed.StatusCode);Assert.False(receipt.Snapshot.Enable);Assert.NotNull(pauseWait);Assert.Equal(businessWait.Backend,pauseWait!.Blocker);Assert.Equal(new[]{"A-01-01","A-01-02"},(await SpaceCodeHttpData<string[]>(committed)).OrderBy(x=>x));
    }
}
