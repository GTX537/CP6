using CP6.Entity.DTOs;

namespace CP6.Core.Services.Erp;

public interface IOrderTraceService
{
    Task<OrderTraceDto?> GetAsync(string webOrderNo, CancellationToken ct = default);
}
