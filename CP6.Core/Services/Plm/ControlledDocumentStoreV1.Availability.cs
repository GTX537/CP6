using System.Globalization;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Plm;

internal sealed partial class ControlledDocumentStoreV1
{
    private sealed class AvailabilityRow
    {
        public long StateVersion {get;init;} public Guid Etag {get;init;}
        public string State {get;init;}="";public string Action {get;init;}="";public string Reason {get;init;}="";
        public Guid ActorId {get;init;} public DateTimeOffset RecordedAt {get;init;} public Guid? OperationId {get;init;}
        public DocumentAvailabilityV1 View()=>new(StateVersion.ToString(CultureInfo.InvariantCulture),Etag,State,Action,Reason,ActorId,RecordedAt,OperationId);
    }
    private IQueryable<AvailabilityRow> AvailabilityRows(Guid id,long revision)=>db.Database.SqlQuery<AvailabilityRow>($"""
        SELECT state_version AS "StateVersion",etag AS "Etag",state AS "State",action AS "Action",reason AS "Reason",
          actor_id AS "ActorId",recorded_at AS "RecordedAt",operation_id AS "OperationId"
        FROM cp6_controlled_document_v1.availability WHERE tenant_id={db.CurrentTenantId} AND document_id={id} AND revision={revision}
        """);
    internal async Task<DocumentAvailabilityV1> Availability(Guid id,long revision,long? stateVersion,CancellationToken ct)
    {
        Transaction();var rows=AvailabilityRows(id,revision);if(stateVersion!=null)rows=rows.Where(x=>x.StateVersion==stateVersion);
        var value=await rows.OrderByDescending(x=>x.StateVersion).FirstOrDefaultAsync(ct)??throw Error("DOCUMENT_AVAILABILITY_UNAVAILABLE",503);
        return value.View();
    }
    internal async Task<DocumentAvailabilityV1[]> AvailabilityHistory(Guid id,long revision,long? before,CancellationToken ct)
    {
        Transaction();var rows=AvailabilityRows(id,revision);if(before!=null)rows=rows.Where(x=>x.StateVersion<before);
        return (await rows.OrderByDescending(x=>x.StateVersion).Take(20).ToArrayAsync(ct)).Select(x=>x.View()).ToArray();
    }
    internal async Task<DocumentAvailabilityV1> AppendAvailability(DocumentAvailabilityCommandV1 command,Guid actor,string state,CancellationToken ct)
    {
        Transaction();var previous=Revision(command.ExpectedStateVersion);if(previous==long.MaxValue)throw Error("DOCUMENT_AVAILABILITY_VERSION_EXHAUSTED",503);
        var revision=Revision(command.Document.Version);var next=previous+1;var etag=Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_controlled_document_v1.availability(tenant_id,document_id,revision,state_version,etag,state,action,reason,actor_id,operation_id)
            VALUES({db.CurrentTenantId},{command.Document.Id},{revision},{next},{etag},{state},{command.Action},{command.Reason},{actor},{command.OperationId})
            """,ct);
        return await Availability(command.Document.Id,revision,next,ct);
    }
}
