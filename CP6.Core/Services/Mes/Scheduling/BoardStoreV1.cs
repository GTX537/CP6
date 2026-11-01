using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Mes.Scheduling.SchedulingValuesV1;
namespace CP6.Core.Services.Mes.Scheduling;
internal sealed partial class SchedulingStoreV1
{
    internal async Task<BoardSourceV1[]> BoardSources(CancellationToken ct)=>(await db.Database.SqlQuery<byte[]>($"""
        SELECT body AS "Value" FROM cp6_scheduling_v1.board_sources WHERE tenant_id={db.CurrentTenantId} AND environment_id={environment}
        """).ToArrayAsync(ct)).Select(Decode<BoardSourceV1>).ToArray();
    internal Task SetBoardSource(BoardSourceV1 input,CancellationToken ct)=>db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO cp6_scheduling_v1.board_sources VALUES({db.CurrentTenantId},{environment},{input.RootId},{Bytes(input)})
        ON CONFLICT(tenant_id,environment_id,root_id) DO UPDATE SET body=EXCLUDED.body
        """,ct);
}
