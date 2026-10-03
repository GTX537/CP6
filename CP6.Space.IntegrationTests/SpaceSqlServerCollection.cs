namespace CP6.Space.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SpaceSqlServerCollection : ICollectionFixture<SpaceRelationalFixture>
{
    public const string Name = "Space SQL Server";
}
