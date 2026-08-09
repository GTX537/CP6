using System.Text.Json;
using CP6.Core.Services.Wf;

namespace CP6.Core.Services.Oa;

public sealed record DraftListItem(
    Guid Id, string FormKey, string FormName, int FormVersion, int LatestPublishedVersion,
    string DataJson, string? Title, DateTime UpdatedAtUtc, bool Stale, byte[]? RowVersion);

public sealed record DraftDetail(
    Guid Id, string FormKey, string FormName, Guid FormDefVersionId, int FormVersion,
    int LatestPublishedVersion, string SchemaJson, string DataJson, string? Title,
    bool Stale, byte[]? RowVersion);

public sealed record DraftRebaseResult(
    Guid DraftId, int FormVersion, string DataJson, IReadOnlyList<string> RemovedFields,
    IReadOnlyList<string> ValidationErrors, byte[]? RowVersion);

public sealed record DraftPage(IReadOnlyList<DraftListItem> Items, int Total, int Page, int PageSize);

public interface IDraftService
{
    Task<DraftDetail> CreateAsync(Guid ownerId, string formKey, JsonElement data, string? title, CancellationToken ct = default);
    Task<DraftDetail> UpdateAsync(Guid ownerId, Guid draftId, JsonElement data, string? title, byte[]? rowVersion, CancellationToken ct = default);
    Task<DraftPage> ListAsync(Guid ownerId, int page, int pageSize, CancellationToken ct = default);
    Task<DraftDetail> GetAsync(Guid ownerId, Guid draftId, CancellationToken ct = default);
    Task<DraftRebaseResult> RebaseAsync(Guid ownerId, Guid draftId, int targetVersion,
        bool confirmRemovedValues, byte[]? rowVersion, CancellationToken ct = default);
    Task<SubmitFormResult> SubmitAsync(Guid ownerId, Guid draftId, string submissionKey,
        byte[]? rowVersion, CancellationToken ct = default);
    Task DeleteAsync(Guid ownerId, Guid draftId, CancellationToken ct = default);
}
