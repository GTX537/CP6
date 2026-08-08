using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public sealed class ApprovalPanelService : IApprovalPanelService
{
    private readonly CP6Context _db;
    private readonly IOaInstanceAccessService _access;
    private readonly IReadOnlyDictionary<string, IApprovalBusinessAccessAuthorizer> _authorizers;

    public ApprovalPanelService(
        CP6Context db, IOaInstanceAccessService access,
        IEnumerable<IApprovalBusinessAccessAuthorizer> authorizers)
    {
        _db = db;
        _access = access;
        _authorizers = authorizers.ToDictionary(x => x.BizType, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<ApprovalPanelDto> GetAsync(
        string bizType, string bizId, Guid actualUserId, Guid effectiveUserId,
        UserPermissionContext permission, CancellationToken ct = default)
    {
        if (!_authorizers.TryGetValue(bizType, out var authorizer))
            throw new UnauthorizedAccessException("E-WF-043");
        var business = await authorizer.AuthorizeAsync(bizId, permission, ct);

        var instance = await _db.Wf_FlowInstances.AsNoTracking()
            .Where(x => x.BizType == bizType && x.BizId == bizId)
            .OrderByDescending(x => x.CreateDate).ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);
        if (instance != null)
            await _access.GetAsync(actualUserId, effectiveUserId, instance.Id, ct);

        var binding = await _db.Wf_ApprovalBindings.AsNoTracking()
            .Where(x => x.BizType == bizType)
            .OrderByDescending(x => x.Enable).ThenByDescending(x => x.ModifyDate ?? x.CreateDate)
            .FirstOrDefaultAsync(ct);
        var route = RenderDetailRoute(binding?.DetailRoute, bizId);
        if (instance == null)
            return new(bizType, bizId, business.BusinessStatus, "none", null, null,
                Array.Empty<ApprovalPanelTimelineItem>(), business.CanSubmit, route);

        var task = await _db.Wf_FlowTasks.AsNoTracking()
            .Where(x => x.InstanceId == instance.Id && x.AssigneeId == effectiveUserId &&
                        x.Status == FlowTaskStatus.Pending)
            .OrderByDescending(x => x.CreateDate).FirstOrDefaultAsync(ct);
        var formTos = await _db.Wf_FlowFormTos.AsNoTracking()
            .Where(x => x.InstanceId == instance.Id)
            .OrderBy(x => x.StepSeq).ThenBy(x => x.SentAt).ToListAsync(ct);
        var userIds = formTos.SelectMany(x => new[]
            { x.ExpectedHandlerId, x.ActualHandlerId ?? Guid.Empty }).Where(x => x != Guid.Empty).Distinct().ToList();
        var names = await _db.Sys_Users.AsNoTracking().Where(x => userIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => string.IsNullOrWhiteSpace(x.NickName) ? x.UserName : x.NickName!, ct);
        string Name(Guid id) => names.GetValueOrDefault(id, id.ToString());

        var timeline = formTos.Select(x => new ApprovalPanelTimelineItem(
            x.StepSeq, x.NodeId, x.NodeName, Name(x.ExpectedHandlerId),
            x.ActualHandlerId is Guid actual ? Name(actual) : null,
            x.Status, x.Comment, x.SentAt, x.HandledAt)).ToList();
        var myTask = task == null ? null
            : new ApprovalPanelTask(task.Id, task.NodeId, new[] { "approve", "reject" });
        return new(bizType, bizId, business.BusinessStatus, Status(instance.Status),
            instance.Id, myTask, timeline,
            business.CanSubmit && instance.Status is not FlowInstanceStatus.Running and not FlowInstanceStatus.Suspended,
            route);
    }

    internal static string? RenderDetailRoute(string? template, string bizId)
    {
        if (string.IsNullOrWhiteSpace(template) || !template.StartsWith('/') ||
            template.StartsWith("//", StringComparison.Ordinal) ||
            Uri.TryCreate(template, UriKind.Absolute, out _))
            return null;
        var withoutPlaceholder = template.Replace("{bizId}", "", StringComparison.Ordinal);
        if (withoutPlaceholder.Contains('{') || withoutPlaceholder.Contains('}')) return null;
        return template.Replace("{bizId}", Uri.EscapeDataString(bizId), StringComparison.Ordinal);
    }

    private static string Status(int status) => status switch
    {
        FlowInstanceStatus.Running => "running",
        FlowInstanceStatus.Approved => "approved",
        FlowInstanceStatus.Rejected => "rejected",
        FlowInstanceStatus.Withdrawn => "withdrawn",
        FlowInstanceStatus.Suspended => "suspended",
        _ => "unknown",
    };
}
