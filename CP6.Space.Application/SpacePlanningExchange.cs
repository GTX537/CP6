namespace CP6.Space.Application;

public interface ISpacePlanningExchangeService
{
    Task<SpacePlanningExchangeFile> ExportGlbAsync(
        Guid siteId,
        Guid branchId,
        CancellationToken cancellationToken = default);
}

public sealed record SpacePlanningExchangeFile(
    byte[] Content,
    string FileName,
    string ContentType,
    string SchemaVersion,
    string Sha256);
