using CP6.Core.Persistence;

namespace CP6.Tests.Persistence;

public sealed class DatabaseMigrationOwnershipTests
{
    [Theory]
    [InlineData(DatabaseProvider.SqlServer, DatabaseContextKind.Core, DatabaseContextKind.Core)]
    [InlineData(DatabaseProvider.SqlServer, DatabaseContextKind.Space, DatabaseContextKind.Space)]
    [InlineData(DatabaseProvider.SqlServer, DatabaseContextKind.IdentityPriority, DatabaseContextKind.Core)]
    [InlineData(DatabaseProvider.SqlServer, DatabaseContextKind.ErpIntegration, DatabaseContextKind.Core)]
    [InlineData(DatabaseProvider.PostgreSql, DatabaseContextKind.Core, DatabaseContextKind.Core)]
    [InlineData(DatabaseProvider.PostgreSql, DatabaseContextKind.Space, DatabaseContextKind.Space)]
    [InlineData(DatabaseProvider.PostgreSql, DatabaseContextKind.IdentityPriority, DatabaseContextKind.IdentityPriority)]
    [InlineData(DatabaseProvider.PostgreSql, DatabaseContextKind.ErpIntegration, DatabaseContextKind.ErpIntegration)]
    public void Installation_uses_the_actual_owner_without_replaying_sql_queue_templates(
        DatabaseProvider provider, DatabaseContextKind context, DatabaseContextKind expectedOwner)
    {
        var profile = DatabaseMigrationProfile.For(new DatabaseOptions(provider), context);
        Assert.Equal(expectedOwner, profile.MigrationOwner);
        Assert.Equal(provider == DatabaseProvider.PostgreSql ? "public" : null, profile.HistorySchema);
    }
}
