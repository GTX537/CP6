using System.Data.Common;
using CP6.Core.Services.Oa;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.WebApi.BackgroundServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private sealed class TimeoutHistoricalCreation:DbCommandInterceptor
    {
        internal bool Enabled=true;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,CommandEventData eventData,
            InterceptionResult<DbDataReader> result,CancellationToken cancellationToken=default)
        {
            // Fixture-only simulation of a task created two hours earlier. The actual
            // separate WebApi process has no clock override and executes the real worker.
            if(Enabled&&command.CommandText.Contains("SELECT clock_timestamp() AS \"Value\""))
                command.CommandText=command.CommandText.Replace("clock_timestamp()","(clock_timestamp()-interval '2 hours')");
            return ValueTask.FromResult(result);
        }
    }
    [OaNativeBrowserPostgresFact]
    public async Task NativeBrowser_actual_timeout_host_worker_reminder_and_readonly_diagnostics()
    {
        var clock=new TimeoutHistoricalCreation();await using var h=await Seed(clock);await TimeoutZone(h);
        var f=await NativeFormSetup(h,configure:g=>{var n=g.Nodes.Single(n=>n.Id=="a");n.TimeoutHours=1;n.TimeoutAction="remind";});clock.Enabled=false;
        var service=Guid.NewGuid();await TimeoutOwner(h,f.Owner,DateTimeOffset.UtcNow,service);
        await h.Db.Sys_Menus.Where(m=>m.MenuKey=="oa-inbox").ExecuteUpdateAsync(s=>s.SetProperty(m=>m.RoutePath,"/oa/inbox"));
        // The HTTP fixture deliberately disables every hosted service. Run the real
        // timeout hosted service in its own actual Host, scoped tenant enumerator and
        // database connections, without enabling unrelated infrastructure workers.
        using var workerHost=new HostBuilder().ConfigureServices(services=>{
            services.AddSingleton(new NativeTimeoutOptionsV2{Enabled=true,ServiceId=service,HostKey="isolated-postgres-test-host"});
            services.AddScoped<ITenantContext,TenantContext>();services.AddScoped<ITenantEnumerator,TenantEnumerator>();
            services.AddScoped(sp=>server.Context(sp.GetRequiredService<ITenantContext>()));
            services.AddScoped(sp=>{
                var scoped=h with{Db=sp.GetRequiredService<CP6Context>(),Tenant=sp.GetRequiredService<ITenantContext>().CurrentTenantId};
                return new NativeTimeoutOwnerV2(scoped.Db,ScopeFence(scoped),sp.GetRequiredService<NativeTimeoutOptionsV2>(),PersonalForm(scoped),Decisions(scoped));
            });
            services.AddHostedService<NativeTimeoutWorkerV2>();
        }).Build();
        await workerHost.StartAsync();
        try
        {
            var until=DateTimeOffset.UtcNow.AddSeconds(20);
            while(!await h.Db.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT FROM cp6_native_timeout_v2.results r JOIN cp6_native_timeout_v2.claims c ON c.execution_id=r.execution_id WHERE c.task_id={f.Task}) AS \"Value\"").SingleAsync())
            {Assert.True(DateTimeOffset.UtcNow<until,"Real hosted timeout worker produced no effect");await Task.Delay(100);}
            using var result=await BrowserScenario(h,h.User,"oa-timeout-browser.mjs","timeout-browser",
                new(){["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString("D")},new(){["CP6_TASK_INSTANCE"]=f.Instance.ToString("D")});
        }
        finally{await workerHost.StopAsync();}
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_native_timeout_v2.results r JOIN cp6_native_timeout_v2.claims c ON c.execution_id=r.execution_id WHERE c.task_id={f.Task}").SingleAsync());
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_personal_form_v2.votes WHERE task_id={f.Task}").SingleAsync());
        Assert.Equal(0,(await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync()).Status);
    }
}
