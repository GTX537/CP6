using System.Data;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Sys;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Core.Services.Integration;

/// <summary>Current original-session authority for independent manufacturing, reporting, outbound and inbound operations.</summary>
public sealed class OperationsOwnerV1
{
    internal const string Mes = "mes-work-order", Outbound = "wms-outbound-order",
        Production = "mes-production-result", Inbound = "wms-inbound-order", Receipt = "wms-inbound-receipt",
        Stock = "wms-stock", StockTake = "wms-stocktake", WmsDashboard = "wms-dashboard", MesDashboard = "mes-dashboard", Defect = "mes-defect",
        StockQc = "wms-stock-qc", InboundQc = "wms-qc-inspection", Quality = "mes-quality-inspection", Machine = "mes-machine";
    internal const string Mapping = "Main.Operations.Owner.v1";
    private readonly CP6Context db;
    private readonly IHttpContextAccessor http;
    private readonly Func<CancellationToken,Task<Guid>> currentActor;
    private readonly Func<AuthorizationCatalogSnapshotV1> catalog;
    public OperationsOwnerV1(CP6Context db,IHttpContextAccessor http,Func<CancellationToken,Task<Guid>> currentActor)
        :this(db,http,currentActor,AuthorizationCatalogV1.Capture) { }
    internal OperationsOwnerV1(CP6Context db,IHttpContextAccessor http,Func<CancellationToken,Task<Guid>> currentActor,
        Func<AuthorizationCatalogSnapshotV1> catalog)
    {this.db=db;this.http=http;this.currentActor=currentActor;this.catalog=catalog;}
    internal bool Owns(CP6Context context)=>ReferenceEquals(db,context);
    internal static T Freeze<T>(T input)=>JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(input))!;
    internal sealed record Authority(string Resource,string Action,UserPermissionContext Actor,FieldPermissionSourceV1 Source,
        FieldPermissionDecisionV1 Fields,int Scope,string OrganizationVersion);

    internal async Task<Authority> AuthorityAsync(string resource,string action,CancellationToken ct)
    {
        var subject=await currentActor(ct);var definition=catalog();
        if(!definition.IsSealed || !definition.Scopes.TryGetValue(resource,out var scope))throw new CurrentPermissionUnavailableException();
        var source=await new CurrentFieldPermissionSourceReaderV1(db,http,()=>definition).ReadAsync(resource,ct);
        var expected=resource is Outbound or Inbound or Receipt or Stock?new[]{"UnitPrice"}:
            resource is Mes or Production or MesDashboard or Defect or StockQc or InboundQc or Quality or Machine?Array.Empty<string>():
            resource==WmsDashboard?new[]{"Amount"}:new[]{"Amount","UnitPrice"};
        if(!source.Fields.Order(StringComparer.Ordinal).SequenceEqual(expected))throw new CurrentPermissionUnavailableException();
        if(source.SubjectId!=subject)throw new OrderPriceCorrectionException("REAUTH_REQUIRED",401);
        var actor=await PostgreSqlPermissionReader.ReadAsync(db,subject,ct);
        if(string.IsNullOrWhiteSpace(actor.UserName) || !actor.RoleIds.Order().SequenceEqual(source.Roles.Select(r=>r.RoleId).Order()))
            throw new CurrentPermissionUnavailableException();
        var menu=resource=="order"?"erp-order":resource;
        var actions=await (from a in db.Sys_MenuActions.AsNoTracking() join m in db.Sys_Menus.AsNoTracking() on a.MenuId equals m.MenuId
            where m.Enable && m.MenuKey==menu select a.ActionCode).ToArrayAsync(ct);
        if(!actor.ActionKeys.Contains(menu+":"+action) || !actions.Contains(action,StringComparer.Ordinal))
            throw new OrderPriceCorrectionException("ACTION_FORBIDDEN",403);
        var type=actor.DataScopes.GetValueOrDefault(resource,scope.Default);
        if(!scope.Supports.Contains(type) || type is <1 or >5 || type is 2 or 3 && actor.DeptId==null)
            throw new OrderPriceCorrectionException("OWNER_SCOPE_MAPPING_UNAVAILABLE",503);
        var uses=new[]{FieldPermissionUseV1.Read,FieldPermissionUseV1.Write};
        var mappings=source.Fields.SelectMany(f=>uses.Select(use=>new FieldPermissionMappingV1(f,use,"/operations/"+resource+"/"+f,true,[])));
        var constraints=source.Fields.SelectMany(f=>uses.Select(use=>new FieldPermissionConstraintsV1(f,use,
            FieldPermissionCheckV1.Allowed,FieldPermissionCheckV1.Allowed,FieldPermissionCheckV1.Allowed,
            FieldPermissionCheckV1.NotApplicable,FieldPermissionCheckV1.NotApplicable,FieldPermissionCheckV1.Allowed,FieldPermissionCheckV1.Allowed)));
        var decision=source.Evaluate(source.Versions(Mapping),Mapping,mappings,constraints);
        if(!decision.IsAvailable)throw new CurrentPermissionUnavailableException();
        return new(resource,action,actor,source,decision,type,await StandaloneDepartmentScopeV1.Version(db,ct));
    }

    internal async Task<T> ExecuteAsync<T>(string resource,string action,bool write,
        Func<OperationsAccessV1,CancellationToken,Task<T>> body,CancellationToken ct=default, OperationsDelegationV1? delegation=null,
        Func<CancellationToken,Task<string>>? actionAtFence=null,bool readOnlyAuthorityFence=false)
    {
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct,http.HttpContext?.RequestAborted??CancellationToken.None);ct=linked.Token;
        if(resource is not (Mes or Outbound or Production or Inbound or Receipt or Stock or StockTake or WmsDashboard or MesDashboard or Defect or StockQc or InboundQc or Quality or Machine) || !db.Database.IsNpgsql() || db.Database.CurrentTransaction!=null
            || System.Transactions.Transaction.Current!=null || db.ChangeTracker.HasChanges()
            || readOnlyAuthorityFence && (write || delegation!=null || actionAtFence!=null)
            || db.ChangeTracker.Entries().Any(e=>IsBusiness(e.Entity.GetType().Name)))
            throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE",503);
        var original=db.ChangeTracker.Entries().Select(e=>e.Entity).ToHashSet(ReferenceEqualityComparer.Instance);
        IDbContextTransaction? tx=null;var committing=false;
        try
        {
            tx=await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted,ct);
            if(readOnlyAuthorityFence)
            {
                // Application read-only recovery still uses native READ COMMITTED row locking.
                if(!await db.Database.SqlQueryRaw<bool>("""
                    SELECT singleton AS "Value" FROM cp6_platform_admin_guard_v1.root WHERE singleton FOR SHARE
                    """).SingleAsync(ct))throw new CurrentPermissionUnavailableException();
            }
            else await AuthenticationCompletionGateV1.AcquireAsync(db,ct);
            await LockGraph(write,ct);
            if(delegation!=null && (!write || !ReferenceEquals(delegation.Context,db)
                || delegation.Tenant!=db.CurrentTenantId || !AllowsDelegation(resource,action,delegation)))
                throw new OrderPriceCorrectionException("DELEGATION_FORBIDDEN",403);
            // Original-result lookup occurs only after the same native authority/business fences.
            // Only the assembly-internal caller selects query for an existing immutable operation.
            var selectedAction=actionAtFence==null?action:await actionAtFence(ct);
            var authority=await AuthorityAsync(delegation?.Resource??resource,delegation?.Action??selectedAction,ct);
            var access=new OperationsAccessV1(db,this,authority);
            if(delegation!=null)
            {
                if(authority.Source.SubjectId!=delegation.Subject || authority.Source.SourcePermissionVersion!=delegation.PermissionVersion)
                    throw new OrderPriceCorrectionException("AUTHORITY_CHANGED",403);
                await access.RequireDelegationParent(delegation,ct);
                access.AtCommit(t=>access.RequireDelegationParent(delegation,t));
            }
            var result=await body(access,ct);
            await access.ValidateFinalAsync(ct);
            foreach(var initial in access.Authorities)
            {
                var final=await AuthorityAsync(initial.Resource,initial.Action,ct);
                if(final.Source.SubjectId!=initial.Source.SubjectId || final.Source.SourcePermissionVersion!=initial.Source.SourcePermissionVersion
                    || final.Source.RegistryVersion!=initial.Source.RegistryVersion || final.Scope!=initial.Scope
                    || final.OrganizationVersion!=initial.OrganizationVersion)
                    throw new OrderPriceCorrectionException("AUTHORITY_CHANGED",403);
                if(final.Source.TenantExpiresAt<=DateTime.Now)throw new OrderPriceCorrectionException("AUTHORITY_CHANGED",403);
            }
            if(await currentActor(ct)!=authority.Source.SubjectId)throw new OrderPriceCorrectionException("REAUTH_REQUIRED",401);
            ct.ThrowIfCancellationRequested();committing=true;await tx.CommitAsync(ct);return result;
        }
        catch(Exception error)
        {
            Exception? rollback=null;
            if(tx!=null)try{await tx.RollbackAsync(CancellationToken.None);}catch(Exception failure){rollback=failure;}
            if(write && (committing || rollback!=null))throw new OrderPriceCorrectionException("OPERATIONS_OUTCOME_UNKNOWN",503,rollback==null?error:new AggregateException(error,rollback));
            if(error is OrderPriceCorrectionException or DbUpdateConcurrencyException)throw;
            if(error is OperationCanceledException && ct.IsCancellationRequested)throw;
            if(error is InvalidOperationException && error is not CurrentPermissionUnavailableException)throw;
            throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE",503,error);
        }
        finally
        {
            foreach(var entry in db.ChangeTracker.Entries().ToArray())if(!original.Contains(entry.Entity))entry.State=EntityState.Detached;
            if(tx!=null)await tx.DisposeAsync();
        }
    }
    private static readonly string[] BusinessTables=["Order","OrderDetail","OrderProcess","OrderMaterial","OrderProcessNote",
        "WorkOrder","WorkOrderProcess","WorkOrderMaterial","OutboundOrder","OutboundOrderDetail","Stock","StockTransaction",
        "WmsSequence","MesSequence","ShippingPackage","MaterialShortage","ProductionResult",
        "InboundOrder","InboundOrderDetail","InboundReceipt","InboundReceiptDetail","StockTake","StockTakeDetail","DefectRecord","QualityInspection","QualityInspectionItem","QcInspection","QcInspectionItem","Machine","MachineDowntime"];
    private static bool AllowsDelegation(string resource,string action,OperationsDelegationV1 ticket)
        => action=="add" && (ticket.Resource=="order" && ticket.Action=="add" && resource is Mes or Outbound
            || ticket.Resource==Mes && ticket.Action=="issue" && resource==Outbound)
        || ticket.Resource==Production && ticket.Action=="complete" && ticket.CompletionResultId!=null
            && (resource==Receipt && action=="post" || resource==Production && action=="backflush");
    private static bool IsBusiness(string name)=>BusinessTables.Contains(name);
    private async Task LockGraph(bool write,CancellationToken ct)
    {
        // Legacy soft references and stock writers do not yet share one row-lock protocol.
        // Bounded table fences include raw SQL writers; never perform external I/O inside this transaction.
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '10s'",ct);
        string Name(string name)
        {
            var entity=db.Model.GetEntityTypes().Single(e=>e.ClrType.Name==name);
            return db.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!,entity.GetSchema()??"public");
        }
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE "+string.Join(",",BusinessTables.Select(Name).Distinct().Order(StringComparer.Ordinal))
            +(write?" IN SHARE ROW EXCLUSIVE MODE":" IN SHARE MODE"),ct);
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE "+string.Join(",",new[]{"WmsRoleScope","Location","Warehouse","OutboundRoutingRule","ProductProcess","ProductMaterial","ProductMaster","MasterGenericCode","DefectCategory","InspectionTemplate"}
            .Select(Name).Distinct().Order(StringComparer.Ordinal))+" IN SHARE MODE",ct);
    }
}
