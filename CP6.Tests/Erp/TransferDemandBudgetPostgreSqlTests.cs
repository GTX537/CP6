using System.Data;
using CP6.Core.Services.Common;
using CP6.Core.Services.Wms.Transfer;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static CP6.Core.Services.Wms.Transfer.TransferDemandBudgetV1;

namespace CP6.Tests.Erp;

[Collection("TenantManagementPostgres")]
public sealed class TransferDemandBudgetPostgreSqlTests(TenantManagementPostgresFixture server)
{
    private static readonly TransferDemandBudgetV1.Space Simulation = new(Guid.NewGuid(), "SIMULATION", Guid.NewGuid());
    private static Ref Evidence(string owner) => new(owner, Guid.NewGuid(), "1", new string('a', 64));
    private static LocalDemand Demand(string budget = "local-request/line") => new(Guid.NewGuid(), budget, "1",
        Guid.NewGuid(), Guid.NewGuid(), Evidence("PRODUCT"), new("EA", "1"), Guid.NewGuid(), Guid.NewGuid(),
        new("PRODUCTION", "1"), [new(Guid.NewGuid(), Guid.NewGuid())], "10.00000000",
        [new("1000.00000000", "1010.00000000")], "OPEN", Evidence("WMS_TRANSFER"));
    private static BindingInput Bind(Ref demand, string from = "1000.00000000", string to = "1008.00000000") =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), demand, "8.00000000",
            [new("part", new("0.00000000", "8.00000000"), new(from, to))]);

    [CurrentPermissionPostgresFact]
    public async Task Transfer_budget_preserves_nonzero_coordinates_original_binding_and_terminal_release()
    {
        await using var db = server.Context(new TenantContext { CurrentTenantId = Guid.NewGuid() });
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var d = Demand(); var store = await Open(db, Simulation, [d.BudgetKey]);
        var source = await store.RegisterLocal(d, "0");
        var input = Bind(source.Ref); var first = await store.Bind(d.BudgetKey, input);
        Assert.Equal(first, await store.Bind(d.BudgetKey, input));
        Assert.Equal("SOURCE_BINDING_INPUT_CONFLICT", (await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.Bind(d.BudgetKey, input with { RequestId = Guid.NewGuid() }))).Message);
        Assert.Equal("SOURCE_BUDGET_CONFLICT", (await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.Bind(d.BudgetKey, Bind(source.Ref, "1002.00000000", "1010.00000000")))).Message);
        Assert.Equal("CLOSED", await store.Release(d.BudgetKey, input.Id, input.RequestId, input.RequestLineId));
        Assert.Equal(first, await store.Bind(d.BudgetKey, input)); // Original receipt, never reactivation.
        Assert.Equal("CLOSED", await store.ReadState(d.BudgetKey, input.Id));
        await store.Bind(d.BudgetKey, Bind(source.Ref, "1002.00000000", "1010.00000000"));
        await tx.CommitAsync();
    }

    [CurrentPermissionPostgresFact]
    public async Task Transfer_budget_requires_actual_transaction_and_all_declared_locks()
    {
        await using var db = server.Context(new TenantContext { CurrentTenantId = Guid.NewGuid() });
        await Assert.ThrowsAsync<InvalidOperationException>(() => Open(db, Simulation, ["a"]));
        await using var tx = await db.Database.BeginTransactionAsync();
        var store = await Open(db, Simulation, ["a"]);
        Assert.Equal("TRANSFER_BUDGET_LOCK_NOT_DECLARED", (await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.RegisterLocal(Demand("b"), "0"))).Message);
        await tx.RollbackAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RegisterLocal(Demand("a"), "0"));
    }

    [CurrentPermissionPostgresFact]
    public async Task Transfer_budget_source_successor_retains_used_coordinates_and_stopped_source_blocks_new_binding()
    {
        await using var db=server.Context(new TenantContext {CurrentTenantId=Guid.NewGuid()});
        await using var tx=await db.Database.BeginTransactionAsync();var d=Demand();var store=await Open(db,Simulation,[d.BudgetKey]);
        var one=await store.RegisterLocal(d,"0");await store.Bind(d.BudgetKey,Bind(one.Ref));
        Assert.Equal("TRANSFER_SOURCE_VERSION_CONFLICT",(await Assert.ThrowsAsync<InvalidOperationException>(()=>
            store.RegisterLocal(d with {State="STOPPED"},"0"))).Message);
        Assert.Equal("TRANSFER_SOURCE_STALE",(await Assert.ThrowsAsync<InvalidOperationException>(()=>
            store.RegisterLocal(d with {Version="2"},"0"))).Message);
        Assert.Equal("SOURCE_BUDGET_CONFLICT",(await Assert.ThrowsAsync<InvalidOperationException>(()=>
            store.RegisterLocal(d with {Version="2",AuthorizedRanges=[new("2000.00000000","2010.00000000")]},"1"))).Message);
        var two=await store.RegisterLocal(d with {Version="2"},"1");
        Assert.Equal(one,await store.RegisterLocal(d,"0")); // Original read does not move current head backwards.
        Assert.Equal("TRANSFER_SOURCE_STALE",(await Assert.ThrowsAsync<InvalidOperationException>(()=>store.Bind(d.BudgetKey,Bind(one.Ref)))).Message);
        Assert.Equal("SOURCE_BUDGET_CONFLICT",(await Assert.ThrowsAsync<InvalidOperationException>(()=>store.Bind(d.BudgetKey,Bind(two.Ref)))).Message);
        var stopped=await store.RegisterLocal(d with {Version="3",State="STOPPED"},"2");
        Assert.Equal("TRANSFER_SOURCE_NOT_OPEN",(await Assert.ThrowsAsync<InvalidOperationException>(()=>store.Bind(d.BudgetKey,Bind(stopped.Ref)))).Message);
        await tx.CommitAsync();
    }

    [CurrentPermissionPostgresFact]
    public async Task Transfer_budget_exact_quantity_and_split_mapping_reject_gaps_rounding_wrong_units_and_external_owner()
    {
        await using var db=server.Context(new TenantContext {CurrentTenantId=Guid.NewGuid()});
        await using var tx=await db.Database.BeginTransactionAsync();var d=Demand();var store=await Open(db,Simulation,[d.BudgetKey]);
        foreach(var q in new[]{"1","1.000000001","01.00000000","1e0","-0.00000000","10000000000000.00000000"})
            Assert.Equal("TRANSFER_QUANTITY_INVALID",(await Assert.ThrowsAsync<InvalidOperationException>(()=>store.RegisterLocal(d with {Quantity=q},"0"))).Message);
        var source=await store.RegisterLocal(d,"0");
        Assert.Equal("TRANSFER_SOURCE_IDENTITY_CONFLICT",(await Assert.ThrowsAsync<InvalidOperationException>(()=>
            store.RegisterLocal(d with {Version="2",BaseUomRef=new("KG","1")},"1"))).Message);
        var input=Bind(source.Ref) with {DemandSegments=[new("a",new("0.00000000","4.00000000"),new("1006.00000000","1010.00000000")),
            new("b",new("4.00000000","8.00000000"),new("1000.00000000","1004.00000000"))]};
        Assert.Equal("TRANSFER_LOCAL_SOURCE_REQUIRED",(await Assert.ThrowsAsync<InvalidOperationException>(()=>
            store.Bind(d.BudgetKey,input with {DemandRef=source.Ref with {Owner="PLANNING"}}))).Message);
        var gaps=input with {DemandSegments=[input.DemandSegments[0],new("b",new("5.00000000","9.00000000"),new("1000.00000000","1004.00000000"))]};
        Assert.Equal("TRANSFER_RANGE_INVALID",(await Assert.ThrowsAsync<InvalidOperationException>(()=>store.Bind(d.BudgetKey,gaps))).Message);
        var result=await store.Bind(d.BudgetKey,input);Assert.Contains("1006.00000000",result);
        Assert.Equal("TRANSFER_BINDING_OWNER_MISMATCH",(await Assert.ThrowsAsync<InvalidOperationException>(()=>
            store.Release(d.BudgetKey,input.Id,Guid.NewGuid(),input.RequestLineId))).Message);
        await tx.CommitAsync();
    }

    [CurrentPermissionPostgresFact]
    public async Task Transfer_budget_maximum_eight_place_quantity_survives_database_and_fresh_context_recovery()
    {
        var tenant=Guid.NewGuid();var d=Demand() with {Quantity="9999999999999.99999999",AuthorizedRanges=[new("0.00000000","9999999999999.99999999")]};
        BindingInput input;string original;
        await using(var db=server.Context(new TenantContext {CurrentTenantId=tenant}))
        {
            await using var tx=await db.Database.BeginTransactionAsync();var store=await Open(db,Simulation,[d.BudgetKey]);var source=await store.RegisterLocal(d,"0");
            input=Bind(source.Ref) with {BoundQuantity=d.Quantity,DemandSegments=[new("max",d.AuthorizedRanges[0],d.AuthorizedRanges[0])]};
            original=await store.Bind(d.BudgetKey,input);await tx.CommitAsync();
        }
        await using var next=server.Context(new TenantContext {CurrentTenantId=tenant});await using var nextTx=await next.Database.BeginTransactionAsync();
        var recovery=await Open(next,Simulation,[d.BudgetKey]);Assert.Equal(original,await recovery.Bind(d.BudgetKey,input));
        Assert.Contains("9999999999999.99999999",original);await nextTx.CommitAsync();
    }

    [CurrentPermissionPostgresFact]
    public async Task Transfer_budget_caller_rollback_removes_demand_binding_and_other_business_write_together()
    {
        var tenant=Guid.NewGuid();var d=Demand();Guid binding;
        await using(var db=server.Context(new TenantContext {CurrentTenantId=tenant}))
        {
            await using var tx=await db.Database.BeginTransactionAsync();var store=await Open(db,Simulation,[d.BudgetKey]);var source=await store.RegisterLocal(d,"0");
            var i=Bind(source.Ref);binding=i.Id;await store.Bind(d.BudgetKey,i);
            db.Sys_Tenants.Add(new(){Id=tenant,TenantCode=tenant.ToString("N"),TenantName="rollback sibling"});await db.SaveChangesAsync();
            await tx.RollbackAsync();
        }
        await using var next=server.Context(new TenantContext {CurrentTenantId=tenant});await using var nextTx=await next.Database.BeginTransactionAsync();
        var recovered=await Open(next,Simulation,[d.BudgetKey]);Assert.Null(await recovered.ReadState(d.BudgetKey,binding));
        Assert.False(await next.Sys_Tenants.AnyAsync(t=>t.Id==tenant));await recovered.RegisterLocal(d,"0");await nextTx.CommitAsync();
    }

    [CurrentPermissionPostgresFact]
    public async Task Transfer_budget_same_ids_are_isolated_by_tenant_environment_and_sandbox()
    {
        var tenant=Guid.NewGuid();var d=Demand();BindingInput input;
        await using(var db=server.Context(new TenantContext {CurrentTenantId=tenant}))
        {
            await using var tx=await db.Database.BeginTransactionAsync();var store=await Open(db,Simulation,[d.BudgetKey]);
            var source=await store.RegisterLocal(d,"0");input=Bind(source.Ref);await store.Bind(d.BudgetKey,input);await tx.CommitAsync();
        }
        foreach(var (otherTenant,space) in new[]{(Guid.NewGuid(),Simulation),(tenant,Simulation with {EnvironmentId=Guid.NewGuid()}),
            (tenant,Simulation with {SandboxScopeId=Guid.NewGuid()}),(tenant,new TransferDemandBudgetV1.Space(Simulation.EnvironmentId,"PRODUCTION",null))})
        {
            await using var db=server.Context(new TenantContext {CurrentTenantId=otherTenant});await using var tx=await db.Database.BeginTransactionAsync();
            var store=await Open(db,space,[d.BudgetKey]);Assert.Null(await store.ReadState(d.BudgetKey,input.Id));
            Assert.Equal("TRANSFER_SOURCE_NOT_FOUND",(await Assert.ThrowsAsync<InvalidOperationException>(()=>store.Bind(d.BudgetKey,input))).Message);
            await store.RegisterLocal(d,"0");await store.Bind(d.BudgetKey,input);await tx.CommitAsync();
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task Transfer_budget_competing_transactions_wait_then_reject_overlapping_new_request()
    {
        var tenant=Guid.NewGuid();var d=Demand();
        await using var first=server.Context(new TenantContext {CurrentTenantId=tenant});await using var tx=await first.Database.BeginTransactionAsync();
        var store=await Open(first,Simulation,[d.BudgetKey]);var source=await store.RegisterLocal(d,"0");await store.Bind(d.BudgetKey,Bind(source.Ref));
        var backend=new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var competitor=Task.Run(async()=>
        {
            await using var db=server.Context(new TenantContext {CurrentTenantId=tenant});await using var t=await db.Database.BeginTransactionAsync();
            backend.SetResult(((NpgsqlConnection)db.Database.GetDbConnection()).ProcessID);
            var next=await Open(db,Simulation,[d.BudgetKey]);
            return (await Assert.ThrowsAsync<InvalidOperationException>(()=>next.Bind(d.BudgetKey,Bind(source.Ref,"1002.00000000","1010.00000000")))).Message;
        });
        try
        {
            var pid=await backend.Task.WaitAsync(TimeSpan.FromSeconds(10));var blocked=false;
            for(var n=0;n<100 && !blocked;n++)
            {
                blocked=await first.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT FROM pg_locks WHERE pid={pid} AND locktype='advisory' AND NOT granted) AS \"Value\"").SingleAsync();
                if(!blocked)await Task.Delay(20);
            }
            Assert.True(blocked,"Contender must actually wait on the PostgreSQL transaction lock");Assert.False(competitor.IsCompleted);
        }
        finally { await tx.CommitAsync(); }
        Assert.Equal("SOURCE_BUDGET_CONFLICT",await competitor.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [CurrentPermissionPostgresFact]
    public async Task Transfer_budget_database_rejects_original_tampering_direct_overlap_and_terminal_reactivation()
    {
        var tenant=Guid.NewGuid();var d=Demand();BindingInput input;
        await using(var db=server.Context(new TenantContext {CurrentTenantId=tenant}))
        {
            await using var tx=await db.Database.BeginTransactionAsync();var store=await Open(db,Simulation,[d.BudgetKey]);
            input=Bind((await store.RegisterLocal(d,"0")).Ref);await store.Bind(d.BudgetKey,input);await tx.CommitAsync();
        }
        // Each expected SQL failure is rolled back completely; no continuation of an aborted transaction.
        await using(var db=server.Context(new TenantContext {CurrentTenantId=tenant}))
        {
            await using var tx=await db.Database.BeginTransactionAsync();
            var error=await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE cp6_transfer_local_v1.demand_versions SET body_bytes=decode('01','hex') WHERE tenant_id={tenant}
                """));Assert.Equal("23514",error.SqlState);await tx.RollbackAsync();
        }
        await using(var db=server.Context(new TenantContext {CurrentTenantId=tenant}))
        {
            await using var tx=await db.Database.BeginTransactionAsync();
            var error=await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_transfer_local_v1.bindings(environment_id,sandbox_id,tenant_id,budget_key,id,request_id,line_id,demand_version,
                  input_bytes,result_bytes,state,quantity,request_ranges,demand_ranges,created_revision)
                SELECT environment_id,sandbox_id,tenant_id,budget_key,{Guid.NewGuid()},{Guid.NewGuid()},{Guid.NewGuid()},demand_version,
                  input_bytes,result_bytes,state,quantity,request_ranges,demand_ranges,created_revision
                FROM cp6_transfer_local_v1.bindings WHERE tenant_id={tenant} AND id={input.Id}
                """));Assert.Equal("23514",error.SqlState);Assert.Contains("SOURCE_BUDGET_CONFLICT",error.Message);await tx.RollbackAsync();
        }
        await using(var db=server.Context(new TenantContext {CurrentTenantId=tenant}))
        {
            await using var tx=await db.Database.BeginTransactionAsync();var store=await Open(db,Simulation,[d.BudgetKey]);
            var error=await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE cp6_transfer_local_v1.bindings SET state='CLOSED',closed_revision=NULL WHERE tenant_id={tenant} AND id={input.Id}
                """));Assert.Equal("23514",error.SqlState);await tx.RollbackAsync();
        }
        await using(var db=server.Context(new TenantContext {CurrentTenantId=tenant}))
        {
            await using var tx=await db.Database.BeginTransactionAsync();var store=await Open(db,Simulation,[d.BudgetKey]);
            await store.Release(d.BudgetKey,input.Id,input.RequestId,input.RequestLineId);await tx.CommitAsync();
        }
        await using(var db=server.Context(new TenantContext {CurrentTenantId=tenant}))
        {
            await using var tx=await db.Database.BeginTransactionAsync();
            var error=await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE cp6_transfer_local_v1.bindings SET state='ACTIVE',closed_revision=NULL WHERE tenant_id={tenant} AND id={input.Id}
                """));Assert.Equal("23514",error.SqlState);Assert.Contains("TRANSFER_BINDING_TERMINAL",error.Message);await tx.RollbackAsync();
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task Transfer_budget_rejects_invalid_environment_and_reopening_incomplete_lock_plan()
    {
        await using var db=server.Context(new TenantContext {CurrentTenantId=Guid.NewGuid()});await using var tx=await db.Database.BeginTransactionAsync();
        foreach(var space in new[]{Simulation with {Mode="PRODUCTION"},Simulation with {SandboxScopeId=null},Simulation with {EnvironmentId=Guid.Empty}})
            Assert.Equal("TRANSFER_ENVIRONMENT_INVALID",(await Assert.ThrowsAsync<InvalidOperationException>(()=>Open(db,space,["a"])) ).Message);
        await Open(db,Simulation,["b","a","b"]);
        Assert.Equal("TRANSFER_LOCK_PLAN_ALREADY_OPEN",(await Assert.ThrowsAsync<InvalidOperationException>(()=>Open(db,Simulation,["c"]))).Message);
        await tx.RollbackAsync();
    }

    [CurrentPermissionPostgresFact]
    public async Task Transfer_budget_demand_and_binding_cannot_issue_conflicting_refs_with_same_owner_id()
    {
        var tenant=Guid.NewGuid();var d=Demand();SavedDemand source;
        await using(var db=server.Context(new TenantContext {CurrentTenantId=tenant}))
        {
            await using var tx=await db.Database.BeginTransactionAsync();var store=await Open(db,Simulation,[d.BudgetKey]);
            source=await store.RegisterLocal(d,"0");await tx.CommitAsync();
        }
        await using(var db=server.Context(new TenantContext {CurrentTenantId=tenant}))
        {
            await using var tx=await db.Database.BeginTransactionAsync();var store=await Open(db,Simulation,[d.BudgetKey]);
            var error=await Assert.ThrowsAsync<PostgresException>(()=>store.Bind(d.BudgetKey,Bind(source.Ref) with {Id=d.Id}));
            Assert.Equal("23505",error.SqlState);await tx.RollbackAsync();
        }
        await using(var db=server.Context(new TenantContext {CurrentTenantId=tenant}))
        {
            await using var tx=await db.Database.BeginTransactionAsync();var store=await Open(db,Simulation,[d.BudgetKey]);
            Assert.Null(await store.ReadState(d.BudgetKey,d.Id));await store.Bind(d.BudgetKey,Bind(source.Ref));await tx.CommitAsync();
        }
    }
}
