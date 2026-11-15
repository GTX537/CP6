using CP6.Core.Persistence;
using CP6.Space.Application;
using CP6.Space.Contracts;
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
        UsesAuthority ? await BeginAuthorizedAsync(cancellationToken) : null;

    internal Task CommitRegistrationAsync(IDbContextTransaction transaction, CancellationToken cancellationToken) =>
        SpaceDesignWriteTransaction.CommitAsync(context, authority, transaction, cancellationToken);

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
