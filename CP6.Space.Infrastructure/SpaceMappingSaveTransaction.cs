using System.Transactions;
using CP6.Core.Persistence;
using CP6.Space.Application;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.Infrastructure;

internal static class SpaceMappingSaveTransaction
{
    private const string IdempotencyConstraint =
        "UX_Space_IdempotencyRecord_Tenant_Principal_Operation_Key";

    public static async Task<T> ExecuteAsync<T>(
        SpaceContext context,
        Func<Task<T>> saveAttempt,
        Func<Task<T?>> readReplay,
        Func<SpaceProblemException> conflict,
        string currentNameConstraint,
        string versionConstraint,
        CancellationToken cancellationToken)
        where T : class
    {
        var relational = context.Database.IsRelational();
        if (relational)
        {
            if (context.Database.CurrentTransaction is not null ||
                System.Transactions.Transaction.Current is not null ||
                context.Database.GetEnlistedTransaction() is not null)
            {
                throw new InvalidOperationException(
                    "Mapping saves require their own transaction; a caller transaction cannot be retried or committed here.");
            }

            context.ChangeTracker.DetectChanges();
            if (context.ChangeTracker.HasChanges())
            {
                throw new InvalidOperationException(
                    "Mapping saves require a clean context so caller changes are not saved or discarded by transaction recovery.");
            }
        }

        const int maximumAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await saveAttempt();
            }
            catch (Exception exception) when (relational &&
                DatabaseFailureClassifier.Classify(exception).CanRetryTransaction)
            {
                RequireReleasedTransaction(context, exception);
                context.ChangeTracker.Clear();
                if (attempt >= maximumAttempts)
                    throw;
            }
            catch (Exception exception) when (IsMappingConflict(
                exception, currentNameConstraint, versionConstraint))
            {
                RequireReleasedTransaction(context, exception);
                context.ChangeTracker.Clear();
                return await readReplay() ?? throw conflict();
            }
        }
    }

    private static bool IsMappingConflict(Exception exception, string currentNameConstraint, string versionConstraint)
    {
        var failure = DatabaseFailureClassifier.Classify(exception);
        return failure.Kind == DatabaseFailureKind.OptimisticConcurrency ||
            failure.Kind == DatabaseFailureKind.UniqueConstraint &&
            (failure.MatchesConstraint(currentNameConstraint) ||
             failure.MatchesConstraint(versionConstraint) ||
             failure.MatchesConstraint(IdempotencyConstraint));
    }

    private static void RequireReleasedTransaction(SpaceContext context, Exception exception)
    {
        if (context.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException(
                "Mapping transaction recovery requires the failed attempt to finish rollback and disposal first.", exception);
        }
    }
}
