using CP6.Space.Application;
using CP6.Space.Domain;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.Infrastructure;

public sealed class EfSpaceFileCatalog : ISpaceFileCatalog
{
    private readonly SpaceContext _context;

    public EfSpaceFileCatalog(SpaceContext context)
    {
        _context = context;
    }

    public Task<SpaceFile?> FindReusableAsync(
        Guid tenantId,
        string sha256,
        SpaceFileRetentionClass retentionClass,
        CancellationToken cancellationToken = default) =>
        _context.Files
            .AsNoTracking()
            .SingleOrDefaultAsync(
                file =>
                    file.TenantId == tenantId &&
                    file.Sha256 == sha256 &&
                    file.RetentionClass == retentionClass &&
                    (file.State == SpaceFileState.Quarantined ||
                     file.State == SpaceFileState.Scanning ||
                     file.State == SpaceFileState.Clean),
                cancellationToken);

    public async Task AddQuarantinedWithScanJobAsync(
        SpaceFile file,
        SpaceJob scanJob,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(scanJob);
        if (file.TenantId != _context.CurrentTenantId ||
            scanJob.TenantId != _context.CurrentTenantId ||
            scanJob.JobType != SpaceJobType.FileScan ||
            scanJob.SubjectType != SpaceJobSubjectType.File ||
            scanJob.SubjectId != file.Id ||
            scanJob.InputHash != file.Sha256 ||
            file.State != SpaceFileState.Quarantined)
        {
            throw new SpaceTenantScopeException(
                "The quarantined file and its scan Job must share the verified tenant and subject.");
        }

        _context.Files.Add(file);
        _context.Jobs.Add(scanJob);
        await _context.SaveChangesAsync(cancellationToken);
    }
}

public sealed class EfSpaceSourceCatalog : ISpaceSourceCatalog
{
    private readonly SpaceContext _context;

    public EfSpaceSourceCatalog(SpaceContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<SpaceModelSource>> FindByHashAsync(
        Guid tenantId,
        string sha256,
        CancellationToken cancellationToken = default) =>
        await _context.Sources
            .AsNoTracking()
            .Where(source =>
                source.TenantId == tenantId &&
                source.Sha256 == sha256)
            .OrderBy(source => source.CreatedAtUtc)
            .ThenBy(source => source.Id)
            .ToListAsync(cancellationToken);
}
