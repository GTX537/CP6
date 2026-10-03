using CP6.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Persistence;

public sealed class DatabaseFailureClassifierTests
{
    [Theory]
    [InlineData("23505", DatabaseFailureKind.UniqueConstraint, false)]
    [InlineData("23503", DatabaseFailureKind.ForeignKey, false)]
    [InlineData("23001", DatabaseFailureKind.ForeignKey, false)]
    [InlineData("23514", DatabaseFailureKind.CheckConstraint, false)]
    [InlineData("40P01", DatabaseFailureKind.Deadlock, true)]
    [InlineData("40001", DatabaseFailureKind.SerializationFailure, true)]
    public void PostgreSql_sqlstate_controls_kind_and_whole_transaction_retry(
        string sqlState, DatabaseFailureKind expectedKind, bool expectedRetry)
    {
        var failure = DatabaseFailureClassifier.Classify(PostgreSql(sqlState));

        Assert.Equal(expectedKind, failure.Kind);
        Assert.Equal(sqlState, failure.SqlState);
        Assert.Null(failure.DatabaseErrorCode);
        Assert.Equal(expectedRetry, failure.CanRetryTransaction);
    }

    [Theory]
    [InlineData("23505", DatabaseFailureKind.UniqueConstraint)]
    [InlineData("23503", DatabaseFailureKind.ForeignKey)]
    [InlineData("23001", DatabaseFailureKind.ForeignKey)]
    [InlineData("23514", DatabaseFailureKind.CheckConstraint)]
    public void PostgreSql_constraint_identity_comes_from_the_provider_field(
        string sqlState, DatabaseFailureKind expectedKind)
    {
        var exception = PostgreSql(sqlState, "Exact_Constraint", "Other_Constraint appears in prose");

        var failure = DatabaseFailureClassifier.Classify(exception);

        Assert.Equal(expectedKind, failure.Kind);
        Assert.Equal("Exact_Constraint", failure.ConstraintName);
        Assert.True(failure.MatchesConstraint("Exact_Constraint"));
        Assert.False(failure.MatchesConstraint("Other_Constraint"));
        Assert.False(failure.CanRetryTransaction);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void DbUpdateException_and_generic_inner_wrappers_retain_provider_details(int depth)
    {
        Exception exception = PostgreSql("23505", "UX_DocNumber_Tenant_Type");
        for (var index = 0; index < depth; index++)
            exception = new InvalidOperationException("Wrapper", exception);
        exception = new DbUpdateException("Save failed", exception);

        var failure = DatabaseFailureClassifier.Classify(exception);

        Assert.Equal(DatabaseFailureKind.UniqueConstraint, failure.Kind);
        Assert.Equal("23505", failure.SqlState);
        Assert.Equal("UX_DocNumber_Tenant_Type", failure.ConstraintName);
        Assert.False(failure.CanRetryTransaction);
    }

    [Theory]
    [InlineData("08006")]
    [InlineData("57014")]
    [InlineData("53300")]
    [InlineData("23P01")]
    public void Other_PostgreSql_codes_stay_unknown_and_are_not_automatic_transaction_retries(string sqlState)
    {
        var failure = DatabaseFailureClassifier.Classify(PostgreSql(sqlState));

        Assert.Equal(DatabaseFailureKind.Unknown, failure.Kind);
        Assert.Equal(sqlState, failure.SqlState);
        Assert.Null(failure.DatabaseErrorCode);
        Assert.False(failure.CanRetryTransaction);
    }

    [Fact]
    public void Missing_constraint_name_is_not_guessed_from_PostgreSql_message()
    {
        var failure = DatabaseFailureClassifier.Classify(
            PostgreSql("23505", message: "duplicate key violates unique constraint \"UX_From_Message\""));

        Assert.Equal(DatabaseFailureKind.UniqueConstraint, failure.Kind);
        Assert.Null(failure.ConstraintName);
        Assert.False(failure.MatchesConstraint("UX_From_Message"));
    }

    [Fact]
    public void Explicit_EF_optimistic_concurrency_is_not_a_default_whole_transaction_retry()
    {
        var exception = new InvalidOperationException(
            "Outer wrapper", new DbUpdateConcurrencyException("Stale entity"));

        var failure = DatabaseFailureClassifier.Classify(exception);

        Assert.Equal(DatabaseFailureKind.OptimisticConcurrency, failure.Kind);
        Assert.Null(failure.ConstraintName);
        Assert.Null(failure.SqlState);
        Assert.Null(failure.DatabaseErrorCode);
        Assert.False(failure.CanRetryTransaction);
    }

    [Fact]
    public void Explicit_EF_concurrency_takes_precedence_over_inner_retry_code_but_retains_it()
    {
        var exception = new DbUpdateConcurrencyException("Stale entity", PostgreSql("40001"));

        var failure = DatabaseFailureClassifier.Classify(exception);

        Assert.Equal(DatabaseFailureKind.OptimisticConcurrency, failure.Kind);
        Assert.Equal("40001", failure.SqlState);
        Assert.False(failure.CanRetryTransaction);
    }

    [Fact]
    public void Typed_SQL_application_lock_deadlock_is_retryable_without_fabricating_error_1205()
    {
        var nativeResult = new DatabaseResourceLockDeadlockException(DatabaseProvider.SqlServer);
        var exception = new InvalidOperationException("Acquisition wrapper", nativeResult);

        var failure = DatabaseFailureClassifier.Classify(exception);

        Assert.Equal(DatabaseProvider.SqlServer, nativeResult.Provider);
        Assert.Equal(DatabaseFailureKind.Deadlock, failure.Kind);
        Assert.True(failure.CanRetryTransaction);
        Assert.Null(failure.ConstraintName);
        Assert.Null(failure.SqlState);
        Assert.Null(failure.DatabaseErrorCode);
    }

    [Fact]
    public void Broad_DbUpdateException_does_not_imply_retry_or_integrity_failure()
    {
        var failure = DatabaseFailureClassifier.Classify(new DbUpdateException("Save failed"));

        Assert.Equal(DatabaseFailureKind.Unknown, failure.Kind);
        Assert.Null(failure.ConstraintName);
        Assert.Null(failure.SqlState);
        Assert.Null(failure.DatabaseErrorCode);
        Assert.False(failure.CanRetryTransaction);
    }

    [Theory]
    [InlineData("23505 duplicate key constraint \"UX_DocNumber\"")]
    [InlineData("1205 deadlock victim")]
    [InlineData("The INSERT statement conflicted with the FOREIGN KEY constraint \"FK_Test\".")]
    public void Generic_exception_message_never_impersonates_a_provider_error(string message)
    {
        var failure = DatabaseFailureClassifier.Classify(
            new DbUpdateException("Save failed", new InvalidOperationException(message)));

        Assert.Equal(DatabaseFailureKind.Unknown, failure.Kind);
        Assert.Null(failure.ConstraintName);
        Assert.Null(failure.SqlState);
        Assert.Null(failure.DatabaseErrorCode);
        Assert.False(failure.CanRetryTransaction);
    }

    [Theory]
    [InlineData(DatabaseFailureKind.UniqueConstraint, false)]
    [InlineData(DatabaseFailureKind.ForeignKey, false)]
    [InlineData(DatabaseFailureKind.CheckConstraint, false)]
    [InlineData(DatabaseFailureKind.Deadlock, true)]
    [InlineData(DatabaseFailureKind.SerializationFailure, true)]
    [InlineData(DatabaseFailureKind.OptimisticConcurrency, false)]
    [InlineData(DatabaseFailureKind.UnknownIntegrity, false)]
    [InlineData(DatabaseFailureKind.Unknown, false)]
    public void Default_retry_policy_is_limited_to_deadlock_or_serialization(
        DatabaseFailureKind kind, bool expectedRetry)
    {
        var failure = new DatabaseFailure(kind, null, null, null);

        Assert.Equal(expectedRetry, failure.CanRetryTransaction);
    }

    [Theory]
    [InlineData("UX_Exact", "UX_Exact", true)]
    [InlineData("UX_Exact", "UX_Exact_Extra", false)]
    [InlineData("UX_Exact", "ux_exact", false)]
    [InlineData("UX_Exact", "UX_Exact ", false)]
    [InlineData("UX_Exact", "", false)]
    [InlineData("UX_Exact", null, false)]
    [InlineData(null, "UX_Exact", false)]
    [InlineData("", "", false)]
    public void Constraint_match_is_exact_ordinal_and_never_empty(
        string? constraintName, string? requestedName, bool expectedMatch)
    {
        var failure = new DatabaseFailure(DatabaseFailureKind.UniqueConstraint, constraintName, "23505", null);

        Assert.Equal(expectedMatch, failure.MatchesConstraint(requestedName));
    }

    [Fact]
    public void Null_exception_is_rejected_as_an_argument_error()
    {
        var error = Assert.Throws<ArgumentNullException>(() => DatabaseFailureClassifier.Classify(null!));

        Assert.Equal("exception", error.ParamName);
    }

    private static PostgresException PostgreSql(
        string sqlState, string? constraintName = null, string message = "Provider-reported fixture") =>
        new(message, "ERROR", "ERROR", sqlState, constraintName: constraintName);
}
