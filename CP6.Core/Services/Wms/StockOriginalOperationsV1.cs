namespace CP6.Core.Services.Wms;

/// <summary>Versioned independent PostgreSQL stock commands. Legacy parent primitives are not recoverable commands.</summary>
public interface IStockOriginalOperationsV1
{
    Task<StockOperationResultV1> ApplyCommandAsync(StockApplyCommandV1 request,CancellationToken ct=default);
    Task<StockOperationResultV1> MoveCommandAsync(StockMoveCommandV1 request,CancellationToken ct=default);
    Task<StockOperationV1> ReadOperationAsync(Guid operation,CancellationToken ct=default);
    Task<StockOperationV1> CloseOperationAsync(Guid operation,CancellationToken ct=default);
    Task<StockOperationScopeV1> OperationScopeAsync(CancellationToken ct=default);
}
public sealed class StockApplyCommandV1 : StockMovementRequest { public Guid OperationId {get;set;} }
public sealed class StockMoveCommandV1 : StockMoveRequest { public Guid OperationId {get;set;} }
public sealed record StockOperationResultV1(Guid OperationId,string Kind,string? TxnNo=null,string? OutTxnNo=null,string? InTxnNo=null);
public sealed record StockOperationV1(Guid OperationId,string State,StockOperationResultV1? Result=null);
public sealed record StockOperationScopeV1(Guid TenantId,Guid ActorId);
