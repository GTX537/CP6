namespace CP6.Core.Services.Wms;

/// <summary>Explicit creation intent for an absent stock key. Existing v1 command bytes are unchanged.</summary>
public interface IStockCreationOperationsV2
{
    Task<StockCreationContextV2> CreationContextAsync(string action,StockCreationKeyV2 key,CancellationToken ct=default);
    Task<StockOperationResultV1> ApplyCommandAsync(StockApplyCommandV2 request,CancellationToken ct=default);
    Task<StockOperationResultV1> MoveCommandAsync(StockMoveCommandV2 request,CancellationToken ct=default);
}
public sealed record StockCreationKeyV2(string WarehouseCd,string LocationCd,string ProductCd,string LotNo);
public sealed record StockCreationIntentV2(string ExpectedAuthorityVersion,Guid? DepartmentId);
public sealed record StockCreationDepartmentV2(Guid Id,string Code,string Name);
public sealed record StockCreationContextV2(Guid TenantId,Guid ActorId,string AuthorityVersion,bool RequiresCreation,
    bool AllowsUnbound,StockCreationDepartmentV2[] Departments);
public sealed class StockApplyCommandV2 : StockMovementRequest
{
    public Guid OperationId {get;set;}
    public StockCreationIntentV2? NewStock {get;set;}
}
public sealed class StockMoveCommandV2 : StockMoveRequest
{
    public Guid OperationId {get;set;}
    public StockCreationIntentV2? NewStock {get;set;}
}
