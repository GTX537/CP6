using System.Globalization;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Plm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using static CP6.Core.Services.Mes.ResourceSourceStoreV1;

namespace CP6.Core.Services.Mes;

internal sealed class ResourceAssignmentStoreV1(CP6Context db)
{
    internal const string AssignmentOwner = "Main.Mes.ResourceAssignment.v1";
    internal const string Contract = "Main.Mes.ResourceAssignment.commands.v1";
    internal ResourceSourceStoreV1 Sources { get; } = new(db);
    private IDbContextTransaction? fenced;

    internal async Task Fence(bool write, CancellationToken ct)
    {
        await Sources.Fence(false, ct);
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE cp6_resource_assignment_v1.heads,cp6_resource_assignment_v1.versions"
            + (write ? " IN SHARE ROW EXCLUSIVE MODE" : " IN SHARE MODE"), ct);
        if (!await db.Database.SqlQueryRaw<bool>("SELECT cp6_resource_assignment_v1.ready() AS \"Value\"").SingleAsync(ct))
            throw Error("RESOURCE_ASSIGNMENT_STORE_UNAVAILABLE", 503);
        fenced = db.Database.CurrentTransaction;
    }

    private void RequireTransaction()
    {
        if (fenced == null || !ReferenceEquals(fenced, db.Database.CurrentTransaction))
            throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
    }

    private sealed class Row
    {
        public long Revision { get; init; }
        public long Current { get; init; }
        public Guid Actor { get; init; }
        public byte[] Body { get; init; } = [];
        public string Digest { get; init; } = "";
    }

    internal async Task<ResourceAssignmentVersionV1?> Read(Guid machineId, long? version, CancellationToken ct)
    {
        RequireTransaction();
        var row = await db.Database.SqlQuery<Row>($"""
            SELECT v.revision AS "Revision",h.revision AS "Current",v.actor_id AS "Actor",v.body AS "Body",v.digest AS "Digest"
            FROM cp6_resource_assignment_v1.heads h JOIN cp6_resource_assignment_v1.versions v USING(tenant_id,machine_id)
            WHERE h.tenant_id={db.CurrentTenantId} AND h.machine_id={machineId} AND v.revision=coalesce({version},h.revision)
            """).SingleOrDefaultAsync(ct);
        if (row == null) return null;
        ResourceAssignmentOriginalV1? original;
        try { original = JsonSerializer.Deserialize<ResourceAssignmentOriginalV1>(row.Body, EngineeringProductStoreV1.Json); }
        catch (JsonException) { throw Error("RESOURCE_ASSIGNMENT_ORIGINAL_UNAVAILABLE", 503); }
        var revision = row.Revision.ToString(CultureInfo.InvariantCulture);
        if (original == null || original.TenantId != db.CurrentTenantId || original.MachineId != machineId
            || original.Plan.Machine.Id != machineId || original.Version != revision || original.ActorId != row.Actor
            || EngineeringProductStoreV1.Digest(row.Body) != row.Digest)
            throw Error("RESOURCE_ASSIGNMENT_ORIGINAL_UNAVAILABLE", 503);
        return new(db.CurrentTenantId, new(AssignmentOwner, machineId, revision, row.Digest), row.Revision == row.Current,
            original.Plan, Encoding.UTF8.GetString(row.Body));
    }

    internal async Task<ResourceAssignmentVersionV1> Append(ResourceAssignmentCommandV1 input, ResourceAssignmentVersionV1? previous,
        Guid actor, CancellationToken ct)
    {
        RequireTransaction();
        if (previous != null && EngineeringProductStoreV1.Encode(previous.Plan).AsSpan()
            .SequenceEqual(EngineeringProductStoreV1.Encode(input.Plan))) return previous;
        var machineId = input.Plan.Machine.Id;
        var current = previous == null ? 0 : Revision(previous.Reference.Version);
        if (current == long.MaxValue) throw Error("RESOURCE_ASSIGNMENT_VERSION_EXHAUSTED", 503);
        var next = current + 1;
        var version = next.ToString(CultureInfo.InvariantCulture);
        var body = EngineeringProductStoreV1.Encode(new ResourceAssignmentOriginalV1(db.CurrentTenantId, machineId, version,
            actor, DateTimeOffset.UtcNow, input.Plan, input.Reason));
        var digest = EngineeringProductStoreV1.Digest(body);
        var machineVersion = Revision(input.Plan.Machine.Version);
        if (previous == null)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_resource_assignment_v1.heads(tenant_id,machine_id,revision) VALUES({db.CurrentTenantId},{machineId},{next})
                """, ct);
        else
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE cp6_resource_assignment_v1.heads SET revision={next}
                WHERE tenant_id={db.CurrentTenantId} AND machine_id={machineId}
                """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_resource_assignment_v1.versions(tenant_id,machine_id,revision,machine_revision,operation_id,actor_id,body,digest)
            VALUES({db.CurrentTenantId},{machineId},{next},{machineVersion},{input.OperationId},{actor},{body},{digest})
            """, ct);
        return new(db.CurrentTenantId, new(AssignmentOwner, machineId, version, digest), true, input.Plan, Encoding.UTF8.GetString(body));
    }
}
