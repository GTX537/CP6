using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Erp;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Plate_tool_source_HTTP_requires_current_original_owner()
    {
        await using var h = await Seed();
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        using var response = await host.Send(OperationsRequest(cookies, HttpMethod.Get, "/api/plate-molds/source-owner-v1/scope"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static async Task<OperationsOwnerV1> GrantPlateSource(Harness h)
    {
        const string key = "erp-plate-mold";
        var menu = await h.Db.Sys_Menus.SingleOrDefaultAsync(m => m.MenuId == 215);
        if (menu == null) h.Db.Sys_Menus.Add(new() { MenuId = 215, MenuKey = key, MenuName = key }); else menu.MenuKey = key;
        if (!await h.Db.Sys_MenuActions.AnyAsync(a => a.MenuId == 215 && a.ActionCode == "query"))
            h.Db.Sys_MenuActions.Add(new() { MenuId = 215, ActionCode = "query", ActionName = "query" });
        h.Db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = 215, ActionCode = "query" });
        h.Db.Sys_RoleDataScopes.Add(new() { RoleId = 72, ResourceKey = key, ScopeType = 5 });
        var catalog = new AuthorizationCatalogStoreV1(); catalog.RegisterScope(key,key,[1,2,3,4,5],5); catalog.RegisterFields(key,[]); catalog.Seal();
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        return new(h.Db,new OperationsHttp { HttpContext = h.Http },_ => Task.FromResult(h.User),catalog.Capture);
    }
    private static PlateMold NativePlate(string suffix = "A", int revision = 7) => new()
    {
        BaseCd = "BASE-FULL-1234567890", WdPtnNo = "123456789012345" + new string('N',24) + suffix, WdRev = revision,
        VrsnName = "Original tool", DecisionEstimateNo = "private-estimate", CustomerCd = "private-customer", SupplierCd = "private-supplier",
        RepresentativeProductCd = "FULL-PRODUCT-1234567", ProcessCd = "0300", StDate = new DateTime(2026,1,1), EndDate = new DateTime(2026,12,31),
        SheetWidth = 99999999999.9999m, SheetFlow = 0.0001m, BladeWidth = 1.2300m, BladeFlow = 1.0000m, FlexoThick = null,
        CylinderSize = 99999999999.9999m, CompositionQty = 3, MfgQty = 1, Creator = "price-owner", DecisionAmount = 1234.5678m,
        Status = 9, McTransferFlg = true, AtachInfoActual = true
    };
    private static async Task<Guid> SeedPlate(Harness h, string suffix = "A", int revision = 7)
    {
        var row = NativePlate(suffix, revision); row.WdPtnNo = h.Tenant.ToString("N")[..15] + new string('N',24) + suffix; h.Db.PlateMolds.Add(row); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear(); return row.Id;
    }
    [CurrentPermissionPostgresFact]
    public async Task Plate_tool_source_preserves_full_identity_precision_null_and_unknown_qualification()
    {
        await using var h = await Seed(); var authority = await GrantPlateSource(h);
        var a = await SeedPlate(h); var b = await SeedPlate(h,"B"); var c = await SeedPlate(h,"A",8);
        var service = new PlateMoldSourceOwnerV1(h.Db, authority); var rows = await service.ListAsync(h.Tenant.ToString("N")[..15]);
        Assert.Equal(3, rows.Length); Assert.Equal(3, rows.Select(r=>r.Reference.Id).Distinct().Count());
        var source = await service.ReadAsync(a);
        Assert.Equal(h.Tenant.ToString("N")[..15] + new string('N',24) + "A",source.Source.GetProperty("identity").GetProperty("number").GetString());
        Assert.Equal(NativePlate().BaseCd,source.Source.GetProperty("identity").GetProperty("baseCode").GetString());
        Assert.Equal(7,source.Source.GetProperty("identity").GetProperty("businessRevision").GetInt32());
        Assert.Equal("1",source.Reference.Version); Assert.True(source.IsCurrent);
        Assert.Equal("99999999999.9999",source.Source.GetProperty("technical").GetProperty("sheetWidth").GetString());
        Assert.Equal("0.0001",source.Source.GetProperty("technical").GetProperty("sheetFlow").GetString());
        Assert.Equal(JsonValueKind.Null,source.Source.GetProperty("technical").GetProperty("flexoThickness").ValueKind);
        Assert.Equal("UNKNOWN",source.TechnicalApproval); Assert.Equal("UNKNOWN",source.PhysicalQualification);
        Assert.DoesNotContain("private-",source.OriginalJson); Assert.DoesNotContain("1234.5678",source.OriginalJson);
        Assert.Equal(source.CurrentNativeRowVersion,source.ObservedNativeRowVersion);
        Assert.NotEmpty(Convert.FromBase64String(source.CurrentNativeRowVersion));
        Assert.Equal(404,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.ReadAsync(a,"2"))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Plate_tool_source_native_changes_capture_history_but_commercial_edits_only_change_native_token()
    {
        await using var h = await Seed(); var authority = await GrantPlateSource(h); var id = await SeedPlate(h);
        var service = new PlateMoldSourceOwnerV1(h.Db,authority); var first = await service.ReadAsync(id);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"T_PlateMold\" SET \"DecisionAmount\"=987.6543,\"SupplierCd\"='another-private' WHERE \"Id\"={id} AND \"TenantId\"={h.Tenant}");
        var commercial = await service.ReadAsync(id); Assert.Equal(first.Reference,commercial.Reference);
        Assert.Equal(first.ObservedNativeRowVersion,commercial.ObservedNativeRowVersion); Assert.NotEqual(first.CurrentNativeRowVersion,commercial.CurrentNativeRowVersion);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"T_PlateMold\" SET \"SheetWidth\"=2.3456,\"BaseCd\"='new-BASE',\"StDate\"='2026-02-01' WHERE \"Id\"={id} AND \"TenantId\"={h.Tenant}");
        var changed = await service.ReadAsync(id); Assert.Equal("2",changed.Reference.Version);
        Assert.Equal("new-BASE",changed.Source.GetProperty("identity").GetProperty("baseCode").GetString());
        var old = await service.ReadAsync(id,"1"); Assert.False(old.IsCurrent); Assert.Equal(first.OriginalJson,old.OriginalJson);
        Assert.Equal(first.ObservedNativeRowVersion,old.ObservedNativeRowVersion); Assert.Equal(changed.CurrentNativeRowVersion,old.CurrentNativeRowVersion);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"T_PlateMold\" SET \"IsDeleted\"=true WHERE \"Id\"={id} AND \"TenantId\"={h.Tenant}");
        Assert.Empty(await service.ListAsync()); Assert.Equal("3",(await service.ReadAsync(id)).Reference.Version);
    }
    [CurrentPermissionPostgresFact]
    public async Task Plate_tool_source_capture_rolls_back_with_native_transaction_and_serializes_competing_updates()
    {
        await using var h = await Seed(); var authority = await GrantPlateSource(h); var id = await SeedPlate(h);
        var service = new PlateMoldSourceOwnerV1(h.Db,authority); var first = await service.ReadAsync(id);
        await using (var tx = await h.Db.Database.BeginTransactionAsync())
        {
            await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"T_PlateMold\" SET \"SheetWidth\"=5 WHERE \"Id\"={id} AND \"TenantId\"={h.Tenant}");
            Assert.Equal(2,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_plate_mold_source_v1.versions WHERE tenant_id={h.Tenant} AND object_id={id}").SingleAsync());
            await tx.RollbackAsync();
        }
        Assert.Equal(first.Reference,(await service.ReadAsync(id)).Reference);
        async Task Update(decimal value)
        {
            await using var connection = new NpgsqlConnection(h.Db.Database.GetConnectionString()); await connection.OpenAsync();
            await using var cmd = new NpgsqlCommand("UPDATE public.\"T_PlateMold\" SET \"SheetWidth\"=@value WHERE \"TenantId\"=@tenant AND \"Id\"=@id",connection);
            cmd.Parameters.AddWithValue("value",value); cmd.Parameters.AddWithValue("tenant",h.Tenant); cmd.Parameters.AddWithValue("id",id); await cmd.ExecuteNonQueryAsync();
        }
        await Task.WhenAll(Update(6.0001m),Update(7.0002m));
        Assert.Equal("3",(await service.ReadAsync(id)).Reference.Version);
        var second = await service.ReadAsync(id,"2"); var third = await service.ReadAsync(id,"3");
        Assert.NotEqual(second.OriginalJson,third.OriginalJson);
    }
    [CurrentPermissionPostgresFact]
    public async Task Plate_tool_source_requires_current_query_creator_and_tenant_for_history()
    {
        await using var h = await Seed(); var authority = await GrantPlateSource(h); var id = await SeedPlate(h);
        var service = new PlateMoldSourceOwnerV1(h.Db,authority);
        await h.Db.Sys_RoleDataScopes.Where(r=>r.ResourceKey=="erp-plate-mold").ExecuteUpdateAsync(u=>u.SetProperty(r=>r.ScopeType,1));
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"T_PlateMold\" SET \"Creator\"='another' WHERE \"Id\"={id} AND \"TenantId\"={h.Tenant}");
        Assert.Empty(await service.ListAsync()); Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.ReadAsync(id,"1"))).Status);
        await using var foreign = await Seed(); var foreignAuthority = await GrantPlateSource(foreign);
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>new PlateMoldSourceOwnerV1(foreign.Db,foreignAuthority).ReadAsync(id,"1"))).Status);
        await h.Db.Sys_RoleActions.Where(r=>r.MenuId==215 && r.ActionCode=="query").ExecuteDeleteAsync();
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.ScopeAsync())).Status);
    }
    [TenantManagementPostgresTheory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task Plate_tool_source_IAM_uses_original_uuid_and_current_department_for_current_and_history(int scope)
    {
        await using var h = await Seed(); var authority = await GrantPlateSource(h); var id = await SeedPlate(h); await SeedPlate(h,"B");
        var admin = await MappingAdmin(h); await StandaloneScope(h,admin,scope); var service = new PlateMoldSourceOwnerV1(h.Db,authority);
        Assert.Empty(await service.ListAsync()); Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.ReadAsync(id,"1"))).Status);
        var choices = await StandaloneAdmin(h,admin).FindObjectsAsync("plate-mold",h.Tenant.ToString("N")[..15] + new string('N',24) + "A",_=>Task.FromResult(h.User),default);
        Assert.Equal(id,Assert.Single(choices).Id); Assert.Contains(NativePlate().BaseCd,choices[0].Name);
        await BindStandalone(h,admin,"plate-mold",id); Assert.Equal(id,Assert.Single(await service.ListAsync()).Reference.Id);
        Assert.Equal("1",(await service.ReadAsync(id,"1")).Reference.Version);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_operations_owner_v1.department_mappings SET enabled=false WHERE tenant_id={h.Tenant} AND object_kind='plate-mold' AND object_id={id}");
        Assert.Empty(await service.ListAsync()); Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.ReadAsync(id,"1"))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Plate_tool_source_internal_exact_read_requires_same_fence_original_authority_and_current_reference()
    {
        await using var h = await Seed(); var authority = await GrantPlateSource(h); var id = await SeedPlate(h);
        var service = new PlateMoldSourceOwnerV1(h.Db,authority); var source = await service.ReadAsync(id);
        Task<PlateMoldSourceVersionV1> Exact(PlateMoldSourceRefV1 expected) => authority.ExecuteAsync(OperationsOwnerV1.PlateMold,"query",false,async (a,ct)=>
        { var store = new PlateMoldSourceStoreV1(h.Db); await store.Fence(ct); return await store.CurrentExact(a,expected,ct); });
        Assert.Equal(source.Reference,(await Exact(source.Reference)).Reference);
        Assert.Equal(503,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>authority.ExecuteAsync(OperationsOwnerV1.PlateMold,"query",false,
            (a,ct)=>new PlateMoldSourceStoreV1(h.Db).Read(a,id,null,ct)))).Status);
        var machine = await GrantMachine(h);
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>machine.ExecuteAsync(OperationsOwnerV1.Machine,"query",false,async (a,ct)=>
        { var store = new PlateMoldSourceStoreV1(h.Db); await store.Fence(ct); return await store.CurrentExact(a,source.Reference,ct); }))).Status);
        await using var peer = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        Assert.Equal(503,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>new PlateMoldSourceOwnerV1(peer,authority).ReadAsync(id))).Status);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"T_PlateMold\" SET \"SheetWidth\"=3 WHERE \"Id\"={id} AND \"TenantId\"={h.Tenant}");
        Assert.Equal(412,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>Exact(source.Reference))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Plate_tool_source_immutable_originals_missing_capture_and_drift_fail_closed()
    {
        await using var h = await Seed(); var authority = await GrantPlateSource(h); var id = await SeedPlate(h);
        var service = new PlateMoldSourceOwnerV1(h.Db,authority);
        await Assert.ThrowsAsync<PostgresException>(()=>h.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cp6_plate_mold_source_v1.versions WHERE tenant_id={h.Tenant} AND object_id={id}"));
        await Assert.ThrowsAsync<PostgresException>(()=>h.Db.Database.ExecuteSqlRawAsync("TRUNCATE public.\"T_PlateMold\""));
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE public.\"T_PlateMold\" DISABLE TRIGGER plate_mold_source_capture");
        try
        {
            Assert.Equal(503,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.ReadAsync(id))).Status);
            await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"T_PlateMold\" SET \"SheetWidth\"=13 WHERE \"Id\"={id} AND \"TenantId\"={h.Tenant}");
        }
        finally { await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE public.\"T_PlateMold\" ENABLE TRIGGER plate_mold_source_capture"); }
        Assert.Equal(503,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.ReadAsync(id,"1"))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Plate_tool_source_hard_deleted_uuid_is_not_disclosed_or_reused()
    {
        await using var h = await Seed(); var authority = await GrantPlateSource(h); var id = await SeedPlate(h);
        var service = new PlateMoldSourceOwnerV1(h.Db,authority); await h.Db.PlateMolds.Where(p=>p.Id==id).ExecuteDeleteAsync();
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>service.ReadAsync(id,"1"))).Status);
        Assert.False(await h.Db.Database.SqlQuery<bool>($"SELECT source_present AS \"Value\" FROM cp6_plate_mold_source_v1.versions WHERE tenant_id={h.Tenant} AND object_id={id} AND revision=2").SingleAsync());
        var reused = NativePlate(); reused.Id=id; h.Db.PlateMolds.Add(reused);
        var error = await Assert.ThrowsAsync<DbUpdateException>(()=>h.Db.SaveChangesAsync()); Assert.Contains("TOOL_SOURCE_IDENTITY_REUSED",error.InnerException!.Message); h.Db.ChangeTracker.Clear();
    }
    [CurrentPermissionPostgresFact]
    public async Task Plate_tool_source_HTTP_current_history_no_store_and_catalog_without_automatic_query_grant()
    {
        await using var h = await Seed(); var authority = await GrantPlateSource(h); var id = await SeedPlate(h);
        CP6.WebApi.Seed.ErpPermissionSeed.EnsureSeeded(h.Db); h.Db.ChangeTracker.Clear();
        Assert.False(await h.Db.Sys_RoleActions.AnyAsync(r=>r.RoleId==1 && r.MenuId==215 && r.ActionCode=="query"));
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!); var cookies = await Login(host,h);
        using var read = await host.Send(OperationsRequest(cookies,HttpMethod.Get,$"/api/plate-molds/source-owner-v1/{id}?version=1"));
        Assert.Equal(HttpStatusCode.OK,read.StatusCode); Assert.True(read.Headers.CacheControl?.NoStore);
        var body = await read.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(id,body.GetProperty("data").GetProperty("reference").GetProperty("id").GetGuid());
        using var list = await host.Send(OperationsRequest(cookies,HttpMethod.Get,"/api/plate-molds/source-owner-v1")); Assert.Equal(HttpStatusCode.OK,list.StatusCode);
        await h.Db.Sys_RoleActions.Where(r=>r.MenuId==215 && r.ActionCode=="query").ExecuteDeleteAsync();
        using var denied = await host.Send(OperationsRequest(cookies,HttpMethod.Get,$"/api/plate-molds/source-owner-v1/{id}?version=1")); Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
    }
    [CurrentPermissionPostgresFact]
    public async Task Plate_tool_source_forward_migration_preserves_native_rows_and_grants_without_fabricating_history()
    {
        await using var h = await Seed();
        var settings = new NpgsqlConnectionStringBuilder(h.Db.Database.GetConnectionString()) { Database = "postgres" };
        var name = "plate_forward_"+Guid.NewGuid().ToString("N");
        await using(var root = new NpgsqlConnection(settings.ConnectionString))
        { await root.OpenAsync(); await using var command = new NpgsqlCommand("CREATE DATABASE "+name,root); await command.ExecuteNonQueryAsync(); }
        settings.Database=name; var database = new DatabaseOptions(DatabaseProvider.PostgreSql); var profile=DatabaseMigrationProfile.For(database,DatabaseContextKind.Core);
        var options=DatabaseContextOptions.Configure(new DbContextOptionsBuilder<CP6Context>(),database,settings.ConnectionString,profile.MigrationsAssembly,profile.HistoryTable,profile.HistorySchema);
        await using var db=new CP6Context(options.Options,new TenantContext()); await db.GetService<IMigrator>().MigrateAsync("20261026230000_UomTimeBasisV1");
        db.PlateMolds.Add(NativePlate()); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var before=await db.Database.SqlQueryRaw<string>("SELECT to_jsonb(p)::text AS \"Value\" FROM public.\"T_PlateMold\" p").SingleAsync(); var grants=await db.Sys_RoleActions.CountAsync();
        await db.Database.MigrateAsync();
        Assert.Equal(before,await db.Database.SqlQueryRaw<string>("SELECT to_jsonb(p)::text AS \"Value\" FROM public.\"T_PlateMold\" p").SingleAsync()); Assert.Equal(grants,await db.Sys_RoleActions.CountAsync());
        Assert.Equal(1,await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM cp6_plate_mold_source_v1.versions WHERE revision=1 AND source_present").SingleAsync());
    }
}
