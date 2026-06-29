using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Space;
using CP6.Entity.DTOs.Space;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Space;

/// <summary>连接体 CRUD（构造只注 CP6Context；TenantId 由 SaveChanges 盖章，查询走全局过滤）。</summary>
public class ConnectorService : IConnectorService
{
    private readonly CP6Context _db;
    public ConnectorService(CP6Context db) => _db = db;

    public async Task<List<ConnectorView>> ListBySiteAsync(Guid siteId)
    {
        var conns = await _db.Space_Connectors.Where(c => c.SiteId == siteId).ToListAsync();
        var ids = conns.Select(c => c.Id).ToList();
        var stops = await _db.Space_ConnectorStops.Where(s => ids.Contains(s.ConnectorId)).ToListAsync();
        return conns.Select(c => new ConnectorView
        {
            Id = c.Id, ConnectorCode = c.ConnectorCode, ConnectorType = c.ConnectorType, Name = c.Name,
            Stops = stops.Where(s => s.ConnectorId == c.Id)
                         .Select(s => new ConnectorStopView { FloorId = s.FloorId, X = s.X, Y = s.Y }).ToList()
        }).ToList();
    }

    public async Task<Guid> CreateAsync(ConnectorDto d, string? user)
    {
        if (await _db.Space_Connectors.AnyAsync(c => c.SiteId == d.SiteId && c.ConnectorCode == d.ConnectorCode))
            throw new InvalidOperationException("E-SPACE-501");
        var e = new Space_Connector
        {
            Id = Guid.NewGuid(), SiteId = d.SiteId, ConnectorCode = d.ConnectorCode,
            ConnectorType = d.ConnectorType, Name = d.Name, Creator = user, CreateDate = DateTime.Now
        };
        _db.Space_Connectors.Add(e);
        await _db.SaveChangesAsync();
        return e.Id;
    }

    public async Task UpsertStopAsync(Guid connectorId, ConnectorStopDto d, string? user)
    {
        _ = await _db.Space_Connectors.FirstOrDefaultAsync(c => c.Id == connectorId)
            ?? throw new InvalidOperationException("E-SPACE-502");
        var s = await _db.Space_ConnectorStops.FirstOrDefaultAsync(x => x.ConnectorId == connectorId && x.FloorId == d.FloorId);
        if (s is null)
        {
            _db.Space_ConnectorStops.Add(new Space_ConnectorStop
            {
                Id = Guid.NewGuid(), ConnectorId = connectorId, FloorId = d.FloorId, X = d.X, Y = d.Y,
                Creator = user, CreateDate = DateTime.Now
            });
        }
        else { s.X = d.X; s.Y = d.Y; s.Modifier = user; s.ModifyDate = DateTime.Now; }
        await _db.SaveChangesAsync();
    }

    public async Task DeleteStopAsync(Guid connectorId, Guid floorId)
    {
        var s = await _db.Space_ConnectorStops.FirstOrDefaultAsync(x => x.ConnectorId == connectorId && x.FloorId == floorId);
        if (s is null) return;
        _db.Space_ConnectorStops.Remove(s);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        var conn = await _db.Space_Connectors.FirstOrDefaultAsync(c => c.Id == id);
        if (conn is null) return;
        var stops = await _db.Space_ConnectorStops.Where(s => s.ConnectorId == id).ToListAsync();
        _db.Space_ConnectorStops.RemoveRange(stops);
        _db.Space_Connectors.Remove(conn);
        await _db.SaveChangesAsync();
    }
}
