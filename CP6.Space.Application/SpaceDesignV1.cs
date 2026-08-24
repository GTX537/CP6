using CP6.Space.Contracts;

namespace CP6.Space.Application;

public sealed class SpaceProblemException : Exception
{
    public SpaceProblemException(
        string code,
        int statusCode,
        string title,
        string? detail = null,
        string recoveryAction = "contact-support",
        bool retryable = false)
        : base(detail ?? title)
    {
        Code = code;
        StatusCode = statusCode;
        Title = title;
        Detail = detail;
        RecoveryAction = recoveryAction;
        Retryable = retryable;
    }

    public string Code { get; }
    public int StatusCode { get; }
    public string Title { get; }
    public string? Detail { get; }
    public string RecoveryAction { get; }
    public bool Retryable { get; }
}

public sealed record SpaceCursorState(
    string Resource,
    string FilterHash,
    int Offset);

public interface ISpaceCursorCodec
{
    string Encode(SpaceCursorState state);

    SpaceCursorState Decode(
        string cursor,
        string expectedResource,
        string expectedFilterHash);
}

public interface ISpaceDesignAccessEvaluator
{
    void EnsureSiteAccess(Guid siteId, bool write);
}

public interface ISpaceDesignV1Service
{
    Task<SpaceModelDto> GetModelAsync(
        Guid siteId,
        CancellationToken cancellationToken = default);

    Task<SpacePage<SpaceVersionDto>> GetVersionsAsync(
        Guid siteId,
        string? status,
        int limit,
        string? cursor,
        CancellationToken cancellationToken = default);

    Task<SpaceVersionDto> GetVersionAsync(
        Guid versionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SpaceSceneFloorDto>> GetFloorsAsync(
        Guid versionId,
        CancellationToken cancellationToken = default);

    Task<CreateSpaceFloorResponse> CreateFloorAsync(
        Guid versionId,
        CreateSpaceFloorRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<SpaceDesignSceneDto> GetSceneAsync(
        Guid versionId,
        Guid floorLogicalId,
        CancellationToken cancellationToken = default);

    Task<SpacePublishedViewerSceneDto> GetPublishedSceneAsync(
        Guid siteId,
        CancellationToken cancellationToken = default);

    Task<ApplySpaceElementCommandBatchResponse> ApplyElementCommandsAsync(
        Guid versionId,
        Guid floorLogicalId,
        ApplySpaceElementCommandBatchRequest request,
        CancellationToken cancellationToken = default);

    Task<ApplySpaceElementCommandBatchResponse> ApplyCadElementCommandsAsync(
        Guid versionId,
        Guid floorLogicalId,
        ApplySpaceElementCommandBatchRequest request,
        CancellationToken cancellationToken = default);

    Task<ApplySpaceLayoutCommandBatchResponse> ApplyLayoutCommandsAsync(
        Guid versionId,
        Guid floorLogicalId,
        ApplySpaceLayoutCommandBatchRequest request,
        CancellationToken cancellationToken = default);

    Task<SpacePage<SpaceAssetDto>> GetAssetsAsync(
        string? scope,
        string? category,
        int limit,
        string? cursor,
        CancellationToken cancellationToken = default);

    Task<CreateSpaceAssetResponse> CreateAssetAsync(
        CreateSpaceAssetRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SpaceWarehouseTemplateDto>> GetWarehouseTemplatesAsync(
        string? scope,
        CancellationToken cancellationToken = default);

    Task<CreateTenantSpaceWarehouseTemplateResponse>
        CreateTenantWarehouseTemplateAsync(
            CreateTenantSpaceWarehouseTemplateRequest request,
            string idempotencyKey,
            CancellationToken cancellationToken = default);

    Task<SpaceWarehouseTemplateInstantiationPreviewDto>
        PreviewWarehouseTemplateAsync(
            Guid templateId,
            PreviewSpaceWarehouseTemplateRequest request,
            CancellationToken cancellationToken = default);

    Task<ApplySpaceWarehouseTemplateFloorResponse>
        ApplyWarehouseTemplateFloorAsync(
            Guid versionId,
            Guid floorLogicalId,
            Guid templateId,
            ApplySpaceWarehouseTemplateFloorRequest request,
            CancellationToken cancellationToken = default);

    Task<CreateSpaceVersionResponse> CreateVersionAsync(
        Guid siteId,
        CreateSpaceVersionRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<SpacePage<SpaceSourceDto>> GetSourcesAsync(
        Guid versionId,
        string? sourceType,
        string? state,
        int limit,
        string? cursor,
        CancellationToken cancellationToken = default);

    Task<CreateSpaceSourceResponse> CreateSourceAsync(
        Guid versionId,
        CreateSpaceSourceRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<SpaceSourceRemovalPreviewDto> GetSourceRemovalPreviewAsync(
        Guid versionId,
        Guid sourceId,
        CancellationToken cancellationToken = default);

    Task<RemoveSpaceSourceResponse> RemoveSourceAsync(
        Guid versionId,
        Guid sourceId,
        RemoveSpaceSourceRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<SpaceJobDto> GetJobAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);

    Task<SpacePage<SpaceIssueDto>> GetIssuesAsync(
        Guid versionId,
        string? severity,
        string? status,
        int limit,
        string? cursor,
        CancellationToken cancellationToken = default);
}

public sealed record SpaceLocationCodingRuleDefinition(
    Guid RuleId,
    string RuleName,
    int ScopeType,
    Guid? ScopeId,
    IReadOnlyList<SpaceLocationCodeSegmentDto> Segments,
    bool IsDefault = false,
    string? ScopeFloorCode = null,
    string? ScopeZoneCode = null);

public sealed record SpaceLocationCodingCatalog(
    string? SiteCode,
    IReadOnlyList<SpaceLocationCodingRuleDefinition> Rules);

public interface ISpaceLocationCodeRuleProvider
{
    Task<SpaceLocationCodingCatalog> GetCatalogAsync(
        Guid siteId,
        CancellationToken cancellationToken = default);
}

public interface ISpaceDesignCodingService
{
    Task<PreviewSpaceLocationCodesResponse> PreviewLocationCodesAsync(
        Guid versionId,
        Guid floorLogicalId,
        PreviewSpaceLocationCodesRequest request,
        CancellationToken cancellationToken = default);

    Task<ApplySpaceLocationCodesResponse> ApplyLocationCodesAsync(
        Guid versionId,
        Guid floorLogicalId,
        ApplySpaceLocationCodesRequest request,
        CancellationToken cancellationToken = default);
}
