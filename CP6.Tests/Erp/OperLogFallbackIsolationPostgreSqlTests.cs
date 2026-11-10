using System.Net;
using CP6.Entity.DTOs.Space;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData("reject")][InlineData("success")][InlineData("log-fault")]
    public async Task OperLogIsolation_fallback_never_saves_pending_business_state(string mode)
    {
        await using var h=await SceneIngressSeed();await using var other=await SceneIngressSeed();var connection=h.Db.Database.GetConnectionString()!;
        await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);
        var floor=await h.Db.Space_Floors.Where(x=>x.FloorCode=="F1").Select(x=>x.Id).SingleAsync();var published=await SceneIngressLocation(h,"FROZEN");
        var path=SpaceCodePath+"/floor/"+floor+"/scene";var reject=mode=="reject";var logFault=mode=="log-fault";
        var dto=new SceneSaveDto{Zones=[HierarchyZone(floor,"LOG-SCENE")],Deletes=reject?new Deletes{Locations=[published]}:null};
        var before=await HierarchyRows(h);var otherBefore=await HierarchyRows(other);var stock=await PalletStockRows(h);
        await using var control=new NpgsqlConnection(connection);
        if(logFault)
        {
            await control.OpenAsync();await using var install=new NpgsqlCommand($$"""
                CREATE FUNCTION public.cp6_test_operlog_write_failure() RETURNS trigger LANGUAGE plpgsql AS $fault$
                BEGIN RAISE EXCEPTION 'simulated isolated operation log failure'; END $fault$;
                CREATE TRIGGER cp6_test_operlog_write_failure BEFORE INSERT ON public."Sys_OperLogs" FOR EACH ROW
                WHEN (NEW."TenantId"='{{h.Tenant}}' AND NEW."RequestUrl"='/{{path}}') EXECUTE FUNCTION public.cp6_test_operlog_write_failure();
                """,control);await install.ExecuteNonQueryAsync();
        }
        try
        {
            using var response=await host.Send(OperationsRequest(actor,HttpMethod.Post,path,dto));var after=await HierarchyRows(h);var otherAfter=await HierarchyRows(other);var stockAfter=await PalletStockRows(h);
            var logs=await h.Db.Sys_OperLogs.IgnoreQueryFilters().AsNoTracking().Where(x=>x.RequestUrl=="/"+path).ToArrayAsync();var zones=await h.Db.Space_Zones.CountAsync(x=>x.FloorId==floor&&x.ZoneCode=="LOG-SCENE");
            var keepPublished=await h.Db.Space_Locations.AnyAsync(x=>x.Id==published&&x.Status==1&&x.LocationCode=="FROZEN");
            await DeviceEvidence("operlog-isolation-"+mode+".json",new{Status=(int)response.StatusCode,Before=before,After=after,OtherBefore=otherBefore,OtherAfter=otherAfter,StockBefore=stock,StockAfter=stockAfter,Zones=zones,KeepPublished=keepPublished,ExpectedTenant=h.Tenant,
                Logs=logs.Select(x=>new{x.Id,x.TenantId,x.HttpMethod,x.Controller,x.Action,x.RequestUrl,x.UserName,x.RequestBody,x.StatusCode}).ToArray()});
            Assert.Equal(reject?HttpStatusCode.BadRequest:HttpStatusCode.OK,response.StatusCode);Assert.True(keepPublished);Assert.Equal(otherBefore,otherAfter);Assert.Equal(stock,stockAfter);
            if(reject){Assert.Equal(before,after);Assert.Equal(0,zones);}else Assert.Equal(1,zones);
            if(logFault)Assert.Empty(logs);
            else
            {
                var log=Assert.Single(logs);Assert.True(log.Id>0);Assert.Equal(h.Tenant,log.TenantId);Assert.Equal("POST",log.HttpMethod);Assert.Equal("Scene",log.Controller);Assert.Equal("SaveScene",log.Action);
                Assert.Equal(reject?500:200,log.StatusCode);Assert.Null(log.RequestBody);Assert.False(string.IsNullOrWhiteSpace(log.UserName));
            }
        }
        finally
        {
            if(logFault){await using var remove=new NpgsqlCommand("DROP TRIGGER cp6_test_operlog_write_failure ON public.\"Sys_OperLogs\"; DROP FUNCTION public.cp6_test_operlog_write_failure()",control);await remove.ExecuteNonQueryAsync();}
        }
    }
}
