using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;

namespace CP6.Core.Services.Oa;

public class DesignerService : IDesignerService
{
    private readonly CP6Context _db;
    private readonly IEnumerable<IServiceTaskExecutor> _execs;
    private readonly IEnumerable<IWfConnector> _connectors;
    public DesignerService(CP6Context db, IFlowDefService flowDef,
        IEnumerable<IServiceTaskExecutor> execs, IEnumerable<IWfConnector> connectors)
    {
        _db = db; _execs = execs; _connectors = connectors;
    }

    /// <summary>P1-6 服务目录：actions 只含 Kind==dataWriteback 且 VisibleInDesigner 的执行器
    /// （WebApiExecutor 被排除）；connectors 含全部注册连接器。每项 {name, label(DisplayName)}。</summary>
    public ServiceCatalog GetServiceCatalog() => new(
        _execs.Where(e => e.Kind == ServiceKind.DataWriteback && e.VisibleInDesigner)
              .Select(e => new ServiceCatalogItem(e.Key, e.DisplayName)).ToList(),
        _connectors.Select(c => new ServiceCatalogItem(c.Name, c.DisplayName)).ToList());

    public async Task<IReadOnlyList<FlowDefSummary>> ListAsync(string? functionId = null)
    {
        var q = _db.Wf_FlowDefs.AsQueryable();
        if (!string.IsNullOrWhiteSpace(functionId)) q = q.Where(d => d.FunctionId == functionId);
        return await q.OrderBy(d => d.FunctionId).ThenBy(d => d.FlowKey)
            .Select(d => new FlowDefSummary(d.FlowKey, d.FlowName, d.FormKey, d.FunctionId, d.FlowCode, d.Version, d.Enable))
            .ToListAsync();
    }

    public async Task<FlowDefSummary?> LoadAsync(string flowKey) =>
        await _db.Wf_FlowDefs.Where(d => d.FlowKey == flowKey)
            .Select(d => new FlowDefSummary(d.FlowKey, d.FlowName, d.FormKey, d.FunctionId, d.FlowCode, d.Version, d.Enable))
            .FirstOrDefaultAsync();

    public Task SaveAsync(SaveFlowRequest req, string? user) => LegacyDefinitionWrites.Rejected<object>();

    public Task CloneAsync(CloneRequest req, string? user) => LegacyDefinitionWrites.Rejected<object>();
}
