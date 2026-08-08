namespace CP6.Core.Services.Wms;

public interface ILabelJobService
{
    Task<IReadOnlyList<LabelTemplateDto>> GetTemplatesAsync(CancellationToken ct = default);
    Task<LabelTemplateDto> UpsertTemplateAsync(UpsertLabelTemplateRequest request, string? userName, CancellationToken ct = default);
    Task<PagedResult<LabelJobDto>> GetJobsAsync(string? status, string? warehouseCd, int page, int pageSize, CancellationToken ct = default);
    Task<LabelJobDto> CreateJobAsync(CreateLabelJobRequest request, string? userName, CancellationToken ct = default);
    Task<LabelJobDto> ClaimAsync(string jobNo, LabelJobCommand request, string? userName, CancellationToken ct = default);
    Task<LabelJobDto> CompleteAsync(string jobNo, LabelJobCommand request, bool success, string? userName, CancellationToken ct = default);
}
