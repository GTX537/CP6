using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;
public sealed partial class NativeInboxOwnerV2
{
    private sealed record ApplicationProjection(byte[]? Payload, bool PendingGap);
    private async Task<NativeBusinessReadV2> QueryApplication(Member member, NativeBusinessReadV2 business, CancellationToken ct)
    {
        // Called only after current business read authorization. TxR owns chain/hash validation;
        // read its continuous projection, never an out-of-order inbox event or decision ACK.
        var projection = await db.Database.SqlQuery<ApplicationProjection>($"""
            SELECT i.payload AS "Payload",EXISTS(SELECT FROM cp6_approval_receipt_v2.gaps g
              WHERE g.tenant_id=p.tenant_id AND g.environment_id=p.environment_id AND g.stream_id=p.id AND NOT g.completed) AS "PendingGap"
            FROM cp6_approval_receipt_v2.streams p LEFT JOIN cp6_approval_receipt_v2.inbox i
              ON i.tenant_id=p.tenant_id AND i.environment_id=p.environment_id AND i.receipt_id=p.latest_receipt_id
            WHERE p.tenant_id={db.CurrentTenantId} AND p.environment_id={fence.EnvironmentId} AND p.owner={business.Owner}
              AND p.submission_id={business.SubmissionId} AND p.instance_id={member.Root} AND p.subject_revision={business.BusinessRevision}
            """).SingleOrDefaultAsync(ct);
        if (projection?.Payload == null) return business with { PendingReceiptGap = projection?.PendingGap ?? false };
        var receipt = System.Text.Json.JsonSerializer.Deserialize<ApprovalApplicationReceiptV2>(projection.Payload, Json)!;
        return business with { ApplicationState = receipt.Application, ApplicationReceiptId = receipt.ReceiptId,
            ReceiptRevision = receipt.ReceiptRevision, BusinessResultRef = receipt.BusinessResultRef, PendingReceiptGap = projection.PendingGap };
    }
    private sealed record QueryPin(Guid Instance, string FlowName, int FlowNumber, string? FormKey, int? FormNumber,
        string? FormName, DateTimeOffset Submitted, string TimeSource, DateTimeOffset Available);
    private IQueryable<QueryPin> QueryPins() => db.Database.SqlQuery<QueryPin>($"""
        SELECT x.instance_id AS "Instance",v.snapshot_json::jsonb->'fields'->>'Name' AS "FlowName",v.version_number AS "FlowNumber",
          f."FormKey",f."FormVersion" AS "FormNumber",fv.snapshot_json::jsonb->'fields'->>'Name' AS "FormName",
          s.submitted_at AS "Submitted",'FORM_SUBMISSION'::text AS "TimeSource",x.query_available_at AS "Available"
        FROM cp6_personal_form_v2.instances x JOIN cp6_personal_form_v2.submissions s ON s.form_data_id=x.form_data_id
        JOIN public."Wf_FormData" f ON f."TenantId"=s.tenant_id AND f."Id"=s.form_data_id
        JOIN cp6_workflow_authoring_v2.versions v ON v.tenant_id=x.tenant_id AND v.environment_id=x.environment_id AND v.id=x.flow_version
        JOIN cp6_form_authoring_v2.versions fv ON fv.tenant_id=s.tenant_id AND fv.environment_id=s.environment_id AND fv.id=s.form_version
        WHERE x.tenant_id={db.CurrentTenantId} AND x.environment_id={fence.EnvironmentId}
        UNION ALL
        SELECT x.instance_id,v.snapshot_json::jsonb->'fields'->>'Name',v.version_number,NULL::text,NULL::integer,NULL::text,s.received_at,'APP_INTAKE',coalesce(c.query_available_at,r.query_available_at)
        FROM cp6_approval_runtime_v2.instances x JOIN cp6_approval_intake_v2.submissions s
          ON s.tenant_id=x.tenant_id AND s.environment_id=x.environment_id AND s.owner=x.owner AND s.id=x.submission_id
        JOIN cp6_workflow_authoring_v2.versions v ON v.tenant_id=x.tenant_id AND v.environment_id=x.environment_id AND v.id=x.flow_version
        JOIN cp6_approval_start_v2.starts r ON r.tenant_id=x.tenant_id AND r.environment_id=x.environment_id AND r.instance_id=x.root_instance_id
        LEFT JOIN cp6_approval_runtime_v2.children c ON c.tenant_id=x.tenant_id AND c.environment_id=x.environment_id AND c.instance_id=x.instance_id
        WHERE x.tenant_id={db.CurrentTenantId} AND x.environment_id={fence.EnvironmentId}
        """);
    private async Task<string?> TenantTimeZone(CancellationToken ct) => await db.Sys_Tenants.AsNoTracking().Where(t => t.Id == db.CurrentTenantId).Select(t => t.TimeZoneId).SingleAsync(ct);
    public async Task<NativeQueryContextV2> QueryContextAsync(CancellationToken ct = default)
    {
        forms.Entry(); await using var tx = await db.Database.BeginTransactionAsync(ct); var (actor, lease) = await Begin(null, ct, "oa-form-search");
        var id = await TenantTimeZone(ct); _ = NativeQueryDatesV2.Zone(id);
        await FinalRead(actor, lease, ct); return new(id!);
    }
    private static void QueryShape(NativeFormQueryV2 q)
    {
        if (q.Page < 1 || q.Page > 100000 || q.PageSize < 1 || q.PageSize > 100 || q.FormVersion <= 0 || q.Status is < 0 or > 5
            || q.StarterId == Guid.Empty || q.HandlerId == Guid.Empty || q.FlowKey?.Length > 100 || q.FormKey?.Length > 100
            || q.Keyword?.Length > 200 || q.Page > 1 && q.SnapshotAsOf == null) throw Error("QUERY_FILTER_INVALID", 422);
    }
    private static bool Within(DateTimeOffset at, NativeQueryDateWindowV2 window)
        => (!window.FromInclusiveUtc.HasValue || at >= window.FromInclusiveUtc) && (!window.ToExclusiveUtc.HasValue || at < window.ToExclusiveUtc);
    private async Task<List<NativeFormQueryItemV2>> QueryRows(Dataset data, CancellationToken ct)
    {
        var ids = data.Instances.Select(i => i.Id).ToArray();
        var pins = await QueryPins().Where(p => ids.Contains(p.Instance)).ToDictionaryAsync(p => p.Instance, ct);
        var applications = new Dictionary<Guid, NativeBusinessReadV2>();
        foreach (var root in data.Members.GroupBy(m => m.Root))
            if (data.Content[root.Key].Business is { } business) applications[root.Key] = await QueryApplication(root.First(), business, ct);
        return data.Instances.Select(i =>
        {
            var member = data.Members.Single(m => m.InstanceId == i.Id); var content = data.Content[member.Root]; var pin = pins[i.Id];
            return new NativeFormQueryItemV2(i.Id, content.Form?.FormDataId, i.Id, pin.FormKey, pin.FormNumber,
                content.Form?.FormVersionId, pin.FormName, i.FlowKey, pin.FlowNumber, member.FlowVersion, pin.FlowName,
                content.Form?.Title, i.StarterId, pin.Submitted, i.Status, "SubmittedWithInstance", applications.GetValueOrDefault(member.Root),
                "PINNED_PUBLICATION", pin.TimeSource, "/api/oa/v2/query/instances/" + i.Id.ToString("D"), pin.Available);
        }).ToList();
    }
    public async Task<NativeFormQueryPageV2> SearchAsync(NativeFormQueryV2 q, Guid? behalf = null, CancellationToken ct = default)
    {
        QueryShape(q); forms.Entry(); await using var tx = await db.Database.BeginTransactionAsync(ct);
        var (actor, lease) = await Begin(behalf, ct, "oa-form-search");
        var now = await db.Database.SqlQueryRaw<DateTimeOffset>("SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
        var asOf = q.SnapshotAsOf ?? now; if (asOf > now) throw Error("QUERY_SNAPSHOT_INVALID", 422);
        NativeQueryDateWindowV2 window = new(null, null), submitted = new(null, null); string? zoneId = null;
        if (q.From != null || q.To != null || q.SubmittedFrom != null || q.SubmittedTo != null)
        {
            zoneId = await TenantTimeZone(ct); var zone = NativeQueryDatesV2.Zone(zoneId);
            if (q.TimeZoneId != zoneId) throw Error("QUERY_TIMEZONE_CHANGED", 409);
            window = NativeQueryDatesV2.Window(zone, q.From, q.To); submitted = NativeQueryDatesV2.Window(zone, q.SubmittedFrom, q.SubmittedTo);
        }
        var data = await Load(actor, ct); var rows = await QueryRows(data, ct);
        // TASK delegation covers participating instances, never another user's unbound personal submission.
        if (!actor.Delegated)
            foreach (var row in await forms.Unbound(actor.UserId).ToArrayAsync(ct))
                try { rows.Add((await forms.UnboundHistory(row, actor.Actual, ct)).Item); }
                catch (CP6.Core.Services.Sys.ScopeAuthorizationExceptionV1 e) when (e.Status is 403 or 404) { }
        var matches = rows.Where(r => r.QueryAvailableAtUtc <= asOf && Within(r.SubmittedAtUtc, window) && Within(r.SubmittedAtUtc, submitted)
            && (!q.StarterId.HasValue || r.StarterRef == q.StarterId) && (!q.Status.HasValue || r.InstanceStatus == q.Status)
            && (q.FlowKey == null || r.FlowKey == q.FlowKey) && (q.FormKey == null || r.FormKey == q.FormKey)
            && (!q.FormVersion.HasValue || r.FormVersion == q.FormVersion) && (!q.HasInstance.HasValue || r.InstanceId.HasValue == q.HasInstance)
            && (!q.HandlerId.HasValue || r.InstanceId.HasValue && (data.Tasks.Any(t => t.InstanceId == r.InstanceId && t.AssigneeId == q.HandlerId)
                || data.Votes.Any(v => v.InstanceId == r.InstanceId && (v.ActualUserId == q.HandlerId || v.PrincipalId == q.HandlerId))))
            && (string.IsNullOrEmpty(q.Keyword) || r.FlowKey?.Contains(q.Keyword, StringComparison.OrdinalIgnoreCase) == true
                || r.InstanceId.HasValue && data.Instances.Single(i => i.Id == r.InstanceId).BizId?.Contains(q.Keyword, StringComparison.OrdinalIgnoreCase) == true))
            .OrderByDescending(r => r.SubmittedAtUtc).ThenByDescending(r => r.Id.ToString("D"), StringComparer.Ordinal).ToArray();
        await FinalRead(actor, lease, ct);
        return new(matches.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToArray(), matches.Length, q.Page, q.PageSize,
            asOf, zoneId, window, submitted, actor.UserId, actor.EffectiveId);
    }
    public async Task<NativeFormQueryDetailV2> QueryInstanceAsync(Guid id, Guid? behalf = null, CancellationToken ct = default)
    {
        forms.Entry(); await using var tx = await db.Database.BeginTransactionAsync(ct); var (actor, lease) = await Begin(behalf, ct, "oa-form-search");
        var (member, content) = await Visible(id, actor, ct);
        var instance = await db.Wf_FlowInstances.AsNoTracking().SingleAsync(i => i.Id == id, ct);
        var row = (await QueryRows(new([member], [instance], [], [], new() { [member.Root] = content }, [], []), ct)).Single();
        var detail = (await Detail(id, member, content, actor, lease, ct)) with { Business = row.ApplicationSummary };
        await FinalRead(actor, lease, ct); return new(row, content.Form, detail);
    }
    public async Task<NativeFormQueryDetailV2> QueryFormDataAsync(Guid id, Guid? behalf = null, CancellationToken ct = default)
    {
        forms.Entry(); await using var tx = await db.Database.BeginTransactionAsync(ct); var (actor, lease) = await Begin(behalf, ct, "oa-form-search");
        if (!actor.Delegated)
        {
            var unbound = await forms.Unbound(actor.UserId).SingleOrDefaultAsync(r => r.Data == id, ct);
            if (unbound != null)
            {
                var view = await forms.UnboundHistory(unbound, actor.Actual, ct); await FinalRead(actor, lease, ct);
                return new(view.Item, view.Content, null);
            }
        }
        var data = await Load(actor, ct);
        var item = (await QueryRows(data, ct)).Where(r => r.FormDataId == id).OrderBy(r => r.Id).FirstOrDefault() ?? throw Error("RESOURCE_NOT_FOUND", 404);
        var member = data.Members.Single(m => m.InstanceId == item.InstanceId); var content = data.Content[member.Root];
        var detail = await Detail(member.InstanceId, member, content, actor, lease, ct);
        await FinalRead(actor, lease, ct); return new(item, content.Form, detail);
    }
}
