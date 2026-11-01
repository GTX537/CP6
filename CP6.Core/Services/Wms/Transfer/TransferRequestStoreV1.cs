using System.Globalization;
using System.Text.Json;
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms.Transfer;

internal sealed class TransferRequestHeadV1
{
    public Guid Id { get; init; }
    public Guid CreatorId { get; init; }
    public string DraftKey { get; init; } = "";
    public long Revision { get; init; }
    public byte[] Body { get; init; } = [];
}
internal sealed class TransferRequestStoreV1(CP6Context db)
{
    internal const string Owner = "Main.Wms.TransferRequest.v1", Contract = "Main.Wms.TransferRequest.commands.v1";
    internal static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    internal static byte[] Bytes<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Json);
    internal static string Digest<T>(T value) => Integration.MainOwnerOperationJournalV1.Digest(Bytes(value));
    internal static IQueryable<TransferRequestHeadV1> Heads(CP6Context db) => db.Database.SqlQuery<TransferRequestHeadV1>($"""
        SELECT id AS "Id",creator_id AS "CreatorId",draft_key AS "DraftKey",revision AS "Revision",body AS "Body"
        FROM cp6_transfer_request_v1.heads WHERE tenant_id={db.CurrentTenantId}
        """);
    internal static TransferRequestViewV1 View(TransferRequestHeadV1 row) => JsonSerializer.Deserialize<TransferRequestViewV1>(row.Body, Json)!;
    internal async Task<TransferRequestViewV1> Read(Guid id, CancellationToken ct)
        => View(await Heads(db).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw TransferEnvironmentStoreV1.Error("TRANSFER_REQUEST_NOT_FOUND", 404));
    internal async Task Save(TransferRequestViewV1 v, Guid operation, string action, CancellationToken ct)
    {
        var bytes = Bytes(v); var revision = long.Parse(v.Revision, CultureInfo.InvariantCulture); var version = long.Parse(v.Version, CultureInfo.InvariantCulture);
        if (revision == 1)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_transfer_request_v1.heads(tenant_id,id,draft_key,creator_id,revision,version,state,body)
                VALUES({db.CurrentTenantId},{v.Id},{v.DraftKey},{v.CreatorId},{revision},{version},{v.State},{bytes})
                """, ct);
        else
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE cp6_transfer_request_v1.heads SET revision={revision},version={version},state={v.State},body={bytes}
                WHERE tenant_id={db.CurrentTenantId} AND id={v.Id}
                """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_transfer_request_v1.versions(tenant_id,id,revision,version,body)
            VALUES({db.CurrentTenantId},{v.Id},{revision},{version},{bytes})
            """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_transfer_request_v1.events(tenant_id,event_id,request_id,revision,action,body)
            VALUES({db.CurrentTenantId},{operation},{v.Id},{revision},{action},{bytes})
            """, ct);
    }
}
