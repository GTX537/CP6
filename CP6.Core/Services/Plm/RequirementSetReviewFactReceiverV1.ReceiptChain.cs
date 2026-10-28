using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using CP6.Core.Services.Oa;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

using CP6.Core.Services.Wms;
namespace CP6.Core.Services.Plm;

public sealed partial class RequirementSetReviewFactReceiverV1
{
    // Keep the receipt's canonical instant representation intact at the HTTP boundary.
    public static byte[] ApplicationResponseBytes(object value) => value is ApprovalApplicationResultV2
        or ApprovalReceiptChainPageV2 or ApprovalReceiptChainNotObservedV2
        ? ApprovalCanonicalV2.Bytes(value) : throw new ArgumentException("Unsupported application response.", nameof(value));
    private sealed record ChainSource(Guid Instance, Guid Submission, string Revision, string Unit);
    private sealed record ChainSnapshot(Guid Environment, Guid Tenant, Guid Principal, Guid Stream, long Maximum, long After, DateTimeOffset ExpiresAt);
    private sealed record ChainCursor(string SnapshotToken, long Last);
    public async Task<object> ReceiptChainAsync(Guid streamId, string afterRevision = "0", int limit = 20, string? cursor = null, CancellationToken ct = default)
    {
        if (limit is < 1 or > 100 || !long.TryParse(afterRevision, NumberStyles.None, CultureInfo.InvariantCulture, out var after)
            || after < 0 || afterRevision != after.ToString(CultureInfo.InvariantCulture)) throw Error("RECEIPT_CHAIN_INPUT_INVALID", 422);
        var protector = protection?.CreateProtector("CP6.RequirementSetReview.ApplicationReceipt.Chain.v1") ?? throw Error("RECEIPT_CURSOR_UNAVAILABLE");
        Entry(); await using var tx = await db.Database.BeginTransactionAsync(ct);
        var lease = await scope.AcquireAsync(ct); await Ready(ct); var auth = await Service(ct);
        var row = await db.Database.SqlQuery<ChainSource>($"""
            SELECT s.instance_id AS "Instance",f.submission_id AS "Submission",s.subject_revision AS "Revision",s.application_unit_id AS "Unit"
            FROM cp6_requirement_set_review_fact_v1.streams s JOIN cp6_requirement_set_review_fact_v1.facts f
              ON f.environment_id=s.environment_id AND f.tenant_id=s.tenant_id AND f.decision_id=s.decision_id
            WHERE s.environment_id={scope.EnvironmentId} AND s.tenant_id={db.CurrentTenantId} AND s.id={streamId}
            """).SingleOrDefaultAsync(ct);
        if (row == null)
        {
            var missing = new ApprovalReceiptChainNotObservedV2("NOT_OBSERVED", streamId, await Now(ct));
            await Final(lease, auth, null, null, false, ct); await tx.CommitAsync(ct); return missing;
        }
        var grant = await Subject(auth.Service, row.Submission, false, auth.Registration, ct);
        var store = new RequirementSetReviewApplicationStoreV1(db, scope); await store.Ready(ct);
        ChainSnapshot snapshot; string token; long last;
        if (cursor == null)
        {
            var maximum = await store.Latest(streamId, ct);
            snapshot = new(scope.EnvironmentId, db.CurrentTenantId, auth.Service.Principal, streamId,
                maximum == null ? 0 : long.Parse(maximum.ReceiptRevision, CultureInfo.InvariantCulture), after, (await Now(ct)).AddMinutes(15));
            token = protector.Protect(Json(snapshot)); last = after;
        }
        else
        {
            try
            {
                if (cursor.Length is < 1 or > 8192) throw new JsonException();
                var decoded = Parse<ChainCursor>(protector.Unprotect(cursor)); token = decoded.SnapshotToken;
                snapshot = Parse<ChainSnapshot>(protector.Unprotect(token)); last = decoded.Last;
                if (snapshot.Environment != scope.EnvironmentId || snapshot.Tenant != db.CurrentTenantId || snapshot.Principal != auth.Service.Principal
                    || snapshot.Stream != streamId || snapshot.After != after || last < after || last >= snapshot.Maximum || snapshot.ExpiresAt <= await Now(ct)) throw new JsonException();
            }
            catch (Exception e) when (e is CryptographicException or JsonException or ArgumentException or NullReferenceException)
            { throw Error("CURSOR_EXPIRED", 409); }
        }
        var items = await store.Page(streamId, last, snapshot.Maximum, limit, ct);
        var next = items.Length != 0 && long.Parse(items[^1].ReceiptRevision, CultureInfo.InvariantCulture) < snapshot.Maximum
            ? protector.Protect(Json(new ChainCursor(token, long.Parse(items[^1].ReceiptRevision, CultureInfo.InvariantCulture)))) : null;
        var result = new ApprovalReceiptChainPageV2("CHAIN", streamId, row.Instance, row.Revision, row.Unit,
            snapshot.Maximum.ToString(CultureInfo.InvariantCulture), items, next, token);
        await Final(lease, auth, row.Submission, grant, false, ct); await tx.CommitAsync(ct); return result;
    }
}
