using System.Globalization;
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

// The existing movement remains the ledger fact. This monotonic number exposes its accepted Stock wire identity.
internal static class StockMovementSequenceV1
{
    internal static Task Record(CP6Context db, Guid pool, Guid movement, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO cp6_stock_return_v1.movement_sequences(tenant_id,pool_id,movement_id)
        VALUES({db.CurrentTenantId},{pool},{movement})
        """,ct);
    private sealed class Row
    {
        public Guid Pool { get; init; }
        public long Sequence { get; init; }
    }
    internal static async Task<ReturnStockLedgerV1[]> Entries(CP6Context db, Guid[] movements, CancellationToken ct)
    {
        var rows = await db.Database.SqlQuery<Row>($"""
            SELECT pool_id AS "Pool",sequence AS "Sequence" FROM cp6_stock_return_v1.movement_sequences
            WHERE tenant_id={db.CurrentTenantId} AND movement_id=ANY({movements}) ORDER BY sequence
            """).ToArrayAsync(ct);
        return rows.Select(x => new ReturnStockLedgerV1(x.Pool,x.Sequence.ToString(CultureInfo.InvariantCulture))).ToArray();
    }
}
