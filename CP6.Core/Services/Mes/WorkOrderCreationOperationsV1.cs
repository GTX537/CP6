using CP6.Entity.DTOs.Mes;

namespace CP6.Core.Services.Mes;

public sealed record WorkOrderCreationDepartmentV1(Guid Id,string Code,string Name);
public sealed record WorkOrderCreationScopeV1(Guid TenantId,Guid ActorId);
public sealed record WorkOrderCreationContextV1(Guid TenantId,Guid ActorId,string AuthorityVersion,bool AllowsUnbound,
    WorkOrderCreationDepartmentV1[] Departments);
public sealed record WorkOrderCreationCommandV1
{
    public Guid OperationId {get;init;}
    public string ExpectedAuthorityVersion {get;init;}="";
    public Guid? DepartmentId {get;init;}
    public WorkOrderDto WorkOrder {get;init;}=new();
}
public sealed record WorkOrderCreationResultV1(Guid OperationId,Guid WorkOrderId,string WorkOrderNo,Guid? DepartmentId);
public sealed record WorkOrderCreationOperationV1(Guid OperationId,string Status,WorkOrderCreationResultV1? Result=null);
public interface IWorkOrderCreationOperationsV1
{
    Task<WorkOrderCreationContextV1> CreationContextAsync(CancellationToken ct=default);
    Task<WorkOrderCreationScopeV1> CreationScopeAsync(CancellationToken ct=default);
    Task<WorkOrderCreationResultV1> CreateCommandAsync(WorkOrderCreationCommandV1 request,CancellationToken ct=default);
    Task<WorkOrderCreationOperationV1> ReadCreationAsync(Guid operation,CancellationToken ct=default);
    Task<WorkOrderCreationOperationV1> CloseCreationAsync(Guid operation,CancellationToken ct=default);
}
