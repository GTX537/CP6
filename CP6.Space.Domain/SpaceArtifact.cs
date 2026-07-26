namespace CP6.Space.Domain;

public sealed class SpaceArtifact : SpaceTenantEntity
{
    private SpaceArtifact()
    {
    }

    public Guid ModelVersionId { get; private set; }
    public Guid? SourceId { get; private set; }
    public Guid FileId { get; private set; }
    public Guid? JobId { get; private set; }
    public SpaceArtifactType ArtifactType { get; private set; }
    public string SchemaVersion { get; private set; } = string.Empty;

    public static SpaceArtifact Create(
        Guid tenantId,
        Guid modelVersionId,
        SpaceModelSource? source,
        SpaceFile file,
        SpaceArtifactType artifactType,
        string schemaVersion)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (modelVersionId == Guid.Empty)
            throw new ArgumentException("Model version is required.", nameof(modelVersionId));
        if (file.TenantId != tenantId)
            throw new SpaceTenantScopeException("Artifact and file tenants must match.");
        if (file.State != SpaceFileState.Clean || file.IsDeleted)
            throw new SpaceFileStateException("An artifact requires a clean file.");
        if (file.RetentionClass != SpaceFileRetentionClass.Artifact)
            throw new SpaceFileStateException("An artifact requires Artifact retention.");
        if (source is not null &&
            (source.TenantId != tenantId || source.ModelVersionId != modelVersionId))
        {
            throw new SpaceTenantScopeException(
                "Artifact source must belong to the same tenant and model version.");
        }

        var artifact = new SpaceArtifact
        {
            ModelVersionId = modelVersionId,
            SourceId = source?.Id,
            FileId = file.Id,
            ArtifactType = artifactType,
            SchemaVersion = RequireSchemaVersion(schemaVersion),
        };
        artifact.SetTenant(tenantId);
        return artifact;
    }

    public void AttachToJob(SpaceJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (job.TenantId != TenantId)
            throw new SpaceTenantScopeException(
                "Artifact and Job tenants must match.");
        if (JobId.HasValue && JobId != job.Id)
            throw new SpaceJobStateException(
                "Artifact Job lineage cannot be reassigned.");

        JobId = job.Id;
    }

    private static string RequireSchemaVersion(string value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 50)
            throw new ArgumentException(
                "Schema version is required and cannot exceed 50 characters.",
                nameof(value));
        return normalized;
    }
}
