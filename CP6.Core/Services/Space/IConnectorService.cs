using CP6.Entity.DTOs.Space;

namespace CP6.Core.Services.Space;

public interface IConnectorService
{
    Task<List<ConnectorView>> ListBySiteAsync(Guid siteId);
    Task<Guid> CreateAsync(ConnectorDto d, string? user);
    Task UpsertStopAsync(Guid connectorId, ConnectorStopDto d, string? user);
    Task DeleteStopAsync(Guid connectorId, Guid floorId);
    Task DeleteAsync(Guid id);
}
