using System.Globalization;
using System.Text.Json;
using CP6.Core.Services.ErpIntegration;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Erp.OrderSourceFacts;

namespace CP6.Core.Services.Erp;

internal sealed partial class OrderSourceParticipant
{
    private sealed record ControlAssessment(bool Blocked, OrderControlEvidenceGap? Gap);

    private sealed record SourceControl(OrderSourceScope Scope, long SourceVersion, string Owner, string ReasonId,
        long Version, string Disposition, string[] Actions, DateTimeOffset? EffectiveAt);

    private static readonly string[] EffectiveTimeFormats =
        ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"];

    private async Task<ControlAssessment> AssessControls(OrderSourceReceipt head, OrderSourceScope scope,
        OrderSourceAction? action, CancellationToken ct)
    {
        var originals = await db.Database.SqlQuery<Saved>($"""
            SELECT op.actor_id AS "ActorId",op.receipt AS "Result",op.body AS "Body",outbox.envelope AS "Envelope"
            FROM cp6_sales_order_source_v1.operations op
            JOIN cp6_sales_order_source_v1.outbox outbox USING (tenant_id,producer,operation_key)
            WHERE op.tenant_id={db.CurrentTenantId} AND op.lineage_id={head.LineageId}
              AND op.source_version<={head.SourceVersion}
            ORDER BY op.source_version
            """).ToArrayAsync(ct);

        var now = (clock ?? TimeProvider.System).GetUtcNow();
        var actionCode = action?.ToString().ToUpperInvariant();
        var prior = new List<SourceControl>();
        var blocked = false;
        OrderControlEvidenceGap? gap = originals.LongLength == head.SourceVersion ? null : OrderControlEvidenceGap.History;

        foreach (var original in originals)
        {
            List<SourceControl> controls;
            try
            {
                var receipt = original.Receipt;
                if (receipt.LineageId != head.LineageId || receipt.Scope.OrderId != head.Scope.OrderId
                    || receipt.Scope.LineId != head.Scope.LineId || receipt.Scope.ScheduleId != head.Scope.ScheduleId)
                {
                    gap = OrderControlEvidenceGap.History;
                    continue;
                }
                if (receipt.Handoff != "H32") continue;
                controls = ReadJson(original.Body, root =>
                {
                    var parsed = new List<SourceControl>();
                    foreach (var item in root.GetProperty("controls").EnumerateArray())
                    {
                        try { parsed.Add(ReadControl(item, receipt)); }
                        catch (Exception error) when (error is InvalidOperationException or KeyNotFoundException)
                        {
                            gap = OrderControlEvidenceGap.History;
                        }
                    }
                    return parsed;
                });
            }
            catch (OrderPriceCorrectionException)
            {
                gap = OrderControlEvidenceGap.History;
                continue;
            }

            foreach (var control in controls)
            {
                if (actionCode is not null && !control.Actions.Contains(actionCode, StringComparer.Ordinal)) continue;
                var relevant = action is not null || control.SourceVersion == head.SourceVersion;
                if (control.Scope != scope)
                {
                    if (relevant) gap = OrderControlEvidenceGap.Scope;
                    continue;
                }
                if (control.EffectiveAt is null)
                {
                    if (relevant) gap = OrderControlEvidenceGap.EffectiveTime;
                    continue;
                }

                if (control.Disposition != "RELEASE_OWN_REASON")
                {
                    prior.Add(control);
                    blocked |= action is not null && control.EffectiveAt <= now;
                    continue;
                }

                if (!relevant || action is not null && control.EffectiveAt > now) continue;
                // 原原因的业务坐标有明文合同。解除还要核对该原版的 OwnerRef 前驱。
                // 当前本仓缺少将前驱凭据绑定到原控制及其授权的提供者，保留原阻断。
                gap = prior.Any(originalBlock => SameOriginalReason(originalBlock, control))
                    ? OrderControlEvidenceGap.Predecessor
                    : OrderControlEvidenceGap.OriginalReason;
            }
        }
        return new(blocked, gap);
    }

    private static bool SameOriginalReason(SourceControl previous, SourceControl release) =>
        previous.Disposition == "BLOCK" && previous.Scope == release.Scope
        && previous.Owner == release.Owner && previous.ReasonId == release.ReasonId
        && previous.SourceVersion < release.SourceVersion && previous.Version < release.Version
        && previous.EffectiveAt <= release.EffectiveAt
        && previous.Actions.ToHashSet(StringComparer.Ordinal).SetEquals(release.Actions);

    private static SourceControl ReadControl(JsonElement control, OrderSourceReceipt receipt)
    {
        RequireControl(control, receipt.Scope, receipt.LineageId);
        _ = String(control, "controlId");
        RequireControlReference(control.GetProperty("authority"));
        var predecessor = control.GetProperty("predecessor");
        if (predecessor.ValueKind != JsonValueKind.Null) RequireControlReference(predecessor);
        var raw = String(control, "effectiveAt");
        DateTimeOffset? effective = DateTimeOffset.TryParseExact(raw, EffectiveTimeFormats, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value) ? value : null;
        return new(receipt.Scope, receipt.SourceVersion, String(control, "owner"), String(control, "reasonId"),
            Revision(control.GetProperty("controlVersion")), String(control, "disposition"),
            control.GetProperty("scope").GetProperty("actionSet").EnumerateArray().Select(a => a.GetString()!).ToArray(),
            effective);
    }

    private static void RequireControlReference(JsonElement reference)
    {
        RequireFields(reference, "owner", "contractId", "contractVersion", "evidenceId", "evidenceVersion", "contentDigest");
        foreach (var property in reference.EnumerateObject()) _ = String(reference, property.Name);
        if (!CommercialWireV1.IsDigest(String(reference, "contentDigest"))) throw Error("SCHEMA_INVALID", 400);
    }
}
