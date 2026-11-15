using CP6.Core.Persistence;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Space.Infrastructure;

internal sealed class SpaceSourceUploadWriteTransaction(
    SpaceContext context,
    ISpaceDesignWriteAuthority? authority,
    ISpaceFileStore? files,
    Func<SpaceProblemException>? authorityConflict = null)
{
    internal const string UploadPermission = "space:source:upload";

    private bool UsesAuthority => authority is not null && context.Database.IsNpgsql();

    internal Task<SpaceFileUploadResult> UploadAsync(
        SpaceFileUploadService uploads,
        SpaceFileUploadRequest request,
        Stream content,
        CancellationToken cancellationToken)
    {
        if (!UsesAuthority)
            return uploads.UploadAsync(request, content, cancellationToken);

        var fileStore = files ?? throw new InvalidOperationException("The Space source upload file store is required.");
        return uploads.UploadAsync(request, content,
            (saveCatalog, publishContent, token) => CompleteFileAsync(fileStore, saveCatalog, publishContent, token),
            cancellationToken);
    }

    internal async Task<IDbContextTransaction?> BeginRegistrationAsync(CancellationToken cancellationToken) =>
        context.Database.IsNpgsql() ? await BeginAuthorizedAsync(cancellationToken) : null;

    internal Task CommitRegistrationAsync(IDbContextTransaction transaction, CancellationToken cancellationToken) =>
        SpaceDesignWriteTransaction.CommitAsync(context, authority, transaction, cancellationToken);

    internal async Task<(SpaceModelSource Source, bool Reused)> SaveSourceAsync(
        SpaceSourceCoordinator sources, SpaceModelVersion version, SpaceFile uploadedFile, SpaceSourceType type,
        string displayName, Func<SpaceProblemException> rejectedSource, CancellationToken cancellationToken)
    {
        var postgres = context.Database.IsNpgsql();
        var candidates = context.Sources.Where(source => source.ModelVersionId == version.Id &&
            source.Sha256 == uploadedFile.Sha256 && source.SourceType == type);
        var source = await (postgres ? candidates.AsNoTracking() : candidates).SingleOrDefaultAsync(cancellationToken);
        if (source?.State == SpaceSourceState.Rejected)
            throw rejectedSource();
        if (source is not null && source.State != SpaceSourceState.Scanning)
            return (source, true);

        var currentFile = uploadedFile;
        if (postgres)
        {
            // FileScan locks a file before updating its Sources. The existing Source owns its original FileId.
            var fileId = source is null ? uploadedFile.Id : source.FileId
                ?? throw new InvalidOperationException("A scanning source requires its original file.");
            currentFile = await SpaceFileReferenceLock.LoadAsync(context, version.TenantId, fileId,
                includeDeleted: false, cancellationToken)
                ?? throw new SpaceProblemException(SpaceErrorCodes.FileNotFound, 404, "The source file was not found.");
            if (source is not null)
            {
                var sourceId = source.Id;
                source = await context.Sources.FromSqlInterpolated($"""
                    SELECT s.* FROM public."Space_ModelSource" s
                    WHERE s."TenantId" = {version.TenantId} AND s."Id" = {sourceId}
                    FOR UPDATE
                    """).SingleAsync(cancellationToken);
            }
        }

        var reused = source is not null;
        if (source is null)
        {
            // An upload can carry its original quarantined snapshot after FileScan has rejected the catalog row.
            source = currentFile.State == SpaceFileState.Clean
                ? sources.AddFileSource(version, currentFile, type, displayName)
                : sources.AddPendingFileSource(version,
                    currentFile.State == SpaceFileState.Rejected ? uploadedFile : currentFile, type, displayName);
            context.Sources.Add(source);
        }

        var completedScan = postgres && source.State == SpaceSourceState.Scanning &&
            currentFile.State is SpaceFileState.Clean or SpaceFileState.Rejected;
        if (completedScan)
            source.CompleteFileScan(currentFile);
        if (!reused || completedScan)
            await context.SaveChangesAsync(cancellationToken);
        return (source, reused);
    }

    internal async Task<(SpaceFile File, SpaceModelSource Source)> ResolveUploadResponseAsync(
        SpaceModelSource source, Guid uploadedFileId, CancellationToken cancellationToken)
    {
        if (!context.Database.IsNpgsql())
        {
            // Preserve the existing completion path for other providers.
            if (source.State == SpaceSourceState.Scanning)
            {
                var file = await context.Files.SingleAsync(item => item.Id == uploadedFileId, cancellationToken);
                await context.Entry(file).ReloadAsync(cancellationToken);
                if (file.State is SpaceFileState.Clean or SpaceFileState.Rejected)
                {
                    source.CompleteFileScan(file);
                    await context.SaveChangesAsync(cancellationToken);
                }
            }
            return (await context.Files.AsNoTracking().SingleAsync(item => item.Id == uploadedFileId, cancellationToken), source);
        }

        var originalFileId = source.FileId ?? throw new InvalidOperationException("A registered upload source requires its original file.");
        var registration = context.Sources.AsNoTracking().Where(item => item.TenantId == source.TenantId && item.Id == source.Id);
        var current = await (from file in context.Files.AsNoTracking()
                             where file.TenantId == source.TenantId && file.Id == originalFileId
                             from registered in registration.DefaultIfEmpty()
                             select new { File = file, Source = registered }).SingleAsync(cancellationToken);
        // A later authorized removal can hide the current row after the original registration committed.
        return (current.File, current.Source ?? source);
    }

    private async Task<SpaceFileUploadResult> CompleteFileAsync(
        ISpaceFileStore fileStore,
        Func<CancellationToken, Task<SpaceFileUploadResult>> saveCatalog,
        Func<CancellationToken, Task> publishContent,
        CancellationToken cancellationToken)
    {
        await using var transaction = await BeginAuthorizedAsync(cancellationToken);
        var upload = await saveCatalog(cancellationToken);
        if (!upload.Reused)
            await publishContent(cancellationToken);

        try
        {
            await authority!.ConfirmSessionAsync(context, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch
        {
            if (!upload.Reused)
                await fileStore.DeleteAsync(upload.File.TenantId, upload.File.Id, upload.File.StorageKey, CancellationToken.None);
            throw;
        }

        // A failed acknowledgement may follow a committed catalog row that owns this content.
        await transaction.CommitAsync(cancellationToken);
        return upload;
    }

    private async Task<IDbContextTransaction> BeginAuthorizedAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await SpaceDesignWriteTransaction.BeginAsync(context, authority, cancellationToken, UploadPermission);
            }
            catch (Exception error) when (DatabaseFailureClassifier.Classify(error).CanRetryTransaction)
            {
                if (attempt == 3)
                    throw authorityConflict?.Invoke() ?? new SpaceProblemException(
                        SpaceErrorCodes.ConcurrencyConflict, 409, "The Space write authority changed concurrently.",
                        recoveryAction: "reload-source-and-retry", retryable: true);
            }
        }
    }
}
