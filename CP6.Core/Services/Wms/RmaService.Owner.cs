using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public partial class RmaService
{
    private OperationsOwnerV1 Owner => _owner?.Owns(_db) == true ? _owner
        : throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE", 503);

    public Task<RmaCapabilitiesV1> GetCapabilitiesAsync() => !_db.Database.IsNpgsql()
        ? Task.FromResult(new RmaCapabilitiesV1(true, "LEGACY"))
        : Owner.ExecuteAsync(OperationsOwnerV1.Rma, "query", false, (_, _) =>
            Task.FromResult(new RmaCapabilitiesV1(false, "RMA_OWNER_ADOPTION_UNPROVEN")));

    public Task<List<RmaHeader>> SearchAsync(RmaSearchQuery query)
    {
        if (!_db.Database.IsNpgsql()) return SearchCoreAsync(query);
        var frozen = OperationsOwnerV1.Freeze(query);
        frozen.PageSize = Math.Clamp(frozen.PageSize, 1, 500);
        frozen.Page = Math.Clamp(frozen.Page, 1, int.MaxValue / frozen.PageSize);
        return Owner.ExecuteAsync(OperationsOwnerV1.Rma, "query", false, async (access, ct) =>
        {
            var rows = await SearchCoreAsync(frozen, access.Rmas(), ct);
            // The ordinary list needs customer reference only. Do not disclose free text or actor names.
            foreach (var row in rows)
            {
                row.CustomerName = null; row.OperatorCd = null; row.ReturnReason = null; row.Remarks = null;
                row.Creator = null; row.Modifier = null;
            }
            return rows;
        });
    }

    public Task<RmaDto?> GetAsync(string number) => !_db.Database.IsNpgsql() ? GetCoreAsync(number)
        : Owner.ExecuteAsync(OperationsOwnerV1.Rma, "query", false, async (access, ct) =>
        {
            var value = await GetCoreAsync(number, access.Rmas(), ct);
            if (value is not null) access.ProjectRma(value);
            return value;
        });

    // The old commands conflate application/authorization and locally manufacture receipt,
    // disposition and commercial effects. No PostgreSQL call may reach them without the
    // accepted RMA facts/adapters; a menu grant alone cannot establish those prerequisites.
    private Task<T> RejectUnadopted<T>(string action, string? number = null) =>
        Owner.ExecuteAsync<T>(OperationsOwnerV1.Rma, action, false, async (access, ct) =>
        {
            if (number is not null) await access.RequireRma(number, ct);
            throw new OrderPriceCorrectionException("RMA_OWNER_ADOPTION_UNPROVEN", 503);
        });

    public Task<string> CreateAsync(RmaDto dto, string? userName) => !_db.Database.IsNpgsql()
        ? CreateCoreAsync(dto, userName) : RejectUnadopted<string>("add");
    public Task ReceiveAsync(string number, string? userName) => !_db.Database.IsNpgsql()
        ? ReceiveCoreAsync(number, userName) : RejectUnadopted<bool>("receive", number);
    public Task StartInspectionAsync(string number, string? userName) => !_db.Database.IsNpgsql()
        ? StartInspectionCoreAsync(number, userName) : RejectUnadopted<bool>("inspect", number);
    public Task JudgeAndDisposeAsync(string number, List<RmaDispositionInput> inputs, string? userName) => !_db.Database.IsNpgsql()
        ? JudgeAndDisposeCoreAsync(number, inputs, userName) : RejectUnadopted<bool>("judge", number);
    public Task CloseAsync(string number, string? userName) => !_db.Database.IsNpgsql()
        ? CloseCoreAsync(number, userName) : RejectUnadopted<bool>("close", number);
    public Task CancelAsync(string number, string? userName) => !_db.Database.IsNpgsql()
        ? CancelCoreAsync(number, userName) : RejectUnadopted<bool>("cancel", number);
}
