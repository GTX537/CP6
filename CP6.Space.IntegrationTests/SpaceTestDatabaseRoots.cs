using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Space.IntegrationTests;

internal static class SpaceTestDatabaseRoots
{
    internal static readonly InMemoryDatabaseRoot InMemory = new();
}
