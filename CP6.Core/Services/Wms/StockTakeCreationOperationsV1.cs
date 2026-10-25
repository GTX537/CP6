namespace CP6.Core.Services.Wms;

public sealed record StockTakeCreationDepartmentV1(Guid Id,string Code,string Name);
public sealed record StockTakeCreationScopeV1(Guid TenantId,Guid ActorId);
public sealed record StockTakeCreationContextV1(Guid TenantId,Guid ActorId,string AuthorityVersion,bool AllowsUnbound,
    StockTakeCreationDepartmentV1[] Departments);
public sealed record StockTakeCreationCommandV1
{
    public Guid OperationId {get;init;}
    public string ExpectedAuthorityVersion {get;init;}="";
    public Guid? DepartmentId {get;init;}
    public StockTakePlanDto Plan {get;init;}=new();
}
public sealed record StockTakeCreationResultV1(Guid OperationId,Guid StockTakeId,string StockTakeNo,Guid? DepartmentId);
public sealed record StockTakeCreationOperationV1(Guid OperationId,string Status,StockTakeCreationResultV1? Result=null);
public interface IStockTakeCreationOperationsV1
{
    Task<StockTakeCreationContextV1> CreationContextAsync(CancellationToken ct=default);
    Task<StockTakeCreationScopeV1> CreationScopeAsync(CancellationToken ct=default);
    Task<StockTakeCreationResultV1> CreateCommandAsync(StockTakeCreationCommandV1 request,CancellationToken ct=default);
    Task<StockTakeCreationOperationV1> ReadCreationAsync(Guid operation,CancellationToken ct=default);
    Task<StockTakeCreationOperationV1> CloseCreationAsync(Guid operation,CancellationToken ct=default);
}
