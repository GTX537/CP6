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

    public async Task<int> CountActiveReferencesAsync(
        Guid tenantId,
        Guid fileId,
        CancellationToken cancellationToken = default)
    {
        var sources = await _context.Sources.CountAsync(
            source => source.TenantId == tenantId && source.FileId == fileId,
            cancellationToken);
        var artifacts = await _context.Artifacts.CountAsync(
            artifact => artifact.TenantId == tenantId && artifact.FileId == fileId,
            cancellationToken);
        return checked(sources + artifacts);
    }

    public void Add(SpaceFile file)
    {
        _context.Files.Add(file);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);
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
